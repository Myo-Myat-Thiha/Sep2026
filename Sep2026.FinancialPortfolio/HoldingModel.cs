using System.Runtime.CompilerServices;

namespace Sep2026.FinancePortfolio
{
    public class HoldingModel
    {
        private readonly Guid holdingId;
        private readonly PortfolioModel portfolio;
        private SecurityModel security;
        private string tickerSymbol;
        private int share;
        private decimal averagePurchasePrice;

        public Guid HoldingId => holdingId;
        public PortfolioModel Portfolio => portfolio;
        public SecurityModel Security => security;
        public string TickerSymbol => tickerSymbol;
        public int Share
        {
            get { return share; }
            private set 
            {
                ValidatePositiveNumber(value);
                Console.WriteLine($"Setting Share to {value} for {tickerSymbol}: {holdingId} of User: {portfolio.OwnerId}'s {portfolio.PortfolioId} ");
                share = value;
            }
        }
        public decimal AveragePurchasePrice
        {
            get { return averagePurchasePrice; }
            private set
            {
                ValidatePositiveNumber((int)value);
                Console.WriteLine($"Setting AveragePurchasePrice to {value} for {tickerSymbol}: {holdingId} of User: {portfolio.OwnerId}'s {portfolio.PortfolioId} ");
                averagePurchasePrice = value;
            }
        }
        public HoldingModel(SecurityModel security, PortfolioModel portfolio)
        {
            this.holdingId = Guid.NewGuid();
            this.tickerSymbol = security.TickerSymbol;
            this.security = security;
            this.share = 0;
            this.averagePurchasePrice = 0;
            this.portfolio = portfolio;
        }
        public HoldingModel(SecurityModel security, PortfolioModel portfolio, int share, decimal averagePurchasePrice)
        {
            this.holdingId = Guid.NewGuid();
            this.tickerSymbol = security.TickerSymbol;
            this.security = security;
            this.share = share;
            this.averagePurchasePrice = averagePurchasePrice;
            this.portfolio = portfolio;
        }
        internal void IncreaseShares(int amount)
        {
            if (amount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(amount), "Amount must be greater than zero.");
            }
            Share += amount;
        }
        internal void BuyShares(int amount, decimal purchasePrice)
        {
            ValidatePositiveNumber(amount);
            ValidatePositiveNumber((int)purchasePrice);
            decimal totalCost = averagePurchasePrice * share + purchasePrice * amount;
            share += amount;
            averagePurchasePrice = totalCost / share;
            Console.WriteLine($"Bought {amount} shares of {tickerSymbol} at {purchasePrice} each. New average purchase price: {averagePurchasePrice}");
        }
        internal void SellShares(int amount)
        {
            ValidatePositiveNumber(amount);
            if (amount > share)
            {
                throw new InvalidOperationException("Cannot sell more shares than currently held.");
            }
            share -= amount;
            Console.WriteLine($"Sold {amount} shares of {tickerSymbol}. Remaining shares: {share}");
        }
        internal void ValidatePositiveNumber(decimal number)
        {
            if (number <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(number), "Number must be greater than zero.");
            }
        }
    }
}
