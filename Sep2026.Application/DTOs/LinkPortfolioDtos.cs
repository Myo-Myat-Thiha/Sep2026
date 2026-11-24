using Sep2026.Domain;

namespace Sep2026.Application.DTOs;

public enum InvestmentProvider { Alpaca }
public enum ProviderEnvironment { Paper, Live }
public enum ExternalPositionSide { Long, Short }
public enum LinkPortfolioOutcome { Linked, AlreadyLinked }

public sealed record LinkPortfolioRequest(
    InvestmentProvider Provider,
    ProviderEnvironment Environment,
    string? DisplayName = null);

public sealed record ExternalPositionDto(
    string ExternalAssetId,
    string Symbol,
    string AssetClass,
    string Currency,
    ExternalPositionSide Side,
    decimal Quantity,
    decimal? AverageCost);

// A complete provider read, not an atomic provider transaction. Times describe
// the fetch interval; ProviderObservedAt is populated only when actually supplied.
public sealed record ExternalPortfolioSnapshotDto(
    string ExternalAccountId,
    string Currency,
    decimal CashBalance,
    IReadOnlyList<ExternalPositionDto> Positions,
    DateTimeOffset FetchStartedAt,
    DateTimeOffset FetchCompletedAt,
    bool IsComplete,
    string? ExternalAccountType = null,
    DateTimeOffset? ProviderObservedAt = null);

// Only accepted Domain financial state crosses the persistence boundary.
// Fetch/provider metadata stays separate from those financial invariants.
public sealed record InitialPortfolioImport(
    Guid PortfolioId,
    Guid ConnectionId,
    Guid OwnerId,
    InvestmentProvider Provider,
    ProviderEnvironment Environment,
    string? DisplayName,
    Guid CredentialReference,
    PortfolioSnapshot Snapshot,
    DateTimeOffset FetchStartedAt,
    DateTimeOffset FetchCompletedAt,
    string? ExternalAccountType,
    DateTimeOffset? ProviderObservedAt);

// Safe output: never includes a key, secret, or credential reference.
public sealed record LinkedPortfolioDto(
    Guid PortfolioId,
    Guid OwnerId,
    InvestmentProvider Provider,
    ProviderEnvironment Environment,
    string ExternalAccountId,
    string? DisplayName,
    DateTimeOffset LastSuccessfulSyncAt);

public sealed record LinkPortfolioResultDto(
    LinkPortfolioOutcome Outcome,
    LinkedPortfolioDto Portfolio);
