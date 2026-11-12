using Sep2026.Application.Interfaces;
using Sep2026.Application.DTOs;
using Microsoft.EntityFrameworkCore;
using Sep2026.Domain;

namespace Sep2026.Infrastructure.Persistence.Queries
{
    public sealed class PortfolioQueries : IPortfolioQueries
    {
        private readonly GeminoDbContext _context;
        public PortfolioQueries(GeminoDbContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            _context = context;
        }
        public async Task<IReadOnlyList<PortfolioSummaryDto>> GetByUserIdAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            if (userId == Guid.Empty)
                throw new ArgumentException("A user ID is required.", nameof(userId));
            cancellationToken.ThrowIfCancellationRequested();

            // This part is translated into SQL: filter, sort, and select only
            // the summary columns. There is deliberately no status filter or Include.
            var portfolios = await _context.Portfolios
                .AsNoTracking()
                .Where(portfolio => portfolio.OwnerId == userId)
                .OrderByDescending(portfolio => portfolio.CreatedAt)
                .ThenBy(portfolio => portfolio.PortfolioId)
                .Select(portfolio => new
                {
                    portfolio.PortfolioId,
                    portfolio.PortfolioType,
                    portfolio.CurrencyType,
                    portfolio.CashBalance,
                    portfolio.Status,
                    portfolio.CreatedAt
                })
                .ToListAsync(cancellationToken);

            // Convert SQL byte/string values to concrete enums after the query.
            // Invalid stored values must not silently become a default enum value.
            var summaries = portfolios.Select(portfolio =>
            {
                var portfolioType = (PortfolioType)portfolio.PortfolioType;
                var status = (PortfolioStatus)portfolio.Status;
                var currencyCode = portfolio.CurrencyType?.Trim().ToUpperInvariant();

                if (!Enum.IsDefined(portfolioType) || !Enum.IsDefined(status) ||
                    !Enum.TryParse<CurrencyType>(currencyCode, out var currency) ||
                    !Enum.IsDefined(currency) ||
                    !string.Equals(currency.ToString(), currencyCode, StringComparison.Ordinal))
                    throw new InvalidDataException(
                        $"Portfolio '{portfolio.PortfolioId}' has an unsupported type, status, or currency.");

                return new PortfolioSummaryDto(portfolio.PortfolioId, portfolioType,
                    currency, portfolio.CashBalance, status, portfolio.CreatedAt);
            }).ToArray();

            // An empty database result naturally produces an empty read-only collection.
            return Array.AsReadOnly(summaries);
        }
    }
}
