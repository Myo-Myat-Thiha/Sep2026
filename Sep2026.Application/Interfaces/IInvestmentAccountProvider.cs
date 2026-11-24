using Sep2026.Application.DTOs;
using Sep2026.Application.Security;

namespace Sep2026.Application.Interfaces;

public interface IInvestmentAccountProvider
{
    // Verify identity from the provider. A caller-entered account ID is not proof
    // of access. Implementations must isolate authorization per request/connection.
    Task<string> GetAccountIdAsync(
        InvestmentProvider provider, ProviderEnvironment environment,
        ProviderCredentials credentials, CancellationToken cancellationToken = default);

    // Re-read account and every positions page under the account lock. Mark
    // incomplete/error responses accordingly; never translate failures to an
    // empty account. Verify account identity throughout the read.
    Task<ExternalPortfolioSnapshotDto> ReadSnapshotAsync(
        InvestmentProvider provider, ProviderEnvironment environment,
        string externalAccountId, ProviderCredentials credentials,
        CancellationToken cancellationToken = default);
}
