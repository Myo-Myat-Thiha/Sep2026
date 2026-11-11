namespace Sep2026.Domain
{
    public sealed record MarketQuote
    {
        public string TickerSymbol { get; }
        public decimal Price { get; }
        public string CurrencyType { get; }
        public DateTimeOffset QuoteAt { get; }
        public MarketQuote(
            string tickerSymbol, 
            decimal price,
            string currencyType,
            DateTimeOffset quoteAt)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(tickerSymbol, nameof(tickerSymbol));
            if (price < 0) throw new ArgumentOutOfRangeException(nameof(price), "Price cannot be negative.");
            this.TickerSymbol = tickerSymbol;
            this.Price = price;
            this.CurrencyType = currencyType;
            this.QuoteAt = quoteAt;
        }
    }
}
