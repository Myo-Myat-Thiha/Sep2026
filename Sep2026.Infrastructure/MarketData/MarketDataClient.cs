using System.Net.Http.Json;
using System.Net.Http.Headers;
using Sep2026.Application.DTOs;
using Sep2026.Application.Interfaces;
using Sep2026.Domain;

namespace Sep2026.Infrastructure.MarketData
{
    // Alpaca US stock data. The application's single valuation price is the latest trade price.
    public sealed class MarketDataClient : IMarketDataProvider
    {
        private readonly HttpClient client;
        private readonly string apiKeyId;
        private readonly string apiSecretKey;
        private readonly string feed;
        
        public MarketDataClient(
            HttpClient httpClient,
            string apiKeyId,
            string apiSecretKey,
            string feed = "iex")
        {
            ArgumentNullException.ThrowIfNull(httpClient, nameof(httpClient));
            ArgumentException.ThrowIfNullOrWhiteSpace(apiKeyId, nameof(apiKeyId));
            ArgumentException.ThrowIfNullOrWhiteSpace(apiSecretKey, nameof(apiSecretKey));
            ArgumentException.ThrowIfNullOrWhiteSpace(feed, nameof(feed));

            this.client = httpClient;
            this.apiKeyId = apiKeyId;
            this.apiSecretKey = apiSecretKey;
            this.feed = feed.Trim().ToLowerInvariant();
            httpClient.DefaultRequestHeaders.Add("APCA-API-KEY-ID", apiKeyId);
            httpClient.DefaultRequestHeaders.Add("APCA-API-SECRET-KEY", apiSecretKey);
            httpClient.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
        }

        public async Task<IReadOnlyList<AssetDto>> GetAllAssetsAsync(
            string assetClass = "us_equity",
            CancellationToken cancellationToken = default)
        {
            //ArgumnetValidation
            ArgumentException.ThrowIfNullOrWhiteSpace(assetClass);

            string encodedAssetClass = Uri.EscapeDataString(assetClass.Trim().ToLowerInvariant());
            string assetStatus = "active";
            using var request = new HttpRequestMessage
            { 
                Method = HttpMethod.Get,
                RequestUri = new Uri($"https://api.alpaca.markets/v2/assets?status={assetStatus}&asset_class={encodedAssetClass}")
            };
            using var response = await client.SendAsync(request, cancellationToken);
            // Authentication, subscription and rate-limit failures must not become missing/zero prices.
            response.EnsureSuccessStatusCode();
            var assetsResponse = await response.Content.ReadFromJsonAsync<IReadOnlyList<AssetApiResponse>>(
                cancellationToken: cancellationToken);
            if (assetsResponse is null)
            {
                throw new InvalidDataException("Alpaca returned an empty response for all assets.");
            }
            return assetsResponse.Select(asset => new AssetDto(
                asset.AlpacaAssetId,
                asset.TickerSymbol,
                asset.Name,
                asset.AssetClass,
                asset.Exchange,
                asset.Status,
                asset.IsTradable)).ToArray();
        }   

        public async Task<MarketQuote?> GetMarketQuoteAsync(
            string tickerSymbol,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(tickerSymbol, nameof(tickerSymbol));

            string symbol = tickerSymbol.Trim().ToUpperInvariant();
            string encodedTickerSymbol = Uri.EscapeDataString(symbol);
            string encodedFeed = Uri.EscapeDataString(feed);

            using var request = new HttpRequestMessage
            {
                Method = HttpMethod.Get,
                RequestUri = new Uri($"https://data.alpaca.markets/v2/stocks/{encodedTickerSymbol}/trades/latest?feed={encodedFeed}&currency=USD")
            };
            using var response = await client.SendAsync(request, cancellationToken);
            // Authentication, subscription and rate-limit failures must not become missing/zero prices.
            response.EnsureSuccessStatusCode();

            var quoteResponse = await response.Content.ReadFromJsonAsync<QuoteApiResponse>(
                cancellationToken: cancellationToken);

            if (quoteResponse is null ||
                !string.Equals(quoteResponse.TickerSymbol, symbol, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Alpaca returned an empty response or an unexpected stock symbol.");
            }

            // A missing trade cannot be used to value a holding.
            if (quoteResponse.Trade is null)
            {
                return null;
            }

            var trade = quoteResponse.Trade;
            if (trade.Price is null || trade.Price.Value <= 0 ||
                trade.Timestamp is null || trade.Timestamp.Value == default)
            {
                throw new InvalidDataException("Alpaca returned an invalid trade price or timestamp.");
            }

            // USD is requested explicitly; Alpaca does not require a currency field in this response.
            return new MarketQuote(
                symbol,
                trade.Price.Value,
                "USD",
                trade.Timestamp.Value);
        }
    }
}
