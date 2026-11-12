namespace Sep2026.Application.DTOs;

public sealed record HoldingDetailsDto(
    Guid HoldingId,
    Guid PortfolioId,
    Guid SecurityId,
    string TickerSymbol,
    string SecurityType,
    int ShareCount,
    decimal AveragePurchasePrice,
    MarketQuoteDto? LatestQuote);
