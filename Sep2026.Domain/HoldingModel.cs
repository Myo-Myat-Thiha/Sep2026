namespace Sep2026.Domain
{
    public class HoldingModel
    {
        //Feilds
        private readonly Guid holdingId;

        private readonly SecurityModel security;
        private HoldingState state;

        public Guid HoldingId => holdingId;
        public SecurityModel Security => security;
        public string TickerSymbol => security.TickerSymbol;
        public Guid SecurityId => security.SecurityId;
        public Guid PortfolioId { get; }
        public bool IsEmpty => ShareCount == 0;
        public decimal? TotalCostBasis => state.TotalCostBasis;
        // Nullable arithmetic keeps valuations unknown until a market quote is available.
        public decimal? CurrentMarketValue => ShareCount * security.CurrentValue;
        public decimal? UnrealizedProfitLoss => CurrentMarketValue - TotalCostBasis;
        public decimal? UnrealizedProfitLossPercentage => TotalCostBasis == 0 ? 0 : (UnrealizedProfitLoss / TotalCostBasis) * 100;
        

        public decimal ShareCount => state.Quantity;
        public decimal? AveragePurchasePrice => state.AverageCost;
        public HoldingModel(SecurityModel security, Guid portfolioId)
            : this(Guid.NewGuid(), security, portfolioId, 0, 0m)
        {
        }
        public HoldingModel(SecurityModel security, Guid portfolioId, decimal share, decimal? averagePurchasePrice)
            : this(Guid.NewGuid(), security, portfolioId, share, averagePurchasePrice)
        {
            // New funded holdings require positive quantities; restoration also accepts empty holdings.
            ValidatePositiveNumber(share);
        }

        //Restoring the Existing Holding from the Database.
        private HoldingModel(Guid holdingId, SecurityModel security,Guid portfolioId, decimal shareCount, decimal? averagePurchasePrice)
        {
            if (holdingId == Guid.Empty)
                throw new ArgumentException("A holding ID is required.", nameof(holdingId));
            if (portfolioId == Guid.Empty)
                throw new ArgumentException("A portfolio ID is required.", nameof(portfolioId));
            ArgumentNullException.ThrowIfNull(security);
            state = new HoldingState(shareCount, averagePurchasePrice);
            this.holdingId = holdingId;
            this.security = security;
            this.PortfolioId = portfolioId;
        }

        public static HoldingModel Restore(
            Guid holdingId,
            SecurityModel security,
            Guid portfolioId,
            decimal shareCount,
            decimal? averagePurchasePrice)
            => new HoldingModel(holdingId, security, portfolioId, shareCount, averagePurchasePrice);

        internal void BuyShares(decimal amount, decimal purchasePrice)
        {
            ValidatePositiveNumber(amount);
            ValidatePositiveNumber(purchasePrice);
            decimal newShareCount = checked(ShareCount + amount);
            decimal? totalCost = TotalCostBasis + purchasePrice * amount;
            // If historical cost is unknown, a purchase does not make it known.
            state = new HoldingState(newShareCount, totalCost / newShareCount);
        }
        internal void SellShares(decimal amount)
        {
            ValidatePositiveNumber(amount);
            if (amount > ShareCount)
            {
                throw new InvalidOperationException("Cannot sell more shares than currently held.");
            }
            state = new HoldingState(ShareCount - amount, AveragePurchasePrice);
        }
        private static void ValidatePositiveNumber(decimal number)
        {
            if (number <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(number), "Number must be greater than zero.");
            }
        }
    }
}
