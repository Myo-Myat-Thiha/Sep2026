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
            ArgumentException.ThrowIfNullOrWhiteSpace(currencyType);
            var currencyCode = currencyType.Trim().ToUpperInvariant();
            if (!Enum.GetNames<CurrencyType>().Contains(currencyCode, StringComparer.Ordinal))
                throw new ArgumentException("The quote currency is not supported.", nameof(currencyType));
            if (quoteAt == default)
                throw new ArgumentException("A quote timestamp is required.", nameof(quoteAt));

            this.TickerSymbol = tickerSymbol.Trim().ToUpperInvariant();
            this.Price = price;
            this.CurrencyType = currencyCode;
            this.QuoteAt = quoteAt;
        }
    }
}
