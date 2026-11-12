using Sep2026.Domain;
using DbSecurity = Sep2026.Infrastructure.Persistence.Entities.Security;

namespace Sep2026.Infrastructure.Persistence.Mappings;

internal static class SecurityMapper
{
    internal static SecurityModel ToDomain(DbSecurity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        if (entity.CurrentValue.HasValue != entity.QuoteAt.HasValue)
            throw new InvalidDataException(
                $"Security '{entity.SecurityId}' has incomplete cached quote data.");

        try
        {
            MarketQuote? quote = entity.CurrentValue.HasValue
                ? new MarketQuote(entity.TickerSymbol, entity.CurrentValue.Value,
                    entity.CurrencyType, entity.QuoteAt!.Value)
                : null;

            return SecurityModel.Restore(entity.SecurityId, entity.TickerSymbol,
                entity.SecurityType, quote);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException(
                $"Security '{entity.SecurityId}' contains invalid saved state.", exception);
        }
    }
}
