using Sep2026.Application.DTOs;

namespace Sep2026.Application.Interfaces;

public interface ILinkedPortfolioStore
{
    Task<bool> OwnerExistsAsync(Guid ownerId, CancellationToken cancellationToken = default);

    // Always owner-scoped, including during uncertain-commit recovery. Must be
    // usable after a failed create (no poisoned/pending transaction or tracking).
    Task<LinkedPortfolioDto?> FindAsync(Guid ownerId, InvestmentProvider provider,
        ProviderEnvironment environment, string externalAccountId,
        CancellationToken cancellationToken = default);

    // All future link, refresh, and disconnect paths must share this account key.
    // A lock is not a transaction; API calls run without an open SQL transaction.
    Task<IPortfolioLinkLock> AcquireLinkLockAsync(Guid ownerId, InvestmentProvider provider,
        ProviderEnvironment environment, string externalAccountId,
        CancellationToken cancellationToken = default);

    // Atomically save the initial portfolio, connection, mappings, holdings and
    // sync metadata. Validate owner, lock, uniqueness and concurrency at commit.
    // AlreadyLinked must not attach the new import's credential or preferences.
    // Throw PortfolioLinkNotCommittedException ONLY when rollback/no commit is
    // certain. Other failures (including cancellation) may mean commit is unknown.
    // Never throw cancellation after a confirmed successful commit.
    Task<LinkPortfolioResultDto> CreateAsync(InitialPortfolioImport import,
        IPortfolioLinkLock linkLock, CancellationToken cancellationToken = default);
}

public interface IPortfolioLinkLock : IAsyncDisposable
{
    // Disposal must release resources idempotently and must not mask a confirmed
    // commit with a lock-release/network exception. Concrete adapters handle
    // release diagnostics internally, including already-lost connections.
    // Cancel when ownership is lost. CreateAsync must also verify ownership at
    // commit; a cancellation check alone cannot prevent a last-instant lock loss.
    CancellationToken LostToken { get; }
}
