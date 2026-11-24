using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Sep2026.Application.DTOs;
using Sep2026.Application.Exceptions;
using Sep2026.Application.Interfaces;
using Sep2026.Application.Security;
using Sep2026.Application.Services;
using Sep2026.Domain;
using Sep2026.Infrastructure.Persistence;
using Sep2026.Infrastructure.Persistence.Entities;
using Sep2026.Infrastructure.Persistence.Stores;
using Sep2026.Infrastructure.Persistence.Repositories;
using Sep2026.Infrastructure.Persistence.Queries;
using Xunit;

namespace Sep2026.Infrastructure.Tests;

public sealed class LinkedPortfolioStoreTests : IClassFixture<SqlServerFixture>
{
    private readonly SqlServerFixture fixture;
    public LinkedPortfolioStoreTests(SqlServerFixture fixture) => this.fixture = fixture;

    [SqlServerFact]
    public async Task LinksFractionalUnknownCostAndReturnsTheSamePortfolioOnRepeatedLink()
    {
        var owner = await OwnerAsync();
        await using var context = fixture.CreateContext();
        var store = new SqlServerLinkedPortfolioStore(context);
        var vault = new FakeCredentials();
        var service = new LinkPortfolioService(new FakeProvider(), store, vault);
        var request = new LinkPortfolioRequest(InvestmentProvider.Alpaca, ProviderEnvironment.Paper, "My account");
        var first = await service.LinkAsync(owner, request, new("id", "secret"));
        var second = await service.LinkAsync(owner, request with { DisplayName = "Must not overwrite" }, new("other", "other"));
        Assert.Equal(LinkPortfolioOutcome.Linked, first.Outcome);
        Assert.Equal(LinkPortfolioOutcome.AlreadyLinked, second.Outcome);
        Assert.Equal(first.Portfolio.PortfolioId, second.Portfolio.PortfolioId);
        Assert.Equal("My account", second.Portfolio.DisplayName);
        Assert.Equal(1, vault.Saves);
        Assert.Equal(1, await context.Portfolios.CountAsync(item => item.OwnerId == owner));
        var row = await context.Portfolios.Include(item => item.Holdings).SingleAsync(item => item.OwnerId == owner);
        Assert.Equal(123.45m, row.CashBalance);
        Assert.Equal((byte)PortfolioType.Unspecified, row.PortfolioType);
        Assert.Equal(1.123456789m, Assert.Single(row.Holdings).ShareCount);
        Assert.Null(row.Holdings.Single().AveragePurchasePrice);
        var link = await context.ProviderConnections.SingleAsync(item => item.OwnerId == owner);
        Assert.Equal(vault.Reference, link.CredentialReference);
        Assert.NotEmpty(link.RowVersion);
        Assert.NotEmpty(row.RowVersion);
        var restored = await new PortfolioRepository(context).GetByIdAsync(row.PortfolioId);
        Assert.Equal(1.123456789m, Assert.Single(restored!.Holdings).ShareCount);
        Assert.Null(restored.Holdings.Single().AveragePurchasePrice);
        var queries = new PortfolioQueries(context);
        var summary = Assert.Single(await queries.GetByUserIdAsync(owner));
        Assert.Equal("My account", summary.DisplayName);
        Assert.Equal(InvestmentProvider.Alpaca, summary.Connection!.Provider);
        Assert.Equal(ProviderEnvironment.Paper, summary.Connection.Environment);
        var detail = await new PortfolioService(new PortfolioRepository(context), queries).GetByIdAsync(row.PortfolioId);
        Assert.Equal("My account", detail!.DisplayName);
        Assert.Equal(link.LastSuccessfulSyncAt, detail.Connection!.LastSuccessfulSyncAt);
        Assert.Null((await queries.GetConnectionAsync(Guid.NewGuid(), row.PortfolioId)).Connection);
    }

    [SqlServerFact]
    public async Task AccountLocksAreExclusiveAndReleaseWithoutPoolingLeak()
    {
        var owner = await OwnerAsync();
        await using var firstContext = fixture.CreateContext();
        await using var secondContext = fixture.CreateContext();
        var first = new SqlServerLinkedPortfolioStore(firstContext);
        var second = new SqlServerLinkedPortfolioStore(secondContext);
        var lockOne = await first.AcquireLinkLockAsync(owner, InvestmentProvider.Alpaca, ProviderEnvironment.Paper, "exclusive");
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => second.AcquireLinkLockAsync(owner, InvestmentProvider.Alpaca, ProviderEnvironment.Paper, "exclusive"));
            // Another owner's same opaque external ID is a different lock scope.
            await using var unrelated = await second.AcquireLinkLockAsync(Guid.NewGuid(), InvestmentProvider.Alpaca, ProviderEnvironment.Paper, "exclusive");
        }
        finally { await lockOne.DisposeAsync(); }
        await using var reacquired = await second.AcquireLinkLockAsync(owner, InvestmentProvider.Alpaca, ProviderEnvironment.Paper, "exclusive");
    }

    [SqlServerFact]
    public async Task FailedImportRollsBackThePortfolioHoldingsAndSecurityRegistry()
    {
        var owner = await OwnerAsync();
        await using var context = fixture.CreateContext();
        var store = new SqlServerLinkedPortfolioStore(context);
        await using var accountLock = await store.AcquireLinkLockAsync(owner, InvestmentProvider.Alpaca, ProviderEnvironment.Paper, "rollback");
        var symbol = "R" + Guid.NewGuid().ToString("N")[..10];
        var now = DateTimeOffset.UtcNow;
        var snapshot = new PortfolioSnapshot("rollback", CurrencyType.USD, 0, new[]
        { new PositionSnapshot(Guid.NewGuid().ToString(), symbol, "Stock", CurrencyType.USD, 1.25m, null) });
        var import = new InitialPortfolioImport(Guid.NewGuid(), Guid.NewGuid(), owner, InvestmentProvider.Alpaca,
            ProviderEnvironment.Paper, new string('x', 201), Guid.NewGuid(), snapshot, now, now, null, null);
        await Assert.ThrowsAsync<PortfolioLinkNotCommittedException>(() => store.CreateAsync(import, accountLock));
        Assert.False(await context.Portfolios.AnyAsync(item => item.OwnerId == owner));
        Assert.False(await context.ProviderConnections.AnyAsync(item => item.OwnerId == owner));
        Assert.False(await context.Securities.AnyAsync(item => item.TickerSymbol == symbol));
    }

    [SqlServerFact]
    public async Task OwnerIdentityAndSqlRelationshipsPreventCrossOwnerImports()
    {
        var owner = await OwnerAsync();
        var other = await OwnerAsync();
        await using var context = fixture.CreateContext();
        var store = new SqlServerLinkedPortfolioStore(context);
        var service = new LinkPortfolioService(new FakeProvider(), store, new FakeCredentials());
        await service.LinkAsync(owner, new(InvestmentProvider.Alpaca, ProviderEnvironment.Paper), new("id", "secret"));
        Assert.Null(await store.FindAsync(other, InvestmentProvider.Alpaca, ProviderEnvironment.Paper, "account"));
        // Use an unlinked portfolio so the portfolio-uniqueness constraint cannot
        // hide a missing/misconfigured owner foreign key in this test.
        var portfolio = new Portfolio { PortfolioId = Guid.NewGuid(), OwnerId = owner, CurrencyType = "USD", CreatedAt = DateTimeOffset.UtcNow };
        context.Portfolios.Add(portfolio);
        await context.SaveChangesAsync();
        var time = DateTimeOffset.UtcNow;
        context.ProviderConnections.Add(new ProviderConnection
        {
            ConnectionId = Guid.NewGuid(), OwnerId = other, PortfolioId = portfolio.PortfolioId,
            ExternalAccountId = "cross-owner", CredentialReference = Guid.NewGuid(),
            LastAttemptAt = time, LastSuccessfulSyncAt = time, FetchStartedAt = time, FetchCompletedAt = time, SyncGeneration = 1
        });
        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.Contains("FK_ProviderConnections_Portfolios", failure.InnerException!.Message);
    }

    private async Task<Guid> OwnerAsync()
    {
        await using var context = fixture.CreateContext();
        var id = Guid.NewGuid();
        context.Users.Add(new User { UserId = id, Name = "Test owner", Email = $"{id:N}@example.com" });
        await context.SaveChangesAsync();
        return id;
    }

    private sealed class FakeProvider : IInvestmentAccountProvider
    {
        public Task<string> GetAccountIdAsync(InvestmentProvider provider, ProviderEnvironment environment, ProviderCredentials credentials, CancellationToken cancellationToken = default)
            => Task.FromResult("account");
        public Task<ExternalPortfolioSnapshotDto> ReadSnapshotAsync(InvestmentProvider provider, ProviderEnvironment environment,
            string externalAccountId, ProviderCredentials credentials, CancellationToken cancellationToken = default)
        {
            var time = DateTimeOffset.UtcNow;
            return Task.FromResult(new ExternalPortfolioSnapshotDto(externalAccountId, "USD", 123.45m,
                new[] { new ExternalPositionDto("apple-asset", "AAPL", "us_equity", "USD", ExternalPositionSide.Long, 1.123456789m, null) }, time, time, true));
        }
    }
    private sealed class FakeCredentials : IProviderCredentialStore
    {
        public int Saves { get; private set; }
        public Guid Reference { get; private set; }
        public Task SaveAsync(Guid ownerId, Guid reference, ProviderCredentials credentials, CancellationToken cancellationToken = default)
        { Saves++; Reference = reference; return Task.CompletedTask; }
        public Task DeleteAsync(Guid ownerId, Guid reference, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
