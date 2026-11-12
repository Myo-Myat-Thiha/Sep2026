using Microsoft.EntityFrameworkCore;
using Sep2026.Domain;
using Sep2026.Infrastructure.Persistence;
using Sep2026.Infrastructure.Persistence.Entities;
using Sep2026.Infrastructure.Persistence.Repositories;
using Xunit;

namespace Sep2026.Infrastructure.Tests;

public class PortfolioRepositorySqlServerTests : IClassFixture<SqlServerFixture>
{
    private readonly SqlServerFixture fixture;

    public PortfolioRepositorySqlServerTests(SqlServerFixture fixture) => this.fixture = fixture;

    [SqlServerFact]
    public async Task LoadsAllHoldingsAndSecuritiesFromSqlServerWithoutTracking()
    {
        await using var context = fixture.CreateContext();
        var entity = await SeedPortfolioAsync(context, withHoldings: true);
        var quotedHolding = entity.Holdings.Single(holding => holding.ShareCount == 10);
        var emptyHolding = entity.Holdings.Single(holding => holding.ShareCount == 0);
        context.ChangeTracker.Clear();

        var result = await new PortfolioRepository(context).GetByIdAsync(entity.PortfolioId);

        Assert.NotNull(result);
        Assert.Equal(entity.PortfolioId, result.PortfolioId);
        Assert.Equal(entity.OwnerId, result.OwnerId);
        Assert.Equal(entity.CashBalance, result.CashBalance);
        Assert.Equal(entity.CreatedAt, result.CreatedAt);
        Assert.Equal(PortfolioStatus.Archived, result.Status);
        Assert.Equal(PortfolioType.Retirement, result.PortfolioType);
        Assert.Equal(CurrencyType.GBP, result.CurrencyType);
        Assert.Equal(2, result.Holdings.Count);
        var quoted = Assert.Single(result.Holdings, holding => holding.HoldingId == quotedHolding.HoldingId);
        Assert.Equal(quotedHolding.SecurityId, quoted.SecurityId);
        Assert.Equal(quotedHolding.Security.TickerSymbol, quoted.TickerSymbol);
        Assert.Equal(10, quoted.ShareCount);
        Assert.Equal(25.12345678m, quoted.AveragePurchasePrice);
        var quote = Assert.IsType<MarketQuote>(quoted.Security.LatestQuote);
        Assert.Equal(42.125m, quote.Price);
        Assert.Equal("GBP", quote.CurrencyType);
        Assert.Equal(quotedHolding.Security.QuoteAt, quote.QuoteAt);
        var empty = Assert.Single(result.Holdings, holding => holding.HoldingId == emptyHolding.HoldingId);
        Assert.Equal(0, empty.ShareCount);
        Assert.Equal(0m, empty.AveragePurchasePrice);
        Assert.Null(empty.Security.CurrentValue);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [SqlServerFact]
    public async Task ReturnsNullForAMissingPortfolio()
    {
        await using var context = fixture.CreateContext();
        Assert.Null(await new PortfolioRepository(context).GetByIdAsync(Guid.NewGuid()));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [SqlServerFact]
    public async Task LoadsAPortfolioWithNoHoldings()
    {
        await using var context = fixture.CreateContext();
        var entity = await SeedPortfolioAsync(context, withHoldings: false);
        context.ChangeTracker.Clear();
        var result = await new PortfolioRepository(context).GetByIdAsync(entity.PortfolioId);
        Assert.NotNull(result);
        Assert.Empty(result.Holdings);
    }

    [SqlServerFact]
    public async Task ChangingTheReadOnlyDomainSnapshotDoesNotSaveDatabaseChanges()
    {
        await using var context = fixture.CreateContext();
        var entity = await SeedPortfolioAsync(context, withHoldings: false, active: true);
        var savedBalance = entity.CashBalance;
        context.ChangeTracker.Clear();

        var result = await new PortfolioRepository(context).GetByIdAsync(entity.PortfolioId);
        Assert.NotNull(result);
        result.DepositCash(100m);
        Assert.Equal(savedBalance + 100m, result.CashBalance);
        Assert.Equal(0, await context.SaveChangesAsync());
        var databaseBalance = await context.Portfolios
            .Where(portfolio => portfolio.PortfolioId == entity.PortfolioId)
            .Select(portfolio => portfolio.CashBalance).SingleAsync();
        Assert.Equal(savedBalance, databaseBalance);
    }

    private static async Task<Portfolio> SeedPortfolioAsync(
        GeminoDbContext context, bool withHoldings, bool active = false)
    {
        var owner = new User
        {
            UserId = Guid.NewGuid(), Name = "Repository test",
            Email = $"{Guid.NewGuid():N}@example.com"
        };
        var entity = new Portfolio
        {
            PortfolioId = Guid.NewGuid(), OwnerId = owner.UserId, Owner = owner,
            CashBalance = 1200.12345678m, PortfolioType = (byte)PortfolioType.Retirement,
            CurrencyType = "GBP", Status = (byte)(active ? PortfolioStatus.Active : PortfolioStatus.Archived),
            CreatedAt = new DateTimeOffset(2024, 2, 3, 10, 30, 0, TimeSpan.FromHours(1))
        };
        if (withHoldings)
        {
            var security = new Security
            {
                SecurityId = Guid.NewGuid(), TickerSymbol = $"VOD{Guid.NewGuid():N}"[..12].ToUpperInvariant(),
                SecurityType = "Stock", CurrentValue = 42.125m, CurrencyType = "GBP",
                QuoteAt = entity.CreatedAt.AddDays(1)
            };
            var unquoted = new Security
            {
                SecurityId = Guid.NewGuid(), TickerSymbol = $"NEW{Guid.NewGuid():N}"[..12].ToUpperInvariant(),
                SecurityType = "Stock", CurrencyType = "GBP"
            };
            entity.Holdings.Add(new Holding
            {
                HoldingId = Guid.NewGuid(), PortfolioId = entity.PortfolioId,
                SecurityId = security.SecurityId, Security = security,
                ShareCount = 10, AveragePurchasePrice = 25.12345678m
            });
            entity.Holdings.Add(new Holding
            {
                HoldingId = Guid.NewGuid(), PortfolioId = entity.PortfolioId,
                SecurityId = unquoted.SecurityId, Security = unquoted,
                ShareCount = 0, AveragePurchasePrice = 0m
            });
        }
        context.Portfolios.Add(entity);
        await context.SaveChangesAsync();
        return entity;
    }
}
