namespace Sep2026.Application.DTOs;

public sealed record PortfolioDetailsDto(
    Guid PortfolioId,
    Guid OwnerId,
    decimal CashBalance,
    string PortfolioType,
    string CurrencyType,
    string Status,
    DateTimeOffset CreatedAt,
    IReadOnlyList<HoldingDetailsDto> Holdings);
