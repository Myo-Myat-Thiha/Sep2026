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
        public async Task<(string? DisplayName, PortfolioConnectionDto? Connection)> GetConnectionAsync(
            Guid ownerId, Guid portfolioId, CancellationToken cancellationToken = default)
        {
            if (ownerId == Guid.Empty || portfolioId == Guid.Empty) throw new ArgumentException("Owner and portfolio IDs are required.");
            var row = await _context.Portfolios.AsNoTracking()
                .Where(portfolio => portfolio.OwnerId == ownerId && portfolio.PortfolioId == portfolioId)
                .Select(portfolio => new
                {
                    portfolio.DisplayName,
                    Connection = portfolio.ProviderConnections.Select(connection => new PortfolioConnectionDto(
                        (InvestmentProvider)connection.Provider, (ProviderEnvironment)connection.Environment,
                        (ProviderConnectionStatus)connection.ConnectionStatus, connection.LastSuccessfulSyncAt,
                        connection.FetchStartedAt, connection.FetchCompletedAt, connection.ProviderObservedAt)).SingleOrDefault()
                }).SingleOrDefaultAsync(cancellationToken);
            ValidateConnection(row?.Connection);
            return (row?.DisplayName, row?.Connection);
        }

        private static void ValidateConnection(PortfolioConnectionDto? connection)
        {
            if (connection is not null && (!Enum.IsDefined(connection.Provider) ||
                !Enum.IsDefined(connection.Environment) || !Enum.IsDefined(connection.Status)))
                throw new InvalidDataException("The saved portfolio has an unsupported provider connection.");
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
                    portfolio.CreatedAt,
                    portfolio.DisplayName,
                    Connection = portfolio.ProviderConnections.Select(connection => new PortfolioConnectionDto(
                        (InvestmentProvider)connection.Provider, (ProviderEnvironment)connection.Environment,
                        (ProviderConnectionStatus)connection.ConnectionStatus, connection.LastSuccessfulSyncAt,
                        connection.FetchStartedAt, connection.FetchCompletedAt, connection.ProviderObservedAt)).SingleOrDefault()
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

                ValidateConnection(portfolio.Connection);
                return new PortfolioSummaryDto(portfolio.PortfolioId, portfolioType,
                    currency, portfolio.CashBalance, status, portfolio.CreatedAt, portfolio.DisplayName, portfolio.Connection);
            }).ToArray();

            // An empty database result naturally produces an empty read-only collection.
            return Array.AsReadOnly(summaries);
        }
    }
}
