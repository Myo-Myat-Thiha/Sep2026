namespace Sep2026.Domain
{
    public class HoldingModel
    {
        //Feilds
        private readonly Guid holdingId;

        //Will Remove later, doesn't need the whole object, just the PortfolioId is enough, but for now keeping it for logging purpose.
        private readonly PortfolioModel portfolio;  
        private readonly SecurityModel security;
        private int shareCount;
        private decimal averagePurchasePrice;

        public Guid HoldingId => holdingId;
        public PortfolioModel Portfolio => portfolio;
        public SecurityModel Security => security;
        public string TickerSymbol => security.TickerSymbol;
        public Guid SecurityId => security.SecurityId;
        public Guid PortfolioId => portfolio.PortfolioId;
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
                Console.WriteLine($"Setting Share to {value} for {TickerSymbol}: {holdingId} of User: {portfolio.OwnerId}'s {portfolio.PortfolioId} ");
                shareCount = value;
            }
        }
        public decimal AveragePurchasePrice
        {
            get { return averagePurchasePrice; }
            private set
            {
                ValidatePositiveNumber(value);
                Console.WriteLine($"Setting AveragePurchasePrice to {value} for {TickerSymbol}: {holdingId} of User: {portfolio.OwnerId}'s {portfolio.PortfolioId} ");
                averagePurchasePrice = value;
            }
        }
        public HoldingModel(SecurityModel security, PortfolioModel portfolio)
        {
            ArgumentNullException.ThrowIfNull(security, nameof(security));
            ArgumentNullException.ThrowIfNull(portfolio, nameof(portfolio));
            this.holdingId = Guid.NewGuid();
            this.security = security;
            this.shareCount = 0;
            this.averagePurchasePrice = 0;
            this.portfolio = portfolio;
        }
        public HoldingModel(SecurityModel security, PortfolioModel portfolio, int share, decimal averagePurchasePrice)
        {
            // Argument validation
            ArgumentNullException.ThrowIfNull(security, nameof(security));
            ArgumentNullException.ThrowIfNull(portfolio, nameof(portfolio));

            ValidatePositiveNumber(share);
            ValidatePositiveNumber(averagePurchasePrice);

            this.holdingId = Guid.NewGuid();
            this.security = security;
            this.shareCount = share;
            this.averagePurchasePrice = averagePurchasePrice;
            this.portfolio = portfolio;
        }
        internal void BuyShares(int amount, decimal purchasePrice)
        {
            ValidatePositiveNumber(amount);
            ValidatePositiveNumber(purchasePrice);
            decimal totalCost = averagePurchasePrice * shareCount + purchasePrice * amount;
            int newShareCount = checked(shareCount + amount);
            decimal newAveragePurchasePrice = totalCost / newShareCount;
            ShareCount += newShareCount;
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
