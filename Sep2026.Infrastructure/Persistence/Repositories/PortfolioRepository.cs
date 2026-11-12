using Microsoft.EntityFrameworkCore;
using Sep2026.Application.Interfaces;
using Sep2026.Domain;
using Sep2026.Infrastructure.Persistence.Mappings;

namespace Sep2026.Infrastructure.Persistence.Repositories;

public sealed class PortfolioRepository : IPortfolioRepository
{
    private readonly GeminoDbContext _context;

    public PortfolioRepository(GeminoDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    public async Task<PortfolioModel?> GetByIdAsync(
        Guid portfolioId,
        CancellationToken cancellationToken = default)
    {
        if (portfolioId == Guid.Empty)
            throw new ArgumentException("A portfolio ID is required.", nameof(portfolioId));
        cancellationToken.ThrowIfCancellationRequested();

        // Load the full aggregate deliberately; an unloaded collection must never
        // be mistaken for a portfolio without holdings.
        var entity = await _context.Portfolios
            .AsNoTracking()
            .Include(portfolio => portfolio.Holdings)
                .ThenInclude(holding => holding.Security)
            .SingleOrDefaultAsync(portfolio => portfolio.PortfolioId == portfolioId,
                cancellationToken);

        return entity is null ? null : PortfolioMapper.ToDomain(entity);
    }
}
