using System;
using System.Collections.Generic;

namespace Sep2026.Infrastructure.Persistence.Entities;

public partial class ProviderSecurityMapping
{
    public Guid MappingId { get; set; }

    public byte Provider { get; set; }

    public byte Environment { get; set; }

    public string ExternalAssetId { get; set; } = null!;

    public Guid SecurityId { get; set; }

    public virtual Security Security { get; set; } = null!;
}
