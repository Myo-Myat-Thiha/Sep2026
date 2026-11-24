using Sep2026.Application.DTOs;

namespace Sep2026.Application.Interfaces
{
    public interface IPortfolioQueries
    {
        Task<(string? DisplayName, PortfolioConnectionDto? Connection)> GetConnectionAsync(
            Guid ownerId, Guid portfolioId, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<PortfolioSummaryDto>> GetByUserIdAsync(
            Guid userId,
            CancellationToken cancellationToken = default);
    }
}
