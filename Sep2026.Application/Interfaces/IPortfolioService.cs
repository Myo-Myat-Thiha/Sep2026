using Sep2026.Application.DTOs;

namespace Sep2026.Application.Interfaces;

public interface IPortfolioService
{
    Task<PortfolioDetailsDto?> GetByIdAsync(
        Guid portfolioId,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PortfolioSummaryDto>> GetByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}
