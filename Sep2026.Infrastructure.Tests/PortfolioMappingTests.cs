using Microsoft.EntityFrameworkCore;
using Sep2026.Domain;
using Sep2026.Infrastructure.Persistence;
using Sep2026.Infrastructure.Persistence.Entities;
using Sep2026.Infrastructure.Persistence.Mappings;
using Sep2026.Infrastructure.Persistence.Repositories;
using Xunit;

namespace Sep2026.Infrastructure.Tests;

public class PortfolioMappingTests
{
    private static readonly DateTimeOffset SavedAt =
        new(2024, 2, 3, 10, 30, 0, TimeSpan.FromHours(1));

    [Fact]
    public void MapsACompleteArchivedPortfolioWithoutLoadingTheOwnerGraph()
    {
        var entity = CreatePortfolio();
        var holding = Assert.Single(entity.Holdings);

        var result = PortfolioMapper.ToDomain(entity);

        Assert.Equal(entity.PortfolioId, result.PortfolioId);
        Assert.Equal(entity.OwnerId, result.OwnerId);
        Assert.Equal(entity.CashBalance, result.CashBalance);
        Assert.Equal(PortfolioType.Retirement, result.PortfolioType);
        Assert.Equal(PortfolioStatus.Archived, result.Status);
        Assert.Equal(CurrencyType.GBP, result.CurrencyType);
        Assert.Equal(SavedAt, result.CreatedAt);
        Assert.Equal(SavedAt.Offset, result.CreatedAt.Offset);
        var resultHolding = Assert.Single(result.Holdings);
        Assert.Equal(holding.HoldingId, resultHolding.HoldingId);
        Assert.Equal(holding.PortfolioId, resultHolding.PortfolioId);
        Assert.Equal(holding.ShareCount, resultHolding.ShareCount);
        Assert.Equal(holding.AveragePurchasePrice, resultHolding.AveragePurchasePrice);
        Assert.Equal(holding.SecurityId, resultHolding.SecurityId);
        Assert.Equal("VOD", resultHolding.TickerSymbol);
        Assert.Equal("Stock", resultHolding.Security.SecurityType);
        var quote = Assert.IsType<MarketQuote>(resultHolding.Security.LatestQuote);
        Assert.Equal(42.125m, quote.Price);
        Assert.Equal("GBP", quote.CurrencyType);
        Assert.Equal(SavedAt.AddDays(1), quote.QuoteAt);
        Assert.Null(entity.Owner);
        Assert.Null(holding.Portfolio);
    }

    [Fact]
    public void MapsAnEmptyPortfolio()
    {
        var entity = CreatePortfolio();
        entity.Holdings.Clear();
        Assert.Empty(PortfolioMapper.ToDomain(entity).Holdings);
    }

    [Fact]
    public void MapsAnEmptyHoldingWithAnUnavailableQuote()
    {
        var entity = CreatePortfolio();
        var holding = Assert.Single(entity.Holdings);
        holding.ShareCount = 0;
        holding.AveragePurchasePrice = 0m;
        holding.Security.CurrentValue = null;
        holding.Security.QuoteAt = null;

        var result = Assert.Single(PortfolioMapper.ToDomain(entity).Holdings);

        Assert.True(result.IsEmpty);
        Assert.Equal(0m, result.AveragePurchasePrice);
        Assert.Null(result.Security.LatestQuote);
        Assert.Null(result.Security.CurrentValue);
    }

    [Fact]
    public void MapsAKnownZeroPriceWithoutTreatingItAsMissing()
    {
        var entity = CreatePortfolio();
        Assert.Single(entity.Holdings).Security.CurrentValue = 0m;
        var holding = Assert.Single(PortfolioMapper.ToDomain(entity).Holdings);
        Assert.NotNull(holding.Security.LatestQuote);
        Assert.Equal(0m, holding.CurrentMarketValue);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("XYZ")]
    [InlineData("0")]
    public void RejectsUnsupportedPortfolioCurrencies(string? currency)
    {
        var entity = CreatePortfolio();
        entity.CurrencyType = currency!;
        var exception = Assert.Throws<InvalidDataException>(() => PortfolioMapper.ToDomain(entity));
        Assert.Contains(entity.PortfolioId.ToString(), exception.Message);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RejectsUnsupportedPortfolioEnums(bool invalidStatus)
    {
        var entity = CreatePortfolio();
        if (invalidStatus) entity.Status = 99;
        else entity.PortfolioType = 99;
        var exception = Assert.Throws<InvalidDataException>(() => PortfolioMapper.ToDomain(entity));
        Assert.Contains(entity.PortfolioId.ToString(), exception.Message);
        Assert.IsType<ArgumentOutOfRangeException>(exception.InnerException);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RejectsIncompleteQuotePairs(bool missingPrice)
    {
        var entity = CreatePortfolio();
        var security = Assert.Single(entity.Holdings).Security;
        if (missingPrice) security.CurrentValue = null;
        else security.QuoteAt = null;

        var exception = Assert.Throws<InvalidDataException>(() => PortfolioMapper.ToDomain(entity));
        Assert.Contains(security.SecurityId.ToString(), exception.Message);
    }

    [Fact]
    public void RejectsAnUnloadedSecurityRelationship()
    {
        var entity = CreatePortfolio();
        var holding = Assert.Single(entity.Holdings);
        holding.Security = null!;
        var exception = Assert.Throws<InvalidDataException>(() => PortfolioMapper.ToDomain(entity));
        Assert.Contains(holding.HoldingId.ToString(), exception.Message);
        Assert.Contains("loaded", exception.Message);
    }

    [Fact]
    public void RejectsAnInconsistentSecurityRelationship()
    {
        var entity = CreatePortfolio();
        var holding = Assert.Single(entity.Holdings);
        holding.SecurityId = Guid.NewGuid();
        Assert.Throws<InvalidDataException>(() => PortfolioMapper.ToDomain(entity));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RejectsInvalidSavedAmounts(bool invalidHolding)
    {
        var entity = CreatePortfolio();
        if (invalidHolding) Assert.Single(entity.Holdings).ShareCount = -1;
        else entity.CashBalance = -1m;
        Assert.Throws<InvalidDataException>(() => PortfolioMapper.ToDomain(entity));
    }

    [Fact]
    public void RejectsInvalidSecurityDescriptionsWithRecordContext()
    {
        var entity = CreatePortfolio();
        var security = Assert.Single(entity.Holdings).Security;
        security.SecurityType = " ";
        var exception = Assert.Throws<InvalidDataException>(() => PortfolioMapper.ToDomain(entity));
        Assert.Contains(security.SecurityId.ToString(), exception.Message);
        Assert.IsAssignableFrom<ArgumentException>(exception.InnerException);
    }

    [Fact]
    public void RejectsHoldingsForAnotherPortfolio()
    {
        var entity = CreatePortfolio();
        Assert.Single(entity.Holdings).PortfolioId = Guid.NewGuid();
        Assert.Throws<InvalidDataException>(() => PortfolioMapper.ToDomain(entity));
    }

    [Fact]
    public void RejectsDuplicateSecuritiesInAPortfolio()
    {
        var entity = CreatePortfolio();
        var first = Assert.Single(entity.Holdings);
        entity.Holdings.Add(new Holding
        {
            HoldingId = Guid.NewGuid(), PortfolioId = entity.PortfolioId,
            SecurityId = first.SecurityId, Security = first.Security,
            ShareCount = 1, AveragePurchasePrice = 20m
        });
        Assert.Throws<InvalidDataException>(() => PortfolioMapper.ToDomain(entity));
    }

    [Fact]
    public void RejectsMissingHoldingCollectionsAndNullEntries()
    {
        var entity = CreatePortfolio();
        entity.Holdings = null!;
        Assert.Throws<InvalidDataException>(() => PortfolioMapper.ToDomain(entity));
        entity.Holdings = [null!];
        Assert.Throws<InvalidDataException>(() => PortfolioMapper.ToDomain(entity));
    }

    [Fact]
    public async Task RejectsEmptyIdsBeforeOpeningADatabaseConnection()
    {
        await using var context = CreateDisconnectedContext();
        var repository = new PortfolioRepository(context);
        await Assert.ThrowsAsync<ArgumentException>(() => repository.GetByIdAsync(Guid.Empty));
    }

    [Fact]
    public async Task HonorsCancellationBeforeOpeningADatabaseConnection()
    {
        await using var context = CreateDisconnectedContext();
        var repository = new PortfolioRepository(context);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.GetByIdAsync(
            Guid.NewGuid(), new CancellationToken(canceled: true)));
    }

    private static GeminoDbContext CreateDisconnectedContext()
        => new(new DbContextOptionsBuilder<GeminoDbContext>()
            .UseSqlServer("Server=unreachable-test-server;Database=Gemino;Integrated Security=True;Connect Timeout=1;")
            .Options);

    private static Portfolio CreatePortfolio()
    {
        var entity = new Portfolio
        {
            PortfolioId = Guid.NewGuid(), OwnerId = Guid.NewGuid(),
            CashBalance = 1200.12345678m, PortfolioType = (byte)PortfolioType.Retirement,
            CurrencyType = "GBP", Status = (byte)PortfolioStatus.Archived,
            CreatedAt = SavedAt, RowVersion = [1, 2, 3, 4, 5, 6, 7, 8]
        };
        var security = new Security
        {
            SecurityId = Guid.NewGuid(), TickerSymbol = "VOD", SecurityType = "Stock",
            CurrentValue = 42.125m, CurrencyType = "GBP", QuoteAt = SavedAt.AddDays(1)
        };
        entity.Holdings.Add(new Holding
        {
            HoldingId = Guid.NewGuid(), PortfolioId = entity.PortfolioId,
            SecurityId = security.SecurityId, Security = security,
            ShareCount = 10, AveragePurchasePrice = 20.25m,
            RowVersion = [8, 7, 6, 5, 4, 3, 2, 1]
        });
        return entity;
    }
}
