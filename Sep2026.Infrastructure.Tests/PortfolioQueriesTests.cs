using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Sep2026.Domain;
using Sep2026.Infrastructure.Persistence;
using Sep2026.Infrastructure.Persistence.Entities;
using Sep2026.Infrastructure.Persistence.Queries;
using Sep2026.Infrastructure.Persistence.Repositories;
using Xunit;

namespace Sep2026.Infrastructure.Tests;

public class PortfolioQueryArgumentTests
{
    [Fact]
    public async Task EmptyUserIdsAreRejectedBeforeConnecting()
    {
        await using var context = CreateDisconnectedContext();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            new PortfolioQueries(context).GetByUserIdAsync(Guid.Empty));
    }

    [Fact]
    public async Task PreCanceledQueriesDoNotOpenAConnection()
    {
        await using var context = CreateDisconnectedContext();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new PortfolioQueries(context).GetByUserIdAsync(Guid.NewGuid(), new CancellationToken(true)));
    }

    private static GeminoDbContext CreateDisconnectedContext()
        => new(new DbContextOptionsBuilder<GeminoDbContext>()
            .UseSqlServer("Server=unreachable-test-server;Database=Gemino;Integrated Security=True;Connect Timeout=1;")
            .Options);
}

public class PortfolioQueriesSqlServerTests : IClassFixture<SqlServerFixture>
{
    private readonly SqlServerFixture fixture;

    public PortfolioQueriesSqlServerTests(SqlServerFixture fixture) => this.fixture = fixture;

    [SqlServerFact]
    public async Task FiltersByOwnerIncludesBothStatusesAndOrdersByDateThenId()
    {
        var commands = new CommandRecorder();
        await using var context = fixture.CreateContext(commands);
        var owner = CreateOwner();
        var otherOwner = CreateOwner();
        var savedAt = new DateTimeOffset(2024, 2, 3, 10, 30, 0, TimeSpan.FromHours(1));
        var first = CreatePortfolio(owner, savedAt, PortfolioStatus.Archived);
        first.PortfolioId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var second = CreatePortfolio(owner, savedAt, PortfolioStatus.Active);
        second.PortfolioId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var older = CreatePortfolio(owner, savedAt.AddDays(-1), PortfolioStatus.Active);
        var unrelated = CreatePortfolio(otherOwner, savedAt.AddDays(1), PortfolioStatus.Active);
        AddHolding(first);
        context.Portfolios.AddRange(second, older, first, unrelated);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        commands.Commands.Clear();

        var results = await new PortfolioQueries(context).GetByUserIdAsync(owner.UserId);

        Assert.Collection(results,
            summary => Assert.Equal(first.PortfolioId, summary.PortfolioId),
            summary => Assert.Equal(second.PortfolioId, summary.PortfolioId),
            summary => Assert.Equal(older.PortfolioId, summary.PortfolioId));
        Assert.Equal(PortfolioStatus.Archived, results[0].Status);
        Assert.Equal(PortfolioStatus.Active, results[1].Status);
        Assert.Equal(PortfolioType.Retirement, results[0].PortfolioType);
        Assert.Equal(CurrencyType.GBP, results[0].CurrencyType);
        Assert.Equal(first.CashBalance, results[0].CashBalance);
        Assert.Equal(savedAt, results[0].CreatedAt);
        Assert.Equal(savedAt.Offset, results[0].CreatedAt.Offset);
        Assert.Empty(context.ChangeTracker.Entries());

        var sql = Assert.Single(commands.Commands);
        Assert.Contains("[Portfolios]", sql);
        Assert.DoesNotContain("[Holdings]", sql);
        Assert.DoesNotContain("[Securities]", sql);
        Assert.DoesNotContain("[Users]", sql);
        Assert.DoesNotContain("[RowVersion]", sql);
        Assert.DoesNotContain("JOIN", sql, StringComparison.OrdinalIgnoreCase);
    }

    [SqlServerFact]
    public async Task ReturnsEmptyForAnOwnerWithoutPortfoliosAndAnUnknownUser()
    {
        await using var context = fixture.CreateContext();
        var owner = CreateOwner();
        context.Users.Add(owner);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var queries = new PortfolioQueries(context);
        Assert.Empty(await queries.GetByUserIdAsync(owner.UserId));
        Assert.Empty(await queries.GetByUserIdAsync(Guid.NewGuid()));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [SqlServerFact]
    public async Task ASelectedSummaryCanBeUsedToLoadTheCompletePortfolio()
    {
        await using var context = fixture.CreateContext();
        var owner = CreateOwner();
        var entity = CreatePortfolio(owner, DateTimeOffset.UtcNow, PortfolioStatus.Active);
        AddHolding(entity);
        context.Portfolios.Add(entity);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var summary = Assert.Single(await new PortfolioQueries(context).GetByUserIdAsync(owner.UserId));
        var detail = await new PortfolioRepository(context).GetByIdAsync(summary.PortfolioId);

        Assert.NotNull(detail);
        Assert.Equal(owner.UserId, detail.OwnerId);
        Assert.Equal(summary.PortfolioId, detail.PortfolioId);
        var holding = Assert.Single(detail.Holdings);
        Assert.Equal(10, holding.ShareCount);
        Assert.NotNull(holding.Security.LatestQuote);
    }

    private static User CreateOwner() => new()
    {
        UserId = Guid.NewGuid(), Name = "Portfolio list test",
        Email = $"{Guid.NewGuid():N}@example.com"
    };

    private static Portfolio CreatePortfolio(User owner, DateTimeOffset createdAt, PortfolioStatus status)
        => new()
        {
            PortfolioId = Guid.NewGuid(), OwnerId = owner.UserId, Owner = owner,
            CashBalance = 1200.12345678m, PortfolioType = (byte)PortfolioType.Retirement,
            CurrencyType = "GBP", Status = (byte)status, CreatedAt = createdAt
        };

    private static void AddHolding(Portfolio portfolio)
    {
        var security = new Security
        {
            SecurityId = Guid.NewGuid(), TickerSymbol = $"VOD{Guid.NewGuid():N}"[..12].ToUpperInvariant(),
            SecurityType = "Stock", CurrencyType = "GBP", CurrentValue = 42.125m,
            QuoteAt = portfolio.CreatedAt
        };
        portfolio.Holdings.Add(new Holding
        {
            HoldingId = Guid.NewGuid(), PortfolioId = portfolio.PortfolioId,
            SecurityId = security.SecurityId, Security = security,
            ShareCount = 10, AveragePurchasePrice = 25m
        });
    }

    private sealed class CommandRecorder : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return new ValueTask<InterceptionResult<DbDataReader>>(result);
        }
    }
}
