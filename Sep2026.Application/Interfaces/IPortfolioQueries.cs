using Sep2026.Application.DTOs;

namespace Sep2026.Application.Interfaces
{
    public interface IPortfolioQueries
    {
        Task<IReadOnlyList<PortfolioSummaryDto>> GetByUserIdAsync(
            Guid userId,
            CancellationToken cancellationToken = default);
    }
}
