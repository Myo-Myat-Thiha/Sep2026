using Xunit;

namespace Sep2026.Domain.Tests;

public sealed class PortfolioSnapshotTests
{
    [Fact]
    public void DomainStateSupportsOtherCurrenciesAndHasNoImportPrecisionOrProviderRestriction()
    {
        var position = new PositionSnapshot("asset", " abc ", "Stock", CurrencyType.GBP,
            0.1234567891m, 1.123456789m);
        var snapshot = new PortfolioSnapshot("opaque-account", CurrencyType.GBP, 0.123456789m, new[] { position });
        Assert.Equal(CurrencyType.GBP, snapshot.Currency);
        Assert.Equal("ABC", Assert.Single(snapshot.Positions).Symbol);
        Assert.Equal(0.1234567891m, position.Quantity);
        Assert.Equal(1.123456789m, position.AverageCost);
    }

    [Fact]
    public void SnapshotCopiesItsCollectionAndPositionsCannotBeMutated()
    {
        var positions = new List<PositionSnapshot> { Position() };
        var snapshot = new PortfolioSnapshot("account", CurrencyType.USD, 0, positions);
        positions.Clear();
        Assert.Single(snapshot.Positions);
        Assert.Throws<NotSupportedException>(() => ((IList<PositionSnapshot>)snapshot.Positions).Clear());
    }

    [Fact]
    public void DuplicateExternalAssetsAreRejectedEvenWithoutApplicationValidation()
    {
        var first = Position();
        var second = new PositionSnapshot(first.ExternalAssetId, "MSFT", "Stock", CurrencyType.USD, 2, null);
        Assert.Throws<ArgumentException>(() => new PortfolioSnapshot("account", CurrencyType.USD, 0, new[] { first, second }));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" account ")]
    public void ExternalAccountIdentityCannotBeEmptyOrSilentlyTrimmed(string identity)
        => Assert.Throws<ArgumentException>(() => new PortfolioSnapshot(identity, CurrencyType.USD, 0, Array.Empty<PositionSnapshot>()));

    [Fact]
    public void SnapshotRequiresValidCurrencyNonnegativeCashAndNonNullPositions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PortfolioSnapshot("account", (CurrencyType)99, 0, Array.Empty<PositionSnapshot>()));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PortfolioSnapshot("account", CurrencyType.USD, -1, Array.Empty<PositionSnapshot>()));
        Assert.Throws<ArgumentNullException>(() => new PortfolioSnapshot("account", CurrencyType.USD, 0, null!));
        Assert.Throws<ArgumentException>(() => new PortfolioSnapshot("account", CurrencyType.USD, 0, new PositionSnapshot[] { null! }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void OpenPositionRequiresPositiveQuantity(int quantity)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new PositionSnapshot("asset", "AAPL", "Stock", CurrencyType.USD, quantity, null));

    [Fact]
    public void PositionRequiresIdentitySymbolTypeAndCurrency()
    {
        Assert.Throws<ArgumentException>(() => new PositionSnapshot(" ", "AAPL", "Stock", CurrencyType.USD, 1, null));
        Assert.Throws<ArgumentException>(() => new PositionSnapshot(" asset ", "AAPL", "Stock", CurrencyType.USD, 1, null));
        Assert.Throws<ArgumentException>(() => new PositionSnapshot("asset", " ", "Stock", CurrencyType.USD, 1, null));
        Assert.Throws<ArgumentException>(() => new PositionSnapshot("asset", "AAPL", " ", CurrencyType.USD, 1, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PositionSnapshot("asset", "AAPL", "Stock", (CurrencyType)99, 1, null));
    }

    [Fact]
    public void HoldingFinancialRulesApplyToSavedAndImportedPositions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new HoldingState(-1, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new HoldingState(1, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => HoldingModel.Restore(Guid.NewGuid(), new SecurityModel("AAPL"), Guid.NewGuid(), 1, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PositionSnapshot("asset", "AAPL", "Stock", CurrencyType.USD, 1, -1));
    }

    [Fact]
    public void FractionalRestorationPreservesUnknownCostAndPropagatesItIntoCalculations()
    {
        var quote = new MarketQuote("AAPL", 100m, "USD", DateTimeOffset.UtcNow);
        var security = SecurityModel.Restore(Guid.NewGuid(), "AAPL", "Stock", quote);
        var holding = HoldingModel.Restore(Guid.NewGuid(), security, Guid.NewGuid(), 1.25m, null);
        Assert.Equal(1.25m, holding.ShareCount);
        Assert.Equal(125m, holding.CurrentMarketValue);
        Assert.Null(holding.AveragePurchasePrice);
        Assert.Null(holding.TotalCostBasis);
        Assert.Null(holding.UnrealizedProfitLoss);
        Assert.Null(holding.UnrealizedProfitLossPercentage);
    }

    [Fact]
    public void ReportedZeroCostRemainsKnown()
    {
        var state = new HoldingState(1.25m, 0m);
        Assert.Equal(0m, state.AverageCost);
        Assert.Equal(0m, state.TotalCostBasis);
        Assert.Null(new HoldingState(1.25m, null).TotalCostBasis);
        Assert.Equal(0m, new HoldingState(0, null).TotalCostBasis);
    }

    [Fact]
    public void FinancialZeroIsValidRegardlessOfDecimalSignBit()
    {
        var negativeZero = new decimal(0, 0, 0, true, 1);
        Assert.Equal(0m, new HoldingState(negativeZero, negativeZero).Quantity);
        Assert.Equal(0m, new PortfolioSnapshot("account", CurrencyType.USD, negativeZero, Array.Empty<PositionSnapshot>()).CashBalance);
    }

    [Fact]
    public void FractionalBuysAndSellsPreserveWeightedCostAndUnknownCost()
    {
        var known = HoldingModel.Restore(Guid.NewGuid(), new SecurityModel("AAPL"), Guid.NewGuid(), 0.5m, 10m);
        known.BuyShares(0.5m, 20m);
        Assert.Equal(1m, known.ShareCount);
        Assert.Equal(15m, known.AveragePurchasePrice);
        known.SellShares(0.75m);
        Assert.Equal(0.25m, known.ShareCount);
        Assert.Equal(15m, known.AveragePurchasePrice);

        var unknown = HoldingModel.Restore(Guid.NewGuid(), new SecurityModel("AAPL"), Guid.NewGuid(), 0.5m, null);
        unknown.BuyShares(0.5m, 20m);
        Assert.Null(unknown.AveragePurchasePrice);
        unknown.SellShares(1m);
        unknown.BuyShares(0.5m, 30m);
        Assert.Equal(30m, unknown.AveragePurchasePrice);
    }

    private static PositionSnapshot Position() => new("asset", "AAPL", "Stock", CurrencyType.USD, 1.25m, null);
}
