using ConsoleApp.Presentation;
using Sep2026.Application.DTOs;
using Sep2026.Domain;
using Xunit;

namespace ConsoleApp.Tests;

public sealed class PortfolioRendererTests
{
    [Fact]
    public void Details_distinguish_unknown_and_zero_quotes_and_preserve_quote_currency_and_time()
    {
        var portfolioId = Guid.NewGuid();
        var quoteAt = new DateTimeOffset(2026, 11, 12, 10, 15, 0, TimeSpan.FromHours(2));
        var holdings = new[]
        {
            new HoldingDetailsDto(Guid.NewGuid(), portfolioId, Guid.NewGuid(), "ABC", "Stock", 2, 12.5m, null),
            new HoldingDetailsDto(Guid.NewGuid(), portfolioId, Guid.NewGuid(), "XYZ", "Stock", 3, 8m,
                new MarketQuoteDto("XYZ", 0m, "USD", quoteAt))
        };
        var details = new PortfolioDetailsDto(portfolioId, Guid.NewGuid(), 1234.56m,
            "Individual", "GBP", "Archived", quoteAt, holdings);
        using var output = new StringWriter();

        new PortfolioRenderer(output).ShowPortfolioDetails(details);

        var text = output.ToString();
        Assert.Contains("GBP 1,234.56", text);
        Assert.Contains("GBP 12.50", text);
        Assert.Contains("Archived", text);
        Assert.Contains("Price unavailable", text);
        Assert.Contains("USD 0.00", text);
        Assert.Contains("2026-11-12 10:15:00 +02:00", text);
    }

    [Fact]
    public void Empty_list_and_empty_holdings_have_explicit_messages()
    {
        using var output = new StringWriter();
        var renderer = new PortfolioRenderer(output);
        renderer.ShowPortfolioList(Guid.NewGuid(), Array.Empty<PortfolioSummaryDto>());
        renderer.ShowPortfolioDetails(new PortfolioDetailsDto(Guid.NewGuid(), Guid.NewGuid(), 0,
            "Individual", "USD", "Active", DateTimeOffset.UtcNow, Array.Empty<HoldingDetailsDto>()));

        Assert.Contains("No portfolios found", output.ToString());
        Assert.Contains("This portfolio has no holdings", output.ToString());
    }
}
