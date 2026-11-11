using System;
using System.Collections.Generic;

namespace Sep2026.Infrastructure.Persistence.Entities;

public partial class Portfolio
{
    public Guid PortfolioId { get; set; }

    public Guid OwnerId { get; set; }

    public decimal CashBalance { get; set; }

    public byte PortfolioType { get; set; }

    public string CurrencyType { get; set; } = null!;

    public byte Status { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public byte[] RowVersion { get; set; } = null!;

    public virtual ICollection<Holding> Holdings { get; set; } = new List<Holding>();

    public virtual User Owner { get; set; } = null!;
}
