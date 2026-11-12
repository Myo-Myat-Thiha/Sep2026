using Sep2026.Domain;

namespace Sep2026.Application.Interfaces;

public interface IPortfolioRepository
{
    /// <summary>
    /// Loads a complete read-only snapshot, including all holdings and their securities.
    /// Returns null when the portfolio does not exist. Changes to the returned Domain
    /// objects are not tracked or saved by this repository.
    /// </summary>
    Task<PortfolioModel?> GetByIdAsync(
        Guid portfolioId,
        CancellationToken cancellationToken = default);
}
