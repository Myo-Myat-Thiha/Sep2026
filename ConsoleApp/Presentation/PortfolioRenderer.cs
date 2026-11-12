using System;
using System.Globalization;
using Sep2026.Application.DTOs;

namespace ConsoleApp.Presentation
{
    internal sealed class PortfolioRenderer
    {
        private readonly TextWriter output;

        public PortfolioRenderer(TextWriter output)
        {
            ArgumentNullException.ThrowIfNull(output);
            this.output = output;
        }

        public void ShowWelcome()
        {
            ShowHeader("Gemino - Portfolio browser");
            output.WriteLine("Browse saved portfolios and holdings.");
            output.WriteLine("Enter a user ID from your Gemino database to begin.");
            output.WriteLine();
        }

        public void ShowLoadingList(Guid userId)
        {
            ShowHeader("Gemino - Portfolios");
            output.WriteLine($"User: {userId}");
            output.WriteLine("Loading saved portfolios...");
        }

        public void ShowPortfolioList(Guid userId, IReadOnlyList<PortfolioSummaryDto> portfolios)
        {
            ShowHeader("Gemino - Portfolios");
            output.WriteLine($"User: {userId}");
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
            output.WriteLine($"Portfolio {portfolioId} was not found. It may have been removed since the list was loaded.");
            output.WriteLine("Return to the list and use R to reload it.");
        }

        public void ShowError(string message) => output.WriteLine($"Error: {message}");

        public void ShowGoodbye()
        {
            output.WriteLine();
            output.WriteLine("Goodbye from Gemino.");
        }

        private void ShowHeader(string title)
        {
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
