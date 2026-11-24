using System.Net;
using Sep2026.Application.DTOs;
using Sep2026.Application.Exceptions;
using Sep2026.Application.Security;
using Sep2026.Infrastructure.AccountProviders;
using Xunit;

namespace Sep2026.Infrastructure.Tests;

public sealed class AlpacaAccountClientTests
{
    private const string Account = """{"id":"account","currency":"USD","cash":"123.45678901"}""";
    private const string Positions = """[{"asset_id":"asset","symbol":"AAPL","asset_class":"us_equity","side":"long","qty":"1.123456789","avg_entry_price":null}]""";

    [Fact]
    public async Task ReadsPaperIdentityAndCompleteFractionalStateWithRequestScopedCredentials()
    {
        var handler = new Handler(request => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(request.RequestUri!.AbsolutePath.EndsWith("positions") ? Positions : Account)
        });
        using var http = new HttpClient(handler);
        var provider = new AlpacaAccountClient(http);
        var credentials = new ProviderCredentials("first", "secret");
        Assert.Equal("account", await provider.GetAccountIdAsync(InvestmentProvider.Alpaca, ProviderEnvironment.Paper, credentials));
        var snapshot = await provider.ReadSnapshotAsync(InvestmentProvider.Alpaca, ProviderEnvironment.Paper, "account", credentials);
        Assert.Equal(123.45678901m, snapshot.CashBalance);
        Assert.Equal(1.123456789m, Assert.Single(snapshot.Positions).Quantity);
        Assert.Null(snapshot.Positions[0].AverageCost);
        Assert.True(snapshot.IsComplete);
        Assert.True(snapshot.FetchCompletedAt >= snapshot.FetchStartedAt);
        Assert.Null(snapshot.ProviderObservedAt);
        await provider.GetAccountIdAsync(InvestmentProvider.Alpaca, ProviderEnvironment.Paper, new("second", "other"));
        Assert.Equal(new[] { "first", "first", "first", "second" }, handler.Keys);
        Assert.Empty(http.DefaultRequestHeaders);
        Assert.All(handler.Hosts, host => Assert.Equal("paper-api.alpaca.markets", host));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[{\"asset_id\":\"asset\"}]")]
    [InlineData("{}")]
    [InlineData("not json")]
    public async Task OnlyAnActualCompleteEmptyArrayCanRepresentNoPositions(string json)
    {
        using var http = new HttpClient(new Handler(request => new(HttpStatusCode.OK)
        { Content = new StringContent(request.RequestUri!.AbsolutePath.EndsWith("positions") ? json : Account) }));
        var read = new AlpacaAccountClient(http).ReadSnapshotAsync(InvestmentProvider.Alpaca, ProviderEnvironment.Paper, "account", new("id", "secret"));
        if (json == "[]") Assert.Empty((await read).Positions);
        else await Assert.ThrowsAsync<InvestmentAccountReadException>(() => read);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(302)]
    public async Task FailuresDoNotBecomeEmptyAccountsOrExposeResponseBodies(int status)
    {
        using var http = new HttpClient(new Handler(_ => new((HttpStatusCode)status)
        { Content = new StringContent("sensitive-provider-response") }));
        var failure = await Assert.ThrowsAsync<InvestmentAccountReadException>(() => new AlpacaAccountClient(http)
            .GetAccountIdAsync(InvestmentProvider.Alpaca, ProviderEnvironment.Paper, new("id", "secret")));
        Assert.DoesNotContain("sensitive-provider-response", failure.ToString());
        Assert.Null(failure.InnerException);
    }

    [Fact]
    public async Task WrongAccountCannotBeImported()
    {
        using var http = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new StringContent(Account) }));
        await Assert.ThrowsAsync<InvestmentAccountReadException>(() => new AlpacaAccountClient(http)
            .ReadSnapshotAsync(InvestmentProvider.Alpaca, ProviderEnvironment.Paper, "other", new("id", "secret")));
    }

    [Theory]
    [InlineData("not-a-number")]
    [InlineData("0.12345678900000000000000000000000001")]
    [InlineData("79228162514264337593543950336")]
    public async Task MalformedOrRoundedMoneyIsRejectedRatherThanSilentlyChanged(string cash)
    {
        using var http = new HttpClient(new Handler(request => new(HttpStatusCode.OK)
        { Content = new StringContent(request.RequestUri!.AbsolutePath.EndsWith("positions") ? "[]" : Account.Replace("123.45678901", cash, StringComparison.Ordinal)) }));
        await Assert.ThrowsAsync<InvestmentAccountReadException>(() => new AlpacaAccountClient(http)
            .ReadSnapshotAsync(InvestmentProvider.Alpaca, ProviderEnvironment.Paper, "account", new("id", "secret")));
    }

    [Theory]
    [InlineData("1e-3", "0.001")]
    [InlineData("123.45000", "123.45")]
    public async Task ExactExponentAndTrailingZeroValuesArePreserved(string cash, string expected)
    {
        using var http = new HttpClient(new Handler(request => new(HttpStatusCode.OK)
        { Content = new StringContent(request.RequestUri!.AbsolutePath.EndsWith("positions") ? "[]" : Account.Replace("123.45678901", cash, StringComparison.Ordinal)) }));
        var snapshot = await new AlpacaAccountClient(http).ReadSnapshotAsync(InvestmentProvider.Alpaca, ProviderEnvironment.Paper, "account", new("id", "secret"));
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), snapshot.CashBalance);
    }

    [Fact]
    public async Task LiveAccessAndHeaderInjectionAreRejectedBeforeHttp()
    {
        var handler = new Handler(_ => throw new InvalidOperationException("Must not send"));
        using var http = new HttpClient(handler);
        var provider = new AlpacaAccountClient(http);
        await Assert.ThrowsAsync<NotSupportedException>(() => provider.GetAccountIdAsync(InvestmentProvider.Alpaca, ProviderEnvironment.Live, new("id", "secret")));
        await Assert.ThrowsAsync<ArgumentException>(() => provider.GetAccountIdAsync(InvestmentProvider.Alpaca, ProviderEnvironment.Paper, new("id", "secret\r\ninjected")));
        Assert.Empty(handler.Keys);
    }

    [Fact]
    public async Task CancellationStopsTheRequest()
    {
        using var http = new HttpClient(new Handler(_ => new(HttpStatusCode.OK)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new AlpacaAccountClient(http)
            .GetAccountIdAsync(InvestmentProvider.Alpaca, ProviderEnvironment.Paper, new("id", "secret"), new CancellationToken(true)));
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        public List<string> Keys { get; } = new();
        public List<string> Hosts { get; } = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Keys.Add(Assert.Single(request.Headers.GetValues("APCA-API-KEY-ID")));
            Hosts.Add(request.RequestUri!.Host);
            return Task.FromResult(send(request));
        }
    }
}
