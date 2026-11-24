namespace Sep2026.Domain;

// Shared financial state for saved holdings and incoming positions. It contains
// no provider, import-release, or SQL precision restrictions.
public sealed class HoldingState
{
    public decimal Quantity { get; }
    public decimal? AverageCost { get; }
    public decimal? TotalCostBasis => Quantity == 0 ? 0m : Quantity * AverageCost;

    public HoldingState(decimal quantity, decimal? averageCost)
    {
        // Financial zero is valid even when decimal arithmetic retains a negative
        // zero sign bit (for example, subtracting an entire fractional position).
        if (quantity < 0) throw new ArgumentOutOfRangeException(nameof(quantity));
        if (averageCost is < 0) throw new ArgumentOutOfRangeException(nameof(averageCost));
        Quantity = quantity;
        AverageCost = averageCost;
    }
}
