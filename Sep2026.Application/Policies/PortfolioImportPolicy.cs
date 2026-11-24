using Sep2026.Application.DTOs;
using Sep2026.Domain;

namespace Sep2026.Application.Policies;

// Release support and import-boundary checks. Financial invariants are enforced
// by Domain construction, which is also used independently of this policy.
public sealed class PortfolioImportPolicy
{
    // Early compatibility checks for the planned SQL representation. Infrastructure
    // must enforce these limits too; they are not universal financial invariants.
    private const decimal MaxQuantity = 9999999999999999999.999999999m;
    private const decimal MaxMoney = 99999999999.99999999m;
    public TimeSpan MaxSnapshotAge { get; }

    public PortfolioImportPolicy(TimeSpan? maxSnapshotAge = null)
    {
        MaxSnapshotAge = maxSnapshotAge ?? TimeSpan.FromMinutes(2);
        if (MaxSnapshotAge <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(maxSnapshotAge));
    }

    public LinkPortfolioRequest ValidateRequest(LinkPortfolioRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Enum.IsDefined(request.Provider)) throw new ArgumentOutOfRangeException(nameof(request.Provider));
        if (!Enum.IsDefined(request.Environment)) throw new ArgumentOutOfRangeException(nameof(request.Environment));
        if (request.Provider != InvestmentProvider.Alpaca || request.Environment != ProviderEnvironment.Paper)
            throw new NotSupportedException("Link Portfolio currently supports Alpaca paper accounts only.");
        var name = string.IsNullOrWhiteSpace(request.DisplayName) ? null : request.DisplayName.Trim();
        if (name?.Length > 200) throw new ArgumentException("The display name must not exceed 200 characters.", nameof(request));
        return request with { DisplayName = name };
    }

    public void ValidateAccountId(string accountId) => ValidateIdentifier(accountId, "account");

    public PortfolioSnapshot AcceptSnapshot(ExternalPortfolioSnapshotDto snapshot,
        string expectedAccountId, DateTimeOffset now)
    {
        ValidateAccountId(expectedAccountId);
        if (snapshot is null || !snapshot.IsComplete || snapshot.Positions is null ||
            !string.Equals(snapshot.ExternalAccountId, expectedAccountId, StringComparison.Ordinal))
            throw new InvalidDataException("A complete snapshot of the verified account is required.");
        if (snapshot.FetchStartedAt == default || snapshot.FetchCompletedAt < snapshot.FetchStartedAt ||
            snapshot.FetchCompletedAt > now || now - snapshot.FetchStartedAt > MaxSnapshotAge ||
            (snapshot.ProviderObservedAt is { } observedAt && observedAt == default))
            throw new InvalidDataException("The account snapshot has invalid or stale observation times.");

        var currency = RequireSupportedCurrency(snapshot.Currency);
        ValidateMoneyFitsStorage(snapshot.CashBalance);
        var positions = new List<PositionSnapshot>();
        var symbols = new HashSet<string>(StringComparer.Ordinal);
        foreach (var position in snapshot.Positions)
        {
            if (position is null) throw new InvalidDataException("Positions cannot contain null entries.");
            ValidateIdentifier(position.ExternalAssetId, "asset");
            if (!string.Equals(position.AssetClass?.Trim(), "us_equity", StringComparison.OrdinalIgnoreCase) ||
                position.Side != ExternalPositionSide.Long)
                throw new NotSupportedException("Only long US-equity positions are supported.");
            var symbol = position.Symbol?.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(symbol) || symbol.Length > 32 || !symbols.Add(symbol))
                throw new InvalidDataException("A position has an invalid or ambiguous symbol.");
            var positionCurrency = RequireSupportedCurrency(position.Currency);
            if (position.Quantity > MaxQuantity || decimal.Round(position.Quantity, 9) != position.Quantity)
                throw new NotSupportedException("Position quantities must fit decimal(28,9).");
            if (position.AverageCost is { } cost) ValidateMoneyFitsStorage(cost);
            try
            {
                positions.Add(new PositionSnapshot(position.ExternalAssetId, symbol, "Stock",
                    positionCurrency, position.Quantity, position.AverageCost));
            }
            catch (ArgumentOutOfRangeException exception)
            {
                throw new NotSupportedException("The reported position has unsupported financial state.", exception);
            }
        }
        try
        {
            return new PortfolioSnapshot(snapshot.ExternalAccountId, currency, snapshot.CashBalance, positions);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new NotSupportedException("The reported account has unsupported financial state.", exception);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("The account contains invalid or duplicate positions.", exception);
        }
    }

    private static CurrencyType RequireSupportedCurrency(string currency)
    {
        if (!string.Equals(currency?.Trim(), "USD", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Only USD accounts and positions are supported.");
        return CurrencyType.USD;
    }

    private static void ValidateMoneyFitsStorage(decimal value)
    {
        if (value > MaxMoney || decimal.Round(value, 8) != value)
            throw new NotSupportedException("Cash and reported costs must fit decimal(19,8).");
    }

    private static void ValidateIdentifier(string value, string kind)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 200 || value != value.Trim())
            throw new InvalidDataException($"The provider returned an invalid external {kind} identity.");
    }
}
