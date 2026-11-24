using ConsoleApp.Presentation;
using Sep2026.Application.DTOs;
using Xunit;

namespace ConsoleApp.Tests;

public sealed class PortfolioRendererTests
{
    [Fact]
    public void LinkedViewsIdentifyProviderEnvironmentAndSavedObservationTimes()
    {
        var now = DateTimeOffset.UtcNow;
        var connection = new PortfolioConnectionDto(InvestmentProvider.Alpaca, ProviderEnvironment.Paper,
            ProviderConnectionStatus.Connected, now, now.AddSeconds(-2), now, null);
        using var output = new StringWriter();
        new PortfolioRenderer(output).ShowPortfolioList(new(Guid.NewGuid(), "Alice", "alice@example.com"), new[]
        {
            new PortfolioSummaryDto(Guid.NewGuid(), Sep2026.Domain.PortfolioType.Unspecified, Sep2026.Domain.CurrencyType.USD,
                123, Sep2026.Domain.PortfolioStatus.Active, now, "My paper account", connection)
        });
        Assert.Contains("Alpaca / Paper / Connected", output.ToString());
        Assert.Contains("Last successful sync", output.ToString());
        Assert.Contains("My paper account", output.ToString());
        Assert.Contains("last saved import", output.ToString());
    }
    [Fact]
    public void LoadingAndResultScreensEachClearBeforeDisplayingTheirContents()
    {
        using var output = new StringWriter();
        var clears = 0;
        var renderer = new PortfolioRenderer(output, () => clears++);
        var user = new UserDto(Guid.NewGuid(), "Alice", "alice@example.com");
        renderer.ShowLoadingList(user);
        renderer.ShowPortfolioList(user, Array.Empty<PortfolioSummaryDto>());
        renderer.ShowError("Connection failed.");
        Assert.Equal(3, clears);
        Assert.Contains("Loading saved portfolios", output.ToString());
        Assert.Contains("Alice (alice@example.com)", output.ToString());
        Assert.Contains("No portfolios found", output.ToString());
        Assert.DoesNotContain(user.UserId.ToString(), output.ToString());
    }
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
    public void DetailsPreserveFractionalSharesAndDistinguishUnavailableCostFromZero()
    {
        var portfolioId = Guid.NewGuid();
        var holdings = new[]
        {
            new HoldingDetailsDto(Guid.NewGuid(), portfolioId, Guid.NewGuid(), "UNKNOWN", "Stock", 1.123456789m, null, null),
            new HoldingDetailsDto(Guid.NewGuid(), portfolioId, Guid.NewGuid(), "ZERO", "Stock", 0.5m, 0, null)
        };
        using var output = new StringWriter();
        new PortfolioRenderer(output).ShowPortfolioDetails(new PortfolioDetailsDto(portfolioId, Guid.NewGuid(), 0,
            "Individual", "USD", "Active", DateTimeOffset.UtcNow, holdings));
        var text = output.ToString();
        Assert.Contains("1.123456789", text);
        Assert.Contains("Cost unavailable", text);
        Assert.Contains("USD 0.00", text);
    }

    [Fact]
    public void Empty_list_and_empty_holdings_have_explicit_messages()
    {
        using var output = new StringWriter();
        var renderer = new PortfolioRenderer(output);
        renderer.ShowPortfolioList(new UserDto(Guid.NewGuid(), "Alice", "alice@example.com"), Array.Empty<PortfolioSummaryDto>());
        renderer.ShowPortfolioDetails(new PortfolioDetailsDto(Guid.NewGuid(), Guid.NewGuid(), 0,
            "Individual", "USD", "Active", DateTimeOffset.UtcNow, Array.Empty<HoldingDetailsDto>()));

        Assert.Contains("No portfolios found", output.ToString());
        Assert.Contains("This portfolio has no holdings", output.ToString());
    }
}
