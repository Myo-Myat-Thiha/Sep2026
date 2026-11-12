namespace Sep2026.Application.DTOs;

public sealed record MarketQuoteDto(
    string TickerSymbol,
    decimal Price,
    string CurrencyType,
    DateTimeOffset QuoteAt);
