using Sep2026.Domain;
using DbHolding = Sep2026.Infrastructure.Persistence.Entities.Holding;

namespace Sep2026.Infrastructure.Persistence.Mappings;

internal static class HoldingMapper
{
    internal static HoldingModel ToDomain(DbHolding entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (entity.Security is null)
            throw new InvalidDataException(
                $"Holding '{entity.HoldingId}' requires its Security relationship to be loaded.");
        if (entity.Security.SecurityId != entity.SecurityId)
            throw new InvalidDataException(
                $"Holding '{entity.HoldingId}' has an inconsistent Security relationship.");

        var security = SecurityMapper.ToDomain(entity.Security);
        try
        {
            return HoldingModel.Restore(entity.HoldingId, security, entity.PortfolioId,
                entity.ShareCount, entity.AveragePurchasePrice);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException(
                $"Holding '{entity.HoldingId}' contains invalid saved state.", exception);
        }
    }
}
