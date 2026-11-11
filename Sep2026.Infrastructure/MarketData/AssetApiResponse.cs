using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Sep2026.Infrastructure.MarketData
{
    public class AssetApiResponse
    {
        [JsonPropertyName("id")]
        public required Guid AlpacaAssetId { get; init; }

        [JsonPropertyName("symbol")]
        public required string TickerSymbol { get; init; }

        [JsonPropertyName("name")]
        public required string Name { get; init; }

        [JsonPropertyName("class")]
        public string? AssetClass { get; init; }

        [JsonPropertyName("exchange")]
        public string? Exchange { get; init; }

        [JsonPropertyName("status")]
        public string? Status { get; init; }

        [JsonPropertyName("tradable")]
        public required bool IsTradable { get; init; }
    }
}
