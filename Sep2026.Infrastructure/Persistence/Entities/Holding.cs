using System;
using System.Collections.Generic;

namespace Sep2026.Infrastructure.Persistence.Entities;

public partial class Holding
{
    public Guid HoldingId { get; set; }

    public Guid PortfolioId { get; set; }

    public Guid SecurityId { get; set; }

    public int ShareCount { get; set; }

    public decimal AveragePurchasePrice { get; set; }

    public byte[] RowVersion { get; set; } = null!;

    public virtual Portfolio Portfolio { get; set; } = null!;

    public virtual Security Security { get; set; } = null!;
}
