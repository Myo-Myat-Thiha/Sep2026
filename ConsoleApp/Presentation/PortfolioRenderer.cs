using System.Globalization;
using Sep2026.Application.DTOs;

namespace ConsoleApp.Presentation
{
    internal sealed class PortfolioRenderer
    {
        private readonly TextWriter output;
        private readonly Action clearScreen;

        public PortfolioRenderer(TextWriter output, Action? clearScreen = null)
        {
            ArgumentNullException.ThrowIfNull(output);
            this.output = output;
            this.clearScreen = clearScreen ?? (() =>
            {
                if (!ReferenceEquals(output, Console.Out) || Console.IsOutputRedirected) return;
                try { Console.Clear(); }
                catch (IOException) { /* Leave readable output on terminals that cannot clear. */ }
            });
        }

        public void ShowWelcome()
        {
            ShowHeader("Gemino - Portfolio browser");
            output.WriteLine("Browse saved portfolios and holdings.");
            output.WriteLine("Sign in with your email and password.");
            output.WriteLine("N: New profile   P: Set the first password for an existing local profile   Q: Exit");
            output.WriteLine();
        }

        public void ShowCreateUser()
        {
            ShowHeader("Gemino - Create portfolio-owner profile");
            output.WriteLine("This creates an owner profile for portfolios.");
        }

        public void ShowUserCreated(UserDto user)
        {
            ShowHeader("Gemino - Profile created");
            output.WriteLine($"Profile created: {user.Name}");
            output.WriteLine($"Email: {user.Email}");
        }

        public void ShowLoading(string message)
        {
            ShowHeader("Gemino");
            output.WriteLine(message);
        }

        public void ShowPasswordSetup()
        {
            ShowHeader("Gemino - Set initial password");
            output.WriteLine("For existing profiles without a password, on this trusted device only.");
            output.WriteLine("This cannot replace an existing password. B: Back   Q: Exit");
        }

        public void ShowLoadingList(UserDto user)
        {
            ShowHeader("Gemino - Portfolios");
            output.WriteLine($"Owner: {user.Name} ({user.Email})");
            output.WriteLine("Loading saved portfolios...");
        }

        public void ShowPortfolioList(UserDto user, IReadOnlyList<PortfolioSummaryDto> portfolios)
        {
            ShowHeader("Gemino - Portfolios");
            output.WriteLine($"Owner: {user.Name} ({user.Email})");
            output.WriteLine();

            if (portfolios.Count == 0)
            {
                output.WriteLine("No portfolios found for this user ID.");
                return;
            }

            output.WriteLine($"{"#",3}  {"Type",-12}  {"Currency",-8}  {"Cash balance",22}  {"Status",-9}  Created");
            output.WriteLine(new string('-', 87));
            for (var index = 0; index < portfolios.Count; index++)
            {
                var portfolio = portfolios[index];
                var created = portfolio.CreatedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                output.WriteLine($"{index + 1,3}  {portfolio.PortfolioType,-12}  {portfolio.CurrencyType,-8}  {FormatAmount(portfolio.CashBalance),22}  {portfolio.Status,-9}  {created}");
            }
        }

        public void ShowLoadingDetails(Guid portfolioId)
        {
            ShowHeader("Gemino - Portfolio details");
            output.WriteLine($"Loading portfolio {portfolioId}...");
        }

        public void ShowPortfolioDetails(PortfolioDetailsDto portfolio)
        {
            ShowHeader("Gemino - Portfolio details");
            output.WriteLine($"Portfolio:    {portfolio.PortfolioId}");
            output.WriteLine($"Owner:        {portfolio.OwnerId}");
            output.WriteLine($"Type:         {portfolio.PortfolioType}");
            output.WriteLine($"Status:       {portfolio.Status}");
            output.WriteLine($"Currency:     {portfolio.CurrencyType}");
            output.WriteLine($"Cash balance: {portfolio.CurrencyType} {FormatAmount(portfolio.CashBalance)}");
            output.WriteLine($"Created:      {FormatTimestamp(portfolio.CreatedAt)}");
            output.WriteLine();
            output.WriteLine($"Holdings ({portfolio.Holdings.Count})");
            output.WriteLine(new string('-', 72));

            if (portfolio.Holdings.Count == 0)
            {
                output.WriteLine("This portfolio has no holdings.");
                return;
            }

            foreach (var holding in portfolio.Holdings)
            {
                output.WriteLine($"{holding.TickerSymbol} - {holding.SecurityType}");
                output.WriteLine($"  Holding ID:             {holding.HoldingId}");
                output.WriteLine($"  Shares:                 {holding.ShareCount.ToString("N0", CultureInfo.InvariantCulture)}");
                output.WriteLine($"  Average purchase price: {portfolio.CurrencyType} {FormatAmount(holding.AveragePurchasePrice)}");

                var quote = holding.LatestQuote;
                if (quote is null)
                {
                    output.WriteLine("  Cached market price:    Price unavailable");
                }
                else
                {
                    output.WriteLine($"  Cached market price:    {quote.CurrencyType} {FormatAmount(quote.Price)}");
                    output.WriteLine($"  Market timestamp:       {FormatTimestamp(quote.QuoteAt)}");
                }

                output.WriteLine();
            }
        }

        public void ShowMissingPortfolio(Guid portfolioId)
        {
            ShowHeader("Gemino - Portfolio unavailable");
            output.WriteLine($"Portfolio {portfolioId} was not found. It may have been removed since the list was loaded.");
            output.WriteLine("Return to the list and use R to reload it.");
        }

        public void ShowError(string message)
        {
            ShowHeader("Gemino - Unable to complete");
            output.WriteLine($"Error: {message}");
        }

        public void ShowNotice(string message) => output.WriteLine(message);

        public void ShowGoodbye()
        {
            output.WriteLine();
            output.WriteLine("Goodbye from Gemino.");
        }

        private void ShowHeader(string title)
        {
            clearScreen();
            output.WriteLine();
            output.WriteLine(new string('=', 72));
            output.WriteLine(title);
            output.WriteLine(new string('=', 72));
        }

        private static string FormatAmount(decimal value)
            => value.ToString("#,##0.00######", CultureInfo.InvariantCulture);

        private static string FormatTimestamp(DateTimeOffset value)
            => value.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);
    }
}
