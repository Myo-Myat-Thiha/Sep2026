using System.Net.NetworkInformation;

namespace Sep2026.FinancePortfolio
{
    public enum CurrencyType{ USD, EUR, GBP, JPY, AUD, CAD, MMK}
    public enum PortfolioType
    {
        Individual,
        Joint,
        Retirement,
        Education,
        Trust,
        Corporate
    }
    public enum PortfolioStatus
    {
        Active,
        Archived
    }
    public class PortfolioModel
    {
        //Fields
        private readonly Guid portfolioId;
        private decimal cashBalance;
        private List<HoldingModel> holdings;

        //Properties
        public Guid PortfolioId => portfolioId;
        public decimal CashBalance
        {
            get { return cashBalance; }
            private set
            {
                ValidateNonNegative(value);
                cashBalance = value;
            }
        }
        public IReadOnlyList<HoldingModel> Holdings => holdings.AsReadOnly();
        public Guid OwnerId { get; } //Dependency
        public PortfolioType PortfolioType { get; private set; }
        public CurrencyType CurrencyType { get; private set; }
        public DateTimeOffset CreatedAt { get; } //Has to go to entity later
        public PortfolioStatus Status { get; private set; }

        //Defualt Constructor
        //To make sure that the portfolio is always associated with a specific user, we require the ownerId to be passed in during construction.
        public PortfolioModel(Guid ownerId)
        {
            if(Guid.Empty.Equals(ownerId))
            {
                throw new ArgumentException("An owner ID is required and cannot be empty.", nameof(ownerId));
            }
            this.portfolioId    = Guid.NewGuid();
            this.cashBalance    = 0;
            this.holdings       = new List<HoldingModel>();
            this.OwnerId        = ownerId;
            this.PortfolioType  = PortfolioType.Individual;  //Default
            this.CurrencyType   = CurrencyType.USD;          //Default
            this.Status         = PortfolioStatus.Active;    //Default
            this.CreatedAt      = DateTimeOffset.UtcNow;
        }
        public void WithdrawCash(decimal cashAmount)
        {
            EnsureActive();

            //Argument Validation
            ValidateGreaterThanZero(cashAmount, nameof(cashAmount));
            if (cashAmount > CashBalance)
            {
                throw new InvalidOperationException("Insufficient funds for withdrawal.");
            }
            CashBalance -= cashAmount;
            //Log the withdrawal action
            Console.WriteLine($"Withdrew {cashAmount} from portfolio {portfolioId}");

        }

        public void DepositCash(decimal depositCashAmount)
        {
            EnsureActive();

            //Argument Validation
            ValidateGreaterThanZero(depositCashAmount, nameof(depositCashAmount));

            CashBalance += depositCashAmount;
            //Log the deposit action
            Console.WriteLine($"Deposited {depositCashAmount} into portfolio {portfolioId}");

        }
        //
        internal void AddHolding(SecurityModel security)
        {
            EnsureActive();

            //Argument Validation
            ArgumentNullException.ThrowIfNull(security, nameof(security));
            //Check if the holding already exists in the portfolio
            if(holdings.Any(h => h.Security.SecurityId == security.SecurityId))
            {
                throw new InvalidOperationException($"Holding for security {security.TickerSymbol} already exists in portfolio {portfolioId}. Cannot add duplicate holding.");
            }
            var newHolding = new HoldingModel(security, this);
            holdings.Add(newHolding);
        }
        public HoldingModel? GetHolding(Guid securityId)
        {
            return holdings.FirstOrDefault(h => h.Security.SecurityId == securityId);
        }

        internal void ApplySell(SecurityModel security, int shareQuantity, decimal sellAmount)
        {
            EnsureActive();

            ArgumentNullException.ThrowIfNull(security, nameof(security));
            ValidateGreaterThanZero(shareQuantity, nameof(shareQuantity));
            ValidateGreaterThanZero(sellAmount, nameof(sellAmount));

            var holding = GetHolding(security.SecurityId);
            if (holding == null)
            {
                throw new InvalidOperationException("Holding not found in portfolio.");
            }
            if (holding.Share < shareQuantity)
            {
                throw new InvalidOperationException("Insufficient shares to sell.");
            }
            decimal totalSellValue = shareQuantity * sellAmount;
            decimal totalCost = shareQuantity * holding.AveragePurchasePrice;

            CashBalance += totalSellValue;
            decimal realizedProfitOrLoss = totalSellValue - totalCost;
            holding.SellShares(shareQuantity);
            Console.WriteLine($"Sold {shareQuantity} shares of {security.TickerSymbol} for a total of {totalSellValue}. Realized profit/loss: {realizedProfitOrLoss}. New cash balance: {CashBalance}");
        }
        internal void ApplyBuy(SecurityModel security, int shareQuantity, decimal purchaseAmount)
        {
            EnsureActive();

            ArgumentNullException.ThrowIfNull(security, nameof(security));
            ValidateGreaterThanZero(shareQuantity, nameof(shareQuantity));
            ValidateGreaterThanZero(purchaseAmount, nameof(purchaseAmount));

            decimal totalCost = shareQuantity * purchaseAmount;
            if(CashBalance < totalCost)
            {
                throw new InvalidOperationException("Insufficient cash to purchase shares.");
            }
            HoldingModel? existingHolding = GetHolding(security.SecurityId);
            if (existingHolding == null)
            {
                var holding = new HoldingModel(security, this, shareQuantity, purchaseAmount);
                holdings.Add(holding);
                Console.WriteLine($"Added new holding for security {security.TickerSymbol} with {shareQuantity} shares at {purchaseAmount} each to portfolio {portfolioId}");
            }
            else
            {
                existingHolding.BuyShares(shareQuantity, purchaseAmount);
                Console.WriteLine($"Updated existing holding for security {security.TickerSymbol} with additional {shareQuantity} shares at {purchaseAmount} each in portfolio {portfolioId}");
            }
            CashBalance -= totalCost;
            Console.WriteLine($"Purchased {shareQuantity} shares of {security.TickerSymbol} for a total of {totalCost}. New cash balance: {CashBalance}");
        }
        public void SetPortfolioType(PortfolioType portfolioType)
        {
            EnsureActive();

            if (!Enum.IsDefined(portfolioType))
            {
                throw new ArgumentOutOfRangeException(nameof(portfolioType),"Invalid portfolio type.");
            }
            PortfolioType = portfolioType;
            Console.WriteLine($"Portfolio {portfolioId} type set to {portfolioType}");
        }

        //This Method is dummy it just gonna change the currency type of the portfolio.
        //It doesn't include the logic of currency conversion and updating the cash balance.
        public void SetCurrencyType(CurrencyType currencyType)
        {
            EnsureActive();

            if(!Enum.IsDefined(currencyType))
            {
                throw new ArgumentOutOfRangeException(nameof(currencyType),"Invalid currency type.");
            }
            CurrencyType = currencyType;
            Console.WriteLine($"Portfolio {portfolioId} currency type set to {currencyType}");
        }

        public void ReactivatePortfolio()
        {
            if (Status == PortfolioStatus.Active)
            {
                throw new InvalidOperationException("Portfolio is already active.");
            }
            Status = PortfolioStatus.Active;
            Console.WriteLine($"Portfolio {portfolioId} has been reactivated.");
        }
        public void ArchivePortfolio()
        {
            if (Status == PortfolioStatus.Archived)
            {
                throw new InvalidOperationException("Portfolio is already archived.");
            }
            Status = PortfolioStatus.Archived;
            Console.WriteLine($"Portfolio {portfolioId} has been archived.");
        }
        private void EnsureActive()
        {
            if (Status == PortfolioStatus.Archived)
            {
                throw new InvalidOperationException("Archived portfolios cannot be modified. Reactivate the portfolio first.");
            }
        }

        internal static void ValidateNonNegative(decimal value)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Value must be greater than or equal to zero.");
            }
        }
        private static void ValidateGreaterThanZero(decimal amount, string parameterName)
        {
            if (amount <= 0)
            {
                throw new ArgumentException($"{parameterName} must be greater than zero.");
            }
        }
    }
}
