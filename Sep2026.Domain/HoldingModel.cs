namespace Sep2026.Domain
{
    public class HoldingModel
    {
        //Feilds
        private readonly Guid holdingId;

        private readonly SecurityModel security;
        private int shareCount;
        private decimal averagePurchasePrice;

        public Guid HoldingId => holdingId;
        public SecurityModel Security => security;
        public string TickerSymbol => security.TickerSymbol;
        public Guid SecurityId => security.SecurityId;
        public Guid PortfolioId { get; }
        public bool IsEmpty => shareCount == 0;
        public decimal TotalCostBasis => shareCount * averagePurchasePrice;
        // Nullable arithmetic keeps valuations unknown until a market quote is available.
        public decimal? CurrentMarketValue => shareCount * security.CurrentValue;
        public decimal? UnrealizedProfitLoss => CurrentMarketValue - TotalCostBasis;
        public decimal? UnrealizedProfitLossPercentage => TotalCostBasis == 0 ? 0 : (UnrealizedProfitLoss / TotalCostBasis) * 100;
        

        public int ShareCount
        {
            get { return shareCount; }
            private set 
            {
                ValidateNonNegative(value);
                Console.WriteLine($"Setting shares to {value} for {TickerSymbol}: holding {holdingId}, portfolio {PortfolioId}");
                shareCount = value;
            }
        }
        public decimal AveragePurchasePrice
        {
            get { return averagePurchasePrice; }
            private set
            {
                ValidatePositiveNumber(value);
                Console.WriteLine($"Setting average purchase price to {value} for {TickerSymbol}: holding {holdingId}, portfolio {PortfolioId}");
                averagePurchasePrice = value;
            }
        }
        public HoldingModel(SecurityModel security, Guid portfolioId)
            : this(Guid.NewGuid(), security, portfolioId, 0, 0m)
        {
        }
        public HoldingModel(SecurityModel security, Guid portfolioId, int share, decimal averagePurchasePrice)
            : this(Guid.NewGuid(), security, portfolioId, share, averagePurchasePrice)
        {
            // New funded holdings require positive quantities; restoration also accepts empty holdings.
            ValidatePositiveNumber(share);
        }

        //Restoring the Existing Holding from the Database.
        private HoldingModel(Guid holdingId, SecurityModel security,Guid portfolioId, int shareCount, decimal averagePurchasePrice)
        {
            if (holdingId == Guid.Empty)
                throw new ArgumentException("A holding ID is required.", nameof(holdingId));
            if (portfolioId == Guid.Empty)
                throw new ArgumentException("A portfolio ID is required.", nameof(portfolioId));
            ArgumentNullException.ThrowIfNull(security);
            ArgumentOutOfRangeException.ThrowIfNegative(shareCount);
            ArgumentOutOfRangeException.ThrowIfNegative(averagePurchasePrice);
            if (shareCount > 0 && averagePurchasePrice == 0)
                throw new ArgumentOutOfRangeException(nameof(averagePurchasePrice),
                    "A nonempty holding must have a positive average purchase price.");
            this.holdingId = holdingId;
            this.security = security;
            this.PortfolioId = portfolioId;
            this.shareCount = shareCount;
            this.averagePurchasePrice = averagePurchasePrice;
        }

        public static HoldingModel Restore(
            Guid holdingId,
            SecurityModel security,
            Guid portfolioId,
            int shareCount,
            decimal averagePurchasePrice)
            => new HoldingModel(holdingId, security, portfolioId, shareCount, averagePurchasePrice);

        internal void BuyShares(int amount, decimal purchasePrice)
        {
            ValidatePositiveNumber(amount);
            ValidatePositiveNumber(purchasePrice);
            decimal totalCost = averagePurchasePrice * shareCount + purchasePrice * amount;
            int newShareCount = checked(shareCount + amount);
            decimal newAveragePurchasePrice = totalCost / newShareCount;
            ShareCount = newShareCount;
            AveragePurchasePrice = newAveragePurchasePrice;
            Console.WriteLine($"Bought {amount} shares of {TickerSymbol} at {purchasePrice} each. New average purchase price: {newAveragePurchasePrice}");
        }
        internal void SellShares(int amount)
        {
            ValidatePositiveNumber(amount);
            if (amount > shareCount)
            {
                throw new InvalidOperationException("Cannot sell more shares than currently held.");
            }
            ShareCount -= amount;
            Console.WriteLine($"Sold {amount} shares of {TickerSymbol}. Remaining shares: {shareCount}");
        }
        private static void ValidatePositiveNumber(decimal number)
        {
            if (number <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(number), "Number must be greater than zero.");
            }
        }
        private static void ValidateNonNegative(decimal number)
        {
            if (number < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(number), "Number must be non-negative.");
            }
        }
    }
}
