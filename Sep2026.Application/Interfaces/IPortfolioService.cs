using Sep2026.Application.DTOs;

namespace Sep2026.Application.Interfaces;

public interface IPortfolioService
{
    /// <summary>
    /// Returns saved portfolio details, including holdings and optional cached quotes.
    /// Returns null when the portfolio does not exist. Does not refresh prices or save data.
    /// </summary>
    Task<PortfolioDetailsDto?> GetByIdAsync(
        Guid portfolioId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the user's saved portfolio summaries, including active and archived portfolios.
    /// Returns an empty collection when none match. No detail graphs or prices are refreshed.
    /// </summary>
    Task<IReadOnlyList<PortfolioSummaryDto>> GetByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

}
