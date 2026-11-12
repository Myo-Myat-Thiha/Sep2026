namespace Sep2026.Domain
{
    public class UserModel
    {
        //Fields
        private readonly Guid userId;
        private string name;
        private string email;
        private readonly List<PortfolioModel> portfolios;
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
        public IReadOnlyList<PortfolioModel> Portfolios => portfolios.AsReadOnly();

        //Default Constructor
        public UserModel(string name, string email)
            : this(Guid.NewGuid(), name, email, Array.Empty<PortfolioModel>())
        {
        }

        //Restoring the Existing User from the Database.
        private UserModel(
            Guid userId,
            string name,
            string email,
            IEnumerable<PortfolioModel> portfolios)
        {
            if (userId == Guid.Empty)
                throw new ArgumentException("A user ID is required.", nameof(userId));
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentException.ThrowIfNullOrWhiteSpace(email);
            ValidateEmailFormat(email);
            ArgumentNullException.ThrowIfNull(portfolios);

            var restoredPortfolios = portfolios.ToList();
            var portfolioIds = new HashSet<Guid>();
            foreach (var portfolio in restoredPortfolios)
            {
                if (portfolio is null)
                    throw new ArgumentException("Portfolios cannot contain null entries.", nameof(portfolios));
                if (portfolio.OwnerId != userId)
                    throw new ArgumentException("Every portfolio must belong to this user.", nameof(portfolios));
                if (!portfolioIds.Add(portfolio.PortfolioId))
                    throw new ArgumentException("Duplicate portfolio IDs are not allowed.", nameof(portfolios));
            }

            this.userId = userId;
            this.name = name;
            this.email = email;
            this.portfolios = restoredPortfolios;
        }

        // Restore existing identity and state without replaying business operations.
        public static UserModel Restore(
            Guid userId,
            string name,
            string email,
            IEnumerable<PortfolioModel> portfolios)
            => new UserModel(userId, name, email, portfolios);

        //Methods
        public void AddPortfolio(PortfolioModel portfolio)
        {
            //Argument Validation
            ArgumentNullException.ThrowIfNull(portfolio, nameof(portfolio));
            if(portfolio.OwnerId != UserId)
            {
                throw new ArgumentException(
                    @"The portfolio does not belong to this user. It already belongs to another user.
                    Maybe in other lifetimes or different multiverse (╥﹏╥).",
                    nameof(portfolio)
                    );
            }
            if(Portfolios.Any(p => p.PortfolioId == portfolio.PortfolioId)) 
            {
                throw new InvalidOperationException($"Portfolio with ID {portfolio.PortfolioId} is already exists for User: {userId}");
            }
            portfolios.Add(portfolio);
            Console.WriteLine($"Added Portfolio: {portfolio.PortfolioId} to User: {userId}");
        }
        public PortfolioModel CreatePortfolio()
        {
            PortfolioModel newPortfolio = new PortfolioModel(UserId);
            AddPortfolio(newPortfolio);
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
            ValidateEmailFormat(newEmail);
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
