using Sep2026.Domain;
using DbPortfolio = Sep2026.Infrastructure.Persistence.Entities.Portfolio;

namespace Sep2026.Infrastructure.Persistence.Mappings;

internal static class PortfolioMapper
{
    // The caller must load all holdings and each holding's security before mapping.
    internal static PortfolioModel ToDomain(DbPortfolio entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (entity.Holdings is null)
            throw new InvalidDataException(
                $"Portfolio '{entity.PortfolioId}' has no loaded Holdings collection.");

        var currencyCode = entity.CurrencyType?.Trim().ToUpperInvariant();
        if (!Enum.TryParse<CurrencyType>(currencyCode, out var currency) ||
            !Enum.IsDefined(currency) ||
            !string.Equals(currency.ToString(), currencyCode, StringComparison.Ordinal))
            throw new InvalidDataException(
                $"Portfolio '{entity.PortfolioId}' has an unsupported currency code.");

        try
        {
            var holdings = entity.Holdings.Select(HoldingMapper.ToDomain).ToArray();
            return PortfolioModel.Restore(entity.PortfolioId, entity.CashBalance,
                holdings, entity.OwnerId, (PortfolioType)entity.PortfolioType,
                currency, entity.CreatedAt, (PortfolioStatus)entity.Status);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException(
                $"Portfolio '{entity.PortfolioId}' contains invalid saved state.", exception);
        }
    }
}
