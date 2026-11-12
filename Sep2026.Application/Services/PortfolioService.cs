using Sep2026.Application.DTOs;
using Sep2026.Application.Interfaces;

namespace Sep2026.Application.Services;

public sealed class PortfolioService : IPortfolioService
{
    private readonly IPortfolioRepository repository;
    private readonly IPortfolioQueries queries;

    public PortfolioService(IPortfolioRepository repository, IPortfolioQueries queries)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(queries);
        this.repository = repository;
        this.queries = queries;
    }

    public async Task<PortfolioDetailsDto?> GetByIdAsync(
        Guid portfolioId,
        CancellationToken cancellationToken = default)
    {
        if (portfolioId == Guid.Empty)
            throw new ArgumentException("A portfolio ID is required.", nameof(portfolioId));
        cancellationToken.ThrowIfCancellationRequested();

        var portfolio = await repository.GetByIdAsync(portfolioId, cancellationToken);
        if (portfolio is null) return null;

        // These are transport records, separate from the mutable Domain objects.
        var holdings = portfolio.Holdings.Select(holding =>
        {
            var quote = holding.Security.LatestQuote;
            return new HoldingDetailsDto(
                holding.HoldingId,
                holding.PortfolioId,
                holding.SecurityId,
                holding.TickerSymbol,
                holding.Security.SecurityType,
                holding.ShareCount,
                holding.AveragePurchasePrice,
                quote is null ? null : new MarketQuoteDto(
                    quote.TickerSymbol, quote.Price, quote.CurrencyType, quote.QuoteAt));
        }).ToArray();

        return new PortfolioDetailsDto(
            portfolio.PortfolioId,
            portfolio.OwnerId,
            portfolio.CashBalance,
            portfolio.PortfolioType.ToString(),
            portfolio.CurrencyType.ToString(),
            portfolio.Status.ToString(),
            portfolio.CreatedAt,
            Array.AsReadOnly(holdings));
    }

    public async Task<IReadOnlyList<PortfolioSummaryDto>> GetByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        //Argument Validation
        if (userId == Guid.Empty)
            throw new ArgumentException("A user ID is required.", nameof(userId));
        cancellationToken.ThrowIfCancellationRequested();
        return await queries.GetByUserIdAsync(userId, cancellationToken);
    }
}
