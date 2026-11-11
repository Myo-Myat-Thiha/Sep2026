namespace Sep2026.Application.DTOs;

public sealed record AssetDto(
    Guid AssetId,
    string TickerSymbol,
    string Name,
    string? AssetClass,
    string? Exchange,
    string? Status,
    bool IsTradable);
