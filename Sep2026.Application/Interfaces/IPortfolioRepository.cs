using Sep2026.Domain;

namespace Sep2026.Application.Interfaces;

public interface IPortfolioRepository
{
    Task<PortfolioModel?> GetByIdAsync(
        Guid portfolioId,
        CancellationToken cancellationToken = default);
}
