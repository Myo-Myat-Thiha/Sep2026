using Sep2026.Application.DTOs;
using Sep2026.Application.Policies;
using Sep2026.Domain;
using Xunit;

namespace Sep2026.Application.Tests;

public sealed class PortfolioImportPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FreshnessCanBeConfiguredWithoutChangingTheServiceOrDomain()
    {
        var data = Snapshot() with { FetchStartedAt = Now.AddMinutes(-3), FetchCompletedAt = Now.AddMinutes(-2) };
        Assert.Throws<InvalidDataException>(() => new PortfolioImportPolicy().AcceptSnapshot(data, "account", Now));
        var accepted = new PortfolioImportPolicy(TimeSpan.FromMinutes(5)).AcceptSnapshot(data, "account", Now);
        Assert.Equal(CurrencyType.USD, accepted.Currency);
        Assert.Equal("account", accepted.ExternalAccountId);
    }

    [Fact]
    public void PolicyRejectsStoragePrecisionThatDomainFinancialStateCanRepresent()
    {
        var position = new ExternalPositionDto("asset", "AAPL", "us_equity", "USD", ExternalPositionSide.Long, 0.1234567891m, null);
        Assert.Throws<NotSupportedException>(() => new PortfolioImportPolicy().AcceptSnapshot(
            Snapshot() with { Positions = new[] { position } }, "account", Now));
        var financialState = new HoldingState(position.Quantity, null);
        Assert.Equal(position.Quantity, financialState.Quantity);
    }

    [Fact]
    public void PolicyRejectsUnsupportedCurrencyWithoutTurningItIntoADomainInvariant()
    {
        Assert.Throws<NotSupportedException>(() => new PortfolioImportPolicy().AcceptSnapshot(Snapshot() with { Currency = "GBP" }, "account", Now));
        Assert.Equal(CurrencyType.GBP, new PortfolioSnapshot("account", CurrencyType.GBP, 0, Array.Empty<PositionSnapshot>()).Currency);
    }

    [Fact]
    public void PolicyMapsFinancialFailuresFromDomainAndKeepsTheCause()
    {
        var failure = Assert.Throws<NotSupportedException>(() => new PortfolioImportPolicy().AcceptSnapshot(Snapshot() with { CashBalance = -1 }, "account", Now));
        Assert.IsType<ArgumentOutOfRangeException>(failure.InnerException);
    }

    [Fact]
    public void InvalidFreshnessLimitIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PortfolioImportPolicy(TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PortfolioImportPolicy(TimeSpan.FromMinutes(-1)));
    }

    private static ExternalPortfolioSnapshotDto Snapshot() => new("account", "USD", 0,
        Array.Empty<ExternalPositionDto>(), Now, Now, true);
}
