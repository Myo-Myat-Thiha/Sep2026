using System.Globalization;
using System.Net;
using System.Text.Json;
using Sep2026.Application.DTOs;
using Sep2026.Application.Exceptions;
using Sep2026.Application.Interfaces;
using Sep2026.Application.Security;

namespace Sep2026.Infrastructure.AccountProviders;

public sealed class AlpacaAccountClient : IInvestmentAccountProvider
{
    private readonly HttpClient client;
    private readonly TimeProvider clock;

    public AlpacaAccountClient(HttpClient client, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        this.client = client;
        this.clock = clock ?? TimeProvider.System;
    }

    public async Task<string> GetAccountIdAsync(InvestmentProvider provider, ProviderEnvironment environment,
        ProviderCredentials credentials, CancellationToken cancellationToken = default)
    {
        ValidateAccess(provider, environment, credentials);
        using var account = await ReadAsync("account", credentials, cancellationToken);
        return RequiredText(account.RootElement, "id");
    }

    public async Task<ExternalPortfolioSnapshotDto> ReadSnapshotAsync(InvestmentProvider provider,
        ProviderEnvironment environment, string externalAccountId, ProviderCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        ValidateAccess(provider, environment, credentials);
        ArgumentException.ThrowIfNullOrWhiteSpace(externalAccountId);
        var started = clock.GetUtcNow();
        using var account = await ReadAsync("account", credentials, cancellationToken);
        var root = account.RootElement;
        if (!string.Equals(RequiredText(root, "id"), externalAccountId, StringComparison.Ordinal))
            throw InvalidResponse();
        var currency = RequiredText(root, "currency");
        var cash = RequiredNumber(root, "cash");
        // This endpoint returns the complete open-positions array (no page token).
        using var response = await ReadAsync("positions", credentials, cancellationToken);
        if (response.RootElement.ValueKind != JsonValueKind.Array) throw InvalidResponse();
        var positions = new List<ExternalPositionDto>();
        foreach (var position in response.RootElement.EnumerateArray())
        {
            var side = RequiredText(position, "side") switch
            {
                "long" => ExternalPositionSide.Long,
                "short" => ExternalPositionSide.Short,
                _ => throw InvalidResponse()
            };
            var positionCurrency = OptionalText(position, "currency") ?? currency;
            positions.Add(new ExternalPositionDto(RequiredText(position, "asset_id"),
                RequiredText(position, "symbol"), RequiredText(position, "asset_class"),
                positionCurrency, side, RequiredNumber(position, "qty"), OptionalNumber(position, "avg_entry_price")));
        }
        return new ExternalPortfolioSnapshotDto(externalAccountId, currency, cash, positions.AsReadOnly(),
            started, clock.GetUtcNow(), true, OptionalText(root, "account_type"));
    }

    private async Task<JsonDocument> ReadAsync(string resource, ProviderCredentials credentials, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        var requestToken = deadline.Token;
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://paper-api.alpaca.markets/v2/{resource}");
        // Headers belong to this request, never to shared HttpClient defaults.
        request.Headers.Add("APCA-API-KEY-ID", credentials.KeyId);
        request.Headers.Add("APCA-API-SECRET-KEY", credentials.SecretKey);
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, requestToken);
            if (!response.IsSuccessStatusCode)
                throw new InvestmentAccountReadException(response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "Alpaca rejected access. Check the paper account API key and secret.",
                    HttpStatusCode.TooManyRequests => "Alpaca's request limit was reached. Wait before retrying.",
                    _ => "Alpaca could not return the account data. Try again later."
                });
            await using var stream = await response.Content.ReadAsStreamAsync(requestToken);
            return await JsonDocument.ParseAsync(stream, cancellationToken: requestToken);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { throw new InvestmentAccountReadException("The Alpaca account request timed out."); }
        catch (HttpRequestException) { throw new InvestmentAccountReadException("Could not connect to Alpaca. Check the network and retry."); }
        catch (JsonException) { throw InvalidResponse(); }
        catch (IOException) { throw new InvestmentAccountReadException("The Alpaca response was interrupted. Retry the account read."); }
    }

    private static void ValidateAccess(InvestmentProvider provider, ProviderEnvironment environment, ProviderCredentials credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        if (provider != InvestmentProvider.Alpaca || environment != ProviderEnvironment.Paper)
            throw new NotSupportedException("Only Alpaca paper accounts are enabled.");
        if (credentials.KeyId.Any(char.IsControl) || credentials.SecretKey.Any(char.IsControl))
            throw new ArgumentException("API credentials must not contain control characters.");
    }

    private static string RequiredText(JsonElement element, string field)
        => OptionalText(element, field) is { Length: > 0 } value && !string.IsNullOrWhiteSpace(value) ? value : throw InvalidResponse();

    private static string? OptionalText(JsonElement element, string field)
    {
        if (element.ValueKind != JsonValueKind.Object) throw InvalidResponse();
        if (!element.TryGetProperty(field, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : throw InvalidResponse();
    }

    private static decimal RequiredNumber(JsonElement element, string field)
        => OptionalNumber(element, field) ?? throw InvalidResponse();

    private static decimal? OptionalNumber(JsonElement element, string field)
    {
        if (element.ValueKind != JsonValueKind.Object) throw InvalidResponse();
        if (!element.TryGetProperty(field, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        var text = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => throw InvalidResponse()
        };
        if (text is null || text.Length > 256 || !decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            throw InvalidResponse();
        // Decimal.TryParse can round an over-precise number. Preserve the exact
        // source value or reject it before the financial import policy sees it.
        if (CanonicalNumber(text) != CanonicalNumber(parsed.ToString("G29", CultureInfo.InvariantCulture)))
            throw InvalidResponse();
        return parsed;
    }

    private static (string Digits, long Power, bool Negative) CanonicalNumber(string text)
    {
        text = text.Trim();
        var negative = text.StartsWith('-');
        if (text.StartsWith('-') || text.StartsWith('+')) text = text[1..];
        var exponentIndex = text.IndexOfAny(['e', 'E']);
        var exponent = 0;
        if (exponentIndex >= 0)
        {
            if (!int.TryParse(text[(exponentIndex + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out exponent))
                throw InvalidResponse();
            text = text[..exponentIndex];
        }
        var decimalIndex = text.IndexOf('.');
        var fractionLength = decimalIndex < 0 ? 0 : text.Length - decimalIndex - 1;
        var digits = text.Replace(".", "", StringComparison.Ordinal).TrimStart('0');
        if (digits.Length == 0) return ("0", 0, false);
        var significant = digits.TrimEnd('0');
        return (significant, (long)exponent - fractionLength + digits.Length - significant.Length, negative);
    }

    private static InvestmentAccountReadException InvalidResponse()
        => new("Alpaca returned incomplete or invalid account data. No portfolio was imported.");
}
