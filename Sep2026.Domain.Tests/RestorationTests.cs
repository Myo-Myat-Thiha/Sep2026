using Sep2026.Domain;
using Xunit;

namespace Sep2026.Domain.Tests;

public class RestorationTests
{
    private static readonly DateTimeOffset SavedAt =
        new(2024, 2, 3, 10, 30, 0, TimeSpan.FromHours(1));

    [Fact]
    public void RestoringAnArchivedGraphPreservesSavedIdentityAndStateWithoutBusinessMessages()
    {
        var userId = Guid.NewGuid();
        var portfolioId = Guid.NewGuid();
        var holdingId = Guid.NewGuid();
        var securityId = Guid.NewGuid();
        var quote = new MarketQuote("VOD", 42.125m, "GBP", SavedAt.AddDays(1));
        var originalOutput = Console.Out;
        using var messages = new StringWriter();

        UserModel user;
        try
        {
            Console.SetOut(messages);
            var security = SecurityModel.Restore(securityId, "VOD", "Stock", quote);
            var holding = HoldingModel.Restore(holdingId, security, portfolioId, 10, 20.25m);
            var portfolio = RestorePortfolio(portfolioId, userId, [holding]);
            user = UserModel.Restore(userId, "Alice", "alice@example.com", [portfolio]);
        }
        finally
        {
            Console.SetOut(originalOutput);
        }

        Assert.Equal(string.Empty, messages.ToString());
        Assert.Equal(userId, user.UserId);
        Assert.Equal("Alice", user.Name);
        Assert.Equal("alice@example.com", user.Email);
        var restoredPortfolio = Assert.Single(user.Portfolios);
        Assert.Equal(portfolioId, restoredPortfolio.PortfolioId);
        Assert.Equal(userId, restoredPortfolio.OwnerId);
        Assert.Equal(1200.345678m, restoredPortfolio.CashBalance);
        Assert.Equal(PortfolioType.Retirement, restoredPortfolio.PortfolioType);
        Assert.Equal(CurrencyType.GBP, restoredPortfolio.CurrencyType);
        Assert.Equal(PortfolioStatus.Archived, restoredPortfolio.Status);
        Assert.Equal(SavedAt, restoredPortfolio.CreatedAt);
        Assert.Equal(SavedAt.Offset, restoredPortfolio.CreatedAt.Offset);

        var restoredHolding = Assert.Single(restoredPortfolio.Holdings);
        Assert.Equal(holdingId, restoredHolding.HoldingId);
        Assert.Equal(portfolioId, restoredHolding.PortfolioId);
        Assert.Equal(10, restoredHolding.ShareCount);
        Assert.Equal(20.25m, restoredHolding.AveragePurchasePrice);
        Assert.Equal(securityId, restoredHolding.SecurityId);
        Assert.Equal("Stock", restoredHolding.Security.SecurityType);
        Assert.Same(quote, restoredHolding.Security.LatestQuote);
        Assert.Equal(421.25m, restoredHolding.CurrentMarketValue);
        Assert.Throws<InvalidOperationException>(() => restoredPortfolio.DepositCash(1m));
    }

    [Fact]
    public void CreatingNewObjectsStillGeneratesIdsAndInitialDefaults()
    {
        var user = new UserModel("Alice", "alice@example.com");
        var portfolio = new PortfolioModel(user.UserId);
        var anotherPortfolio = new PortfolioModel(user.UserId);
        var security = new SecurityModel(" aapl ");
        var holding = new HoldingModel(security, portfolio.PortfolioId);

        Assert.NotEqual(Guid.Empty, user.UserId);
        Assert.NotEqual(Guid.Empty, portfolio.PortfolioId);
        Assert.NotEqual(portfolio.PortfolioId, anotherPortfolio.PortfolioId);
        Assert.NotEqual(Guid.Empty, security.SecurityId);
        Assert.NotEqual(Guid.Empty, holding.HoldingId);
        Assert.Equal(0m, portfolio.CashBalance);
        Assert.Equal(PortfolioStatus.Active, portfolio.Status);
        Assert.Equal(PortfolioType.Individual, portfolio.PortfolioType);
        Assert.Equal(CurrencyType.USD, portfolio.CurrencyType);
        Assert.Empty(portfolio.Holdings);
        Assert.Equal("AAPL", security.TickerSymbol);
        Assert.Equal("Stock", security.SecurityType);
        Assert.Null(security.LatestQuote);
        Assert.Equal(0, holding.ShareCount);
        Assert.Equal(0m, holding.AveragePurchasePrice);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 25)]
    [InlineData(10, 25)]
    public void ValidHoldingStatesCanBeRestored(int shares, int averagePrice)
    {
        var holding = HoldingModel.Restore(Guid.NewGuid(), new SecurityModel("AAPL"),
            Guid.NewGuid(), shares, averagePrice);

        Assert.Equal(shares, holding.ShareCount);
        Assert.Equal((decimal)averagePrice, holding.AveragePurchasePrice);
    }

    [Theory]
    [InlineData(-1, 25)]
    [InlineData(0, -1)]
    [InlineData(1, 0)]
    public void InvalidHoldingAmountsAreRejected(int shares, int averagePrice)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => HoldingModel.Restore(
            Guid.NewGuid(), new SecurityModel("AAPL"), Guid.NewGuid(), shares, averagePrice));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EmptyHoldingOrPortfolioIdsAreRejected(bool emptyHoldingId)
    {
        Assert.Throws<ArgumentException>(() => HoldingModel.Restore(
            emptyHoldingId ? Guid.Empty : Guid.NewGuid(), new SecurityModel("AAPL"),
            emptyHoldingId ? Guid.NewGuid() : Guid.Empty, 0, 0m));
    }

    [Fact]
    public void NewHoldingsAlsoRejectAnEmptyPortfolioId()
    {
        Assert.Throws<ArgumentException>(() => new HoldingModel(new SecurityModel("AAPL"), Guid.Empty));
        Assert.Throws<ArgumentException>(() => new HoldingModel(new SecurityModel("AAPL"), Guid.Empty, 1, 1m));
    }

    [Fact]
    public void HoldingRestorationRequiresASecurity()
    {
        Assert.Throws<ArgumentNullException>(() => HoldingModel.Restore(
            Guid.NewGuid(), null!, Guid.NewGuid(), 0, 0m));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EmptyPortfolioOrOwnerIdsAreRejected(bool emptyPortfolioId)
    {
        Assert.Throws<ArgumentException>(() => RestorePortfolio(
            emptyPortfolioId ? Guid.Empty : Guid.NewGuid(),
            emptyPortfolioId ? Guid.NewGuid() : Guid.Empty));
    }

    [Fact]
    public void NegativeCashIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RestorePortfolio(
            Guid.NewGuid(), Guid.NewGuid(), cashBalance: -1m));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void UnsupportedPortfolioEnumsAreRejected(int invalidField)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RestorePortfolio(
            Guid.NewGuid(), Guid.NewGuid(),
            portfolioType: invalidField == 0 ? (PortfolioType)99 : PortfolioType.Retirement,
            currencyType: invalidField == 1 ? (CurrencyType)99 : CurrencyType.GBP,
            status: invalidField == 2 ? (PortfolioStatus)99 : PortfolioStatus.Archived));
    }

    [Fact]
    public void MissingCreationTimeIsRejected()
    {
        Assert.Throws<ArgumentException>(() => RestorePortfolio(
            Guid.NewGuid(), Guid.NewGuid(), createdAt: default(DateTimeOffset)));
    }

    [Fact]
    public void NullHoldingCollectionOrEntriesAreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => PortfolioModel.Restore(
            Guid.NewGuid(), 0m, null!, Guid.NewGuid(), PortfolioType.Individual,
            CurrencyType.USD, SavedAt, PortfolioStatus.Active));
        Assert.Throws<ArgumentException>(() => RestorePortfolio(
            Guid.NewGuid(), Guid.NewGuid(), new HoldingModel[] { null! }));
    }

    [Fact]
    public void HoldingsCannotBeRestoredIntoAnotherPortfolio()
    {
        var holding = HoldingModel.Restore(Guid.NewGuid(), new SecurityModel("AAPL"),
            Guid.NewGuid(), 1, 10m);
        Assert.Throws<ArgumentException>(() => RestorePortfolio(Guid.NewGuid(), Guid.NewGuid(), [holding]));
    }

    [Fact]
    public void DuplicateHoldingIdsAreRejectedEvenForDifferentSecurities()
    {
        var portfolioId = Guid.NewGuid();
        var holdingId = Guid.NewGuid();
        var first = HoldingModel.Restore(holdingId, new SecurityModel("AAPL"), portfolioId, 1, 10m);
        var second = HoldingModel.Restore(holdingId, new SecurityModel("MSFT"), portfolioId, 1, 20m);
        Assert.Throws<ArgumentException>(() => RestorePortfolio(portfolioId, Guid.NewGuid(), [first, second]));
    }

    [Fact]
    public void DuplicateSecuritiesAreRejectedEvenForDifferentHoldingIds()
    {
        var portfolioId = Guid.NewGuid();
        var security = new SecurityModel("AAPL");
        var first = HoldingModel.Restore(Guid.NewGuid(), security, portfolioId, 1, 10m);
        var second = HoldingModel.Restore(Guid.NewGuid(), security, portfolioId, 1, 20m);
        Assert.Throws<ArgumentException>(() => RestorePortfolio(portfolioId, Guid.NewGuid(), [first, second]));
    }

    [Fact]
    public void RestorationCopiesBothCollectionBoundaries()
    {
        var userId = Guid.NewGuid();
        var portfolioId = Guid.NewGuid();
        var holding = HoldingModel.Restore(Guid.NewGuid(), new SecurityModel("AAPL"), portfolioId, 0, 0m);
        var holdings = new List<HoldingModel> { holding };
        var portfolio = RestorePortfolio(portfolioId, userId, holdings);
        var portfolios = new List<PortfolioModel> { portfolio };
        var user = UserModel.Restore(userId, "Alice", "alice@example.com", portfolios);

        holdings.Clear();
        portfolios.Clear();

        Assert.Same(holding, Assert.Single(portfolio.Holdings));
        Assert.Same(portfolio, Assert.Single(user.Portfolios));
    }

    [Fact]
    public void EmptyUserIdentityIsRejected()
    {
        Assert.Throws<ArgumentException>(() => UserModel.Restore(
            Guid.Empty, "Alice", "alice@example.com", []));
    }

    [Theory]
    [InlineData("", "alice@example.com")]
    [InlineData("Alice", " ")]
    public void BlankUserFieldsAreRejected(string name, string email)
    {
        Assert.Throws<ArgumentException>(() => UserModel.Restore(Guid.NewGuid(), name, email, []));
    }

    [Fact]
    public void RestorationRetainsUserEmailValidation()
    {
        Assert.Throws<FormatException>(() => UserModel.Restore(Guid.NewGuid(), "Alice", "invalid", []));
    }

    [Fact]
    public void UserRestorationRejectsMissingPortfolioCollectionsAndEntries()
    {
        Assert.Throws<ArgumentNullException>(() => UserModel.Restore(Guid.NewGuid(), "Alice", "alice@example.com", null!));
        Assert.Throws<ArgumentException>(() => UserModel.Restore(Guid.NewGuid(), "Alice", "alice@example.com", new PortfolioModel[] { null! }));
    }

    [Fact]
    public void AUserCannotRestoreSomeoneElsesPortfolio()
    {
        var portfolio = RestorePortfolio(Guid.NewGuid(), Guid.NewGuid());
        Assert.Throws<ArgumentException>(() => UserModel.Restore(Guid.NewGuid(), "Alice", "alice@example.com", [portfolio]));
    }

    [Fact]
    public void DuplicatePortfolioIdsAreRejectedForAUser()
    {
        var userId = Guid.NewGuid();
        var portfolioId = Guid.NewGuid();
        var first = RestorePortfolio(portfolioId, userId);
        var second = RestorePortfolio(portfolioId, userId);
        Assert.Throws<ArgumentException>(() => UserModel.Restore(userId, "Alice", "alice@example.com", [first, second]));
    }

    [Fact]
    public void AQuoteLessSecurityKeepsValuationsUnknown()
    {
        var security = SecurityModel.Restore(Guid.NewGuid(), "AAPL", "Stock");
        var holding = HoldingModel.Restore(Guid.NewGuid(), security, Guid.NewGuid(), 10, 10m);
        Assert.Null(security.LatestQuote);
        Assert.Null(security.CurrentValue);
        Assert.Null(holding.CurrentMarketValue);
        Assert.Null(holding.UnrealizedProfitLoss);
    }

    [Fact]
    public void AZeroPriceIsKnownRatherThanMissing()
    {
        var quote = new MarketQuote("AAPL", 0m, "USD", SavedAt);
        var security = SecurityModel.Restore(Guid.NewGuid(), "AAPL", "Stock", quote);
        var holding = HoldingModel.Restore(Guid.NewGuid(), security, Guid.NewGuid(), 10, 10m);
        Assert.Equal(0m, security.CurrentValue);
        Assert.Equal(0m, holding.CurrentMarketValue);
        Assert.Equal(-100m, holding.UnrealizedProfitLoss);
    }

    [Fact]
    public void SecurityRestorationRejectsAnEmptyId()
    {
        Assert.Throws<ArgumentException>(() => SecurityModel.Restore(Guid.Empty, "AAPL", "Stock"));
    }

    [Theory]
    [InlineData("", "Stock")]
    [InlineData("AAPL", " ")]
    public void SecurityRestorationRejectsBlankIdentityDescriptions(string symbol, string type)
    {
        Assert.Throws<ArgumentException>(() => SecurityModel.Restore(Guid.NewGuid(), symbol, type));
    }

    [Fact]
    public void SecurityRestorationRejectsAnotherSymbolsQuote()
    {
        var quote = new MarketQuote("MSFT", 100m, "USD", SavedAt);
        Assert.Throws<ArgumentException>(() => SecurityModel.Restore(Guid.NewGuid(), "AAPL", "Stock", quote));
    }

    [Fact]
    public void UpdatingAQuoteKeepsItsMetadataAndRejectsAnotherSymbolWithoutChangingState()
    {
        var security = new SecurityModel("AAPL");
        var quote = new MarketQuote(" aapl ", 100m, " gbp ", SavedAt);
        security.UpdateMarketQuote(quote);
        Assert.Same(quote, security.LatestQuote);
        var savedQuote = Assert.IsType<MarketQuote>(security.LatestQuote);
        Assert.Equal("GBP", savedQuote.CurrencyType);
        Assert.Equal(SavedAt, savedQuote.QuoteAt);
        Assert.Throws<ArgumentException>(() => security.UpdateMarketQuote(new MarketQuote("MSFT", 200m, "USD", SavedAt)));
        Assert.Throws<ArgumentNullException>(() => security.UpdateMarketQuote(null!));
        Assert.Same(quote, security.LatestQuote);

        security.UpdateTickerSymbol(" aapl ");
        Assert.Same(quote, security.LatestQuote);
        security.UpdateTickerSymbol("MSFT");
        Assert.Null(security.LatestQuote);
        Assert.Null(security.CurrentValue);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("XYZ")]
    [InlineData("0")]
    public void QuotesRejectUnsupportedCurrencies(string currency)
    {
        Assert.Throws<ArgumentException>(() => new MarketQuote("AAPL", 100m, currency, SavedAt));
    }

    [Fact]
    public void QuotesRejectNegativePricesAndMissingTimestampsOrSymbols()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MarketQuote("AAPL", -1m, "USD", SavedAt));
        Assert.Throws<ArgumentException>(() => new MarketQuote("AAPL", 1m, "USD", default));
        Assert.Throws<ArgumentException>(() => new MarketQuote(" ", 1m, "USD", SavedAt));
    }

    [Fact]
    public void BuyingMoreSharesUpdatesQuantityWeightedCostAndCashOnce()
    {
        var portfolioId = Guid.NewGuid();
        var security = new SecurityModel("AAPL");
        var holding = HoldingModel.Restore(Guid.NewGuid(), security, portfolioId, 10, 20m);
        var portfolio = RestorePortfolio(portfolioId, Guid.NewGuid(), [holding],
            cashBalance: 1000m, status: PortfolioStatus.Active);

        portfolio.ApplyBuy(security, 5, 40m);

        Assert.Single(portfolio.Holdings);
        Assert.Equal(15, holding.ShareCount);
        Assert.Equal(400m / 15m, holding.AveragePurchasePrice);
        Assert.Equal(800m, portfolio.CashBalance);
    }

    [Fact]
    public void SellingAllSharesProducesAnEmptyHoldingThatCanBeRestored()
    {
        var holding = HoldingModel.Restore(Guid.NewGuid(), new SecurityModel("AAPL"), Guid.NewGuid(), 10, 20m);
        holding.SellShares(10);
        var restored = HoldingModel.Restore(holding.HoldingId, holding.Security,
            holding.PortfolioId, holding.ShareCount, holding.AveragePurchasePrice);
        Assert.Equal(0, restored.ShareCount);
        Assert.Equal(20m, restored.AveragePurchasePrice);
        Assert.Equal(holding.HoldingId, restored.HoldingId);
    }

    [Fact]
    public void ShareCountOverflowLeavesTheHoldingUnchanged()
    {
        var holding = HoldingModel.Restore(Guid.NewGuid(), new SecurityModel("AAPL"), Guid.NewGuid(), int.MaxValue, 1m);
        Assert.Throws<OverflowException>(() => holding.BuyShares(1, 1m));
        Assert.Equal(int.MaxValue, holding.ShareCount);
        Assert.Equal(1m, holding.AveragePurchasePrice);
    }

    private static PortfolioModel RestorePortfolio(
        Guid portfolioId,
        Guid ownerId,
        IEnumerable<HoldingModel>? holdings = null,
        decimal cashBalance = 1200.345678m,
        PortfolioType portfolioType = PortfolioType.Retirement,
        CurrencyType currencyType = CurrencyType.GBP,
        DateTimeOffset? createdAt = null,
        PortfolioStatus status = PortfolioStatus.Archived)
        => PortfolioModel.Restore(portfolioId, cashBalance, holdings ?? [], ownerId,
            portfolioType, currencyType, createdAt ?? SavedAt, status);
}
