using Sep2026.Application.DTOs;

namespace Sep2026.Application.Interfaces
{
    public interface IPortfolioQueries
    {
        /// <summary>
        /// Lists all saved portfolios owned by the user, including archived ones.
        /// Returns an empty collection when none match. Results are ordered by
        /// creation time descending, then portfolio ID ascending, without loading holdings.
        /// </summary>
        Task<IReadOnlyList<PortfolioSummaryDto>> GetByUserIdAsync(
            Guid userId,
            CancellationToken cancellationToken = default);
    }
}
