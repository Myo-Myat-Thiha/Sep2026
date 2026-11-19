using Sep2026.Domain;

namespace Sep2026.Application.DTOs;

public sealed record PortfolioSummaryDto(
    Guid PortfolioId,
    PortfolioType PortfolioType,
    CurrencyType CurrencyType,
    decimal CashBalance,
    PortfolioStatus Status,
    DateTimeOffset CreatedAt);

public sealed record PortfolioDetailsDto(
    Guid PortfolioId,
    Guid OwnerId,
    decimal CashBalance,
    string PortfolioType,
    string CurrencyType,
    string Status,
    DateTimeOffset CreatedAt,
    IReadOnlyList<HoldingDetailsDto> Holdings);

public sealed record HoldingDetailsDto(
    Guid HoldingId,
    Guid PortfolioId,
    Guid SecurityId,
    string TickerSymbol,
    string SecurityType,
    int ShareCount,
    decimal AveragePurchasePrice,
    MarketQuoteDto? LatestQuote);

public sealed record MarketQuoteDto(
    string TickerSymbol,
    decimal Price,
    string CurrencyType,
    DateTimeOffset QuoteAt);
