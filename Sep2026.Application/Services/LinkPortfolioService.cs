using Sep2026.Application.DTOs;
using Sep2026.Application.Exceptions;
using Sep2026.Application.Interfaces;
using Sep2026.Application.Policies;
using Sep2026.Application.Security;

namespace Sep2026.Application.Services;

public sealed class LinkPortfolioService
{
    private static readonly TimeSpan RecoveryTimeout = TimeSpan.FromSeconds(5);
    private readonly IInvestmentAccountProvider provider;
    private readonly ILinkedPortfolioStore store;
    private readonly IProviderCredentialStore credentials;
    private readonly TimeProvider clock;
    private readonly PortfolioImportPolicy importPolicy;

    public LinkPortfolioService(
        IInvestmentAccountProvider provider, 
        ILinkedPortfolioStore store,
        IProviderCredentialStore credentials, 
        TimeProvider? clock = null,
        PortfolioImportPolicy? importPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(credentials);
        this.provider = provider;
        this.store = store;
        this.credentials = credentials;
        this.clock = clock ?? TimeProvider.System;
        this.importPolicy = importPolicy ?? new PortfolioImportPolicy();
    }

    // ownerId must come from the trusted signed-in session, not arbitrary user input.
    // Infrastructure adapters and console registration are a separate next slice.

    public async Task<LinkPortfolioResultDto> LinkAsync(
        Guid ownerId,
        LinkPortfolioRequest request,
        ProviderCredentials authorization, 
        CancellationToken cancellationToken = default)
    {
        //Argument Validation
        if (ownerId == Guid.Empty) throw new ArgumentException("An owner ID is required.", nameof(ownerId));
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(authorization);
        cancellationToken.ThrowIfCancellationRequested();
        request = importPolicy.ValidateRequest(request);
        if (!await store.OwnerExistsAsync(ownerId, cancellationToken))
            throw new KeyNotFoundException("The local owner profile was not found.");

        cancellationToken.ThrowIfCancellationRequested();
        var accountId = await provider.GetAccountIdAsync(request.Provider, request.Environment, authorization, cancellationToken);
        importPolicy.ValidateAccountId(accountId);
        var existing = await FindAsync(ownerId, request, accountId, cancellationToken);
        if (existing is not null) return new(LinkPortfolioOutcome.AlreadyLinked, existing);

        await using var linkLock = await store.AcquireLinkLockAsync(ownerId, request.Provider,
            request.Environment, accountId, cancellationToken);
        using var work = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, linkLock.LostToken);
        var token = work.Token;
        token.ThrowIfCancellationRequested();
        existing = await FindAsync(ownerId, request, accountId, token);
        if (existing is not null) return new(LinkPortfolioOutcome.AlreadyLinked, existing);

        var snapshot = await provider.ReadSnapshotAsync(request.Provider, request.Environment, accountId, authorization, token);
        var acceptedSnapshot = importPolicy.AcceptSnapshot(snapshot, accountId, clock.GetUtcNow());
        token.ThrowIfCancellationRequested();
        var credentialReference = Guid.NewGuid();
        var import = new InitialPortfolioImport(Guid.NewGuid(), Guid.NewGuid(), ownerId,
            request.Provider, request.Environment, request.DisplayName, credentialReference, acceptedSnapshot,
            snapshot.FetchStartedAt, snapshot.FetchCompletedAt, snapshot.ExternalAccountType, snapshot.ProviderObservedAt);
        var savingImport = false;
        LinkPortfolioResultDto result;
        try
        {
            await credentials.SaveAsync(ownerId, credentialReference, authorization, token);
            token.ThrowIfCancellationRequested();
            savingImport = true;
            result = await store.CreateAsync(import, linkLock, token);
            ValidateResult(result, ownerId, request, accountId, import.PortfolioId);
        }
        catch (Exception failure)
        {
            if (!savingImport || failure is PortfolioLinkNotCommittedException)
            {
                await CleanupAsync(ownerId, credentialReference, failure);
                throw;
            }

            // A timeout/cancelled response does not prove rollback. Use a bounded
            // independent token, even if the caller cancelled or the lock was lost.
            try
            {
                using var recovery = new CancellationTokenSource(RecoveryTimeout);
                existing = await FindAsync(ownerId, request, accountId, recovery.Token);
            }
            catch (Exception recoveryFailure)
            {
                throw new PortfolioLinkCommitUnknownException(import.PortfolioId,
                    new AggregateException(failure, recoveryFailure));
            }
            if (existing is null)
                throw new PortfolioLinkCommitUnknownException(import.PortfolioId, failure);
            result = new(existing.PortfolioId == import.PortfolioId
                ? LinkPortfolioOutcome.Linked : LinkPortfolioOutcome.AlreadyLinked, existing);
        }

        // AlreadyLinked guarantees the store did not attach our new reference.
        // Never delete credentials potentially used by a confirmed/unknown commit.
        if (result.Outcome == LinkPortfolioOutcome.AlreadyLinked)
            await CleanupAsync(ownerId, credentialReference);
        return result; // No post-commit cancellation check.
    }


    private async Task<LinkedPortfolioDto?> FindAsync(Guid ownerId, LinkPortfolioRequest request,
        string accountId, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var found = await store.FindAsync(ownerId, request.Provider, request.Environment, accountId, token);
        token.ThrowIfCancellationRequested();
        if (found is not null) ValidatePortfolio(found, ownerId, request, accountId);
        return found;
    }

    private static void ValidateResult(
        LinkPortfolioResultDto result,
        Guid ownerId,
        LinkPortfolioRequest request, 
        string accountId, 
        Guid attemptedPortfolioId)
    {
        if (result is null || !Enum.IsDefined(result.Outcome) || result.Portfolio is null)
            throw new InvalidDataException("The portfolio store returned an invalid link result.");

        ValidatePortfolio(result.Portfolio, ownerId, request, accountId);

        if (result.Outcome == LinkPortfolioOutcome.Linked && result.Portfolio.PortfolioId != attemptedPortfolioId)
            throw new InvalidDataException("The portfolio store returned a different import identity.");

        if (result.Outcome == LinkPortfolioOutcome.AlreadyLinked && result.Portfolio.PortfolioId == attemptedPortfolioId)
            throw new InvalidDataException("A new import cannot be reported as a pre-existing portfolio.");
    }

    private static void ValidatePortfolio(
        LinkedPortfolioDto portfolio, 
        Guid ownerId,
        LinkPortfolioRequest request, 
        string accountId)
    {
        if (portfolio.PortfolioId == Guid.Empty || portfolio.OwnerId != ownerId ||
            portfolio.Provider != request.Provider || portfolio.Environment != request.Environment ||
            !string.Equals(portfolio.ExternalAccountId, accountId, StringComparison.Ordinal) ||
            portfolio.LastSuccessfulSyncAt == default)
            throw new InvalidDataException("The saved portfolio does not match the authorized owner and external account.");
    }

    private async Task CleanupAsync(
        Guid ownerId, 
        Guid reference, 
        Exception? originalFailure = null)
    {
        using var cleanup = new CancellationTokenSource(RecoveryTimeout);

        try { await credentials.DeleteAsync(ownerId, reference, cleanup.Token); }
        catch (Exception cleanupFailure)
        {
            if (originalFailure is not null)
                throw new AggregateException("Portfolio linking failed and credential cleanup could not be confirmed.",
                    originalFailure, cleanupFailure);
            throw;
        }
    }
}
