using System;
using System.Collections.Generic;

namespace Sep2026.Infrastructure.Persistence.Entities;

public partial class Security
{
    public Guid SecurityId { get; set; }

    public string TickerSymbol { get; set; } = null!;

    public string SecurityType { get; set; } = null!;

    public decimal? CurrentValue { get; set; }

    public string CurrencyType { get; set; } = null!;

    public DateTimeOffset? QuoteAt { get; set; }

    public virtual ICollection<Holding> Holdings { get; set; } = new List<Holding>();

    public virtual ICollection<MarketPriceSnapshot> MarketPriceSnapshots { get; set; } = new List<MarketPriceSnapshot>();
}
