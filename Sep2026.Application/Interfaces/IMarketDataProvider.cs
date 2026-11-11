using System;
using Sep2026.Application.DTOs;
using Sep2026.Domain;

namespace Sep2026.Application.Interfaces
{
    public interface IMarketDataProvider
    {
        Task<IReadOnlyList<AssetDto>> GetAllAssetsAsync(string assetClass = "us_equity", CancellationToken cancellationToken = default);
        Task<MarketQuote?> GetMarketQuoteAsync(string tickerSymbol, CancellationToken cancellationToken = default);
    }
}
