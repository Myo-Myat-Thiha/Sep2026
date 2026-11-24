namespace Sep2026.Domain;

// Immutable accepted financial state, independent of transport DTOs, credentials,
// collection deadlines, and local persistence identifiers.
public sealed class PortfolioSnapshot
{
    public string ExternalAccountId { get; }
    public CurrencyType Currency { get; }
    public decimal CashBalance { get; }
    public IReadOnlyList<PositionSnapshot> Positions { get; }

    public PortfolioSnapshot(string externalAccountId, CurrencyType currency,
        decimal cashBalance, IEnumerable<PositionSnapshot> positions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalAccountId);
        if (externalAccountId != externalAccountId.Trim())
            throw new ArgumentException("External account identity must be preserved exactly.", nameof(externalAccountId));
        if (!Enum.IsDefined(currency)) throw new ArgumentOutOfRangeException(nameof(currency));
        if (cashBalance < 0) throw new ArgumentOutOfRangeException(nameof(cashBalance));
        ArgumentNullException.ThrowIfNull(positions);
        var acceptedPositions = positions.ToArray();
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var position in acceptedPositions)
        {
            if (position is null) throw new ArgumentException("Positions cannot contain null entries.", nameof(positions));
            if (!identities.Add(position.ExternalAssetId))
                throw new ArgumentException("A position's external asset identity must be unique within the account.", nameof(positions));
        }
        ExternalAccountId = externalAccountId;
        Currency = currency;
        CashBalance = cashBalance;
        Positions = Array.AsReadOnly(acceptedPositions);
    }
}

public sealed class PositionSnapshot
{
    public string ExternalAssetId { get; }
    public string Symbol { get; }
    public string SecurityType { get; }
    public CurrencyType Currency { get; }
    public HoldingState State { get; }
    public decimal Quantity => State.Quantity;
    public decimal? AverageCost => State.AverageCost;

    public PositionSnapshot(string externalAssetId, string symbol, string securityType,
        CurrencyType currency, decimal quantity, decimal? averageCost)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalAssetId);
        if (externalAssetId != externalAssetId.Trim())
            throw new ArgumentException("External asset identity must be preserved exactly.", nameof(externalAssetId));
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentException.ThrowIfNullOrWhiteSpace(securityType);
        if (!Enum.IsDefined(currency)) throw new ArgumentOutOfRangeException(nameof(currency));
        // Snapshots describe open positions; saved holdings can also be empty.
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        State = new HoldingState(quantity, averageCost);
        ExternalAssetId = externalAssetId;
        Symbol = symbol.Trim().ToUpperInvariant();
        SecurityType = securityType.Trim();
        Currency = currency;
    }
}
