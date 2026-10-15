namespace Sep2026.FinancePortfolio
{
    public class UserModel
    {
        //Fields
        private readonly Guid userId;
        private string name;
        private string email;
        private List<PortfolioModel> portfolios;
        //Properties
        public Guid UserId
        {
            get { return userId; }
        }
        public string Name
        {
            get { return name; }
            private set
            {
                ArgumentNullException.ThrowIfNullOrWhiteSpace(value, nameof(value));
                Console.WriteLine($"Setting Name to {value} for User: {userId}");
                name = value;
            }
        }
        public string Email
        {
            get { return email; }
            private set
            {
                ArgumentNullException.ThrowIfNullOrWhiteSpace(value, nameof(value));
                Console.WriteLine($"Setting Email to {value} for User: {userId}");
                email = value;
            }
        }

        public List<PortfolioModel> Portfolios
        {
            get { return portfolios; }
        }
        //Default Constructor
        public UserModel(string name, string email)
        {
            //Argument Validation
            ArgumentNullException.ThrowIfNullOrWhiteSpace(name, nameof(name));
            ArgumentNullException.ThrowIfNullOrWhiteSpace(email, nameof(email));

            this.userId = Guid.NewGuid();
            this.name = name;
            this.email = email;
            this.portfolios = new List<PortfolioModel>();
        }

        //Methods
        public void AddPortfolio(PortfolioModel portfolio)
        {
            //Argument Validation
            ArgumentNullException.ThrowIfNull(portfolio, nameof(portfolio));
            if(Portfolios.Any(p => p.PortfolioId == portfolio.PortfolioId)) 
            {
                throw new InvalidOperationException($"Portfolio with ID {portfolio.PortfolioId} already exists for User: {userId}");
            }
            portfolios.Add(portfolio);
            Console.WriteLine($"Added Portfolio: {portfolio.PortfolioId} to User: {userId}");
        }
        public PortfolioModel CreatePortfolio()
        {
            PortfolioModel newPortfolio = new PortfolioModel();
            portfolios.Add(newPortfolio);
            Console.WriteLine($"Created new Portfolio: {newPortfolio.PortfolioId} for User: {userId}");
            return newPortfolio;
        }

        //Has to consider more detail logic for the method.
        // - Has to archieve or delete the portfolio, if the portfolio has any holdings, then it should not be deleted, but should be archieved.
        // -Need to consider how to handle the that portfolio first before removing it from the user.

        //public void RemovePortfolio(PortfolioModel portfolio)
        //{
        //    ArgumentNullException.ThrowIfNull(portfolio, nameof(portfolio));
        //    if (portfolios.Remove(portfolio))
        //    {
        //        Console.WriteLine($"Removed Portfolio: {portfolio.PortfolioId} from User: {userId}");
        //    }
        //    else
        //    {
        //        Console.WriteLine($"Portfolio: {portfolio.PortfolioId} not found for User: {userId}");
        //    }
        //}
        public void UpdateEmail(string newEmail)
        {
            ArgumentNullException.ThrowIfNullOrWhiteSpace(newEmail, nameof(newEmail));
            Email = newEmail;
        }
        public void UpdateName(string newName)
        {
            ArgumentNullException.ThrowIfNullOrWhiteSpace(newName, nameof(newName));
            Name = newName;
        }

        //Beta Validation method for email format, can be improved with regex or other libraries.
        internal void ValidateEmailFormat(string email)
        {
            // Simple email format validation
            if (!email.Contains("@") || !email.Contains("."))
            {
                throw new FormatException("Invalid email format.");
            }
        }

    }
}
