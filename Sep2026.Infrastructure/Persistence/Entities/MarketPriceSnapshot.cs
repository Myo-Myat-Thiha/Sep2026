using System;
using System.Collections.Generic;

namespace Sep2026.Infrastructure.Persistence.Entities;

public partial class MarketPriceSnapshot
{
    public long SnapshotId { get; set; }

    public Guid SecurityId { get; set; }

    public decimal Price { get; set; }

    public string CurrencyType { get; set; } = null!;

    public DateTimeOffset MarketTimestamp { get; set; }

    public DateTimeOffset ReceivedAt { get; set; }

    public string Provider { get; set; } = null!;

    public string Feed { get; set; } = null!;

    public virtual Security Security { get; set; } = null!;
}
