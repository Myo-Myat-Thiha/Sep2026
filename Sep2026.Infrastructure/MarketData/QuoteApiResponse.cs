using System.Text.Json.Serialization;

namespace Sep2026.Infrastructure.MarketData
{
    public sealed record QuoteApiResponse
    {
        [JsonRequired]
        [JsonPropertyName("symbol")]
        public required string TickerSymbol { get; init; }

        [JsonPropertyName("trade")]
        public TradeApiResponse? Trade { get; init; }
    }

    public sealed record TradeApiResponse
    {
        [JsonPropertyName("p")]
        public decimal? Price { get; init; }

        [JsonPropertyName("t")]
        public DateTimeOffset? Timestamp { get; init; }
    }
}
