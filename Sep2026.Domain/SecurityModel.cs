using System;

namespace Sep2026.Domain
{
    public class SecurityModel
    {
        private readonly Guid securityId;


        public Guid SecurityId => securityId;
        public string TickerSymbol { get; private set; }
        // Null means that no market quote has been received yet; zero is a known price.
        public decimal? CurrentValue => LatestQuote?.Price;
        public MarketQuote? LatestQuote { get; private set; }
        public string SecurityType { get; private set; }
        // New securities start without a market quote.
        public SecurityModel(string tickerSymbol, string securityType = "Stock")
            : this(Guid.NewGuid(), tickerSymbol, securityType, null)
        {
        }

        //Restoring the Existing Security from the Database.
        private SecurityModel(Guid securityId, string tickerSymbol, string securityType, MarketQuote? latestQuote)
        {
            if (securityId == Guid.Empty)
                throw new ArgumentException("A security ID is required.", nameof(securityId));
            ArgumentException.ThrowIfNullOrWhiteSpace(securityType);
            var symbol = NormalizeTickerSymbol(tickerSymbol);
            ValidateQuoteSymbol(symbol, latestQuote);

            this.securityId = securityId;
            TickerSymbol = symbol;
            SecurityType = securityType;
            LatestQuote = latestQuote;
        }

        public static SecurityModel Restore(
            Guid securityId,
            string tickerSymbol,
            string securityType,
            MarketQuote? latestQuote = null)
            => new SecurityModel(securityId, tickerSymbol, securityType, latestQuote);

        public void UpdateMarketQuote(MarketQuote quote)
        {
            ArgumentNullException.ThrowIfNull(quote);
            ValidateQuoteSymbol(TickerSymbol, quote);
            LatestQuote = quote;
        }
        public void UpdateTickerSymbol(string newTickerSymbol)
        {
            var symbol = NormalizeTickerSymbol(newTickerSymbol);
            if (!string.Equals(TickerSymbol, symbol, StringComparison.Ordinal))
                LatestQuote = null;
            TickerSymbol = symbol;
        }

        public void UpdateSecurityType(string newSecurityType)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(newSecurityType);
            SecurityType = newSecurityType;
        }

        private static string NormalizeTickerSymbol(string tickerSymbol)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(tickerSymbol);
            return tickerSymbol.Trim().ToUpperInvariant();
        }

        private static void ValidateQuoteSymbol(string tickerSymbol, MarketQuote? quote)
        {
            if (quote is not null &&
                !string.Equals(tickerSymbol, quote.TickerSymbol, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("The quote must belong to this security.", nameof(quote));
        }
    }
}
