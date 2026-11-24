using Sep2026.Application.Security;

namespace Sep2026.Application.Interfaces;

public interface IProviderCredentialStore
{
    // The generated reference is known before saving, so even an interrupted
    // credential write can be cleaned up. Save/Delete must be owner-scoped;
    // Delete is idempotent and cannot delete another owner's reference.
    Task SaveAsync(Guid ownerId, Guid reference, ProviderCredentials credentials,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid ownerId, Guid reference,
        CancellationToken cancellationToken = default);
}
