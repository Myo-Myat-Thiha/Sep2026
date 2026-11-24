using System;
using System.Collections.Generic;

namespace Sep2026.Infrastructure.Persistence.Entities;

public partial class ProviderConnection
{
    public Guid ConnectionId { get; set; }

    public Guid OwnerId { get; set; }

    public Guid PortfolioId { get; set; }

    public byte Provider { get; set; }

    public byte Environment { get; set; }

    public string ExternalAccountId { get; set; } = null!;

    public Guid CredentialReference { get; set; }

    public byte ConnectionStatus { get; set; }

    public DateTimeOffset LastAttemptAt { get; set; }

    public DateTimeOffset LastSuccessfulSyncAt { get; set; }

    public DateTimeOffset FetchStartedAt { get; set; }

    public DateTimeOffset FetchCompletedAt { get; set; }

    public DateTimeOffset? ProviderObservedAt { get; set; }

    public string? ExternalAccountType { get; set; }

    public string? LastError { get; set; }

    public long SyncGeneration { get; set; }

    public byte[] RowVersion { get; set; } = null!;

    public virtual Portfolio Portfolio { get; set; } = null!;
}
