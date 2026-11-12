using Sep2026.Domain;

namespace Sep2026.Application.DTOs;

public sealed record PortfolioSummaryDto(
    Guid PortfolioId,
    PortfolioType PortfolioType,
    CurrencyType CurrencyType,
    decimal CashBalance,
    PortfolioStatus Status,
    DateTimeOffset CreatedAt
    );
