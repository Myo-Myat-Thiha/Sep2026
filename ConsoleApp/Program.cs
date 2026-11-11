using ConsoleApp.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Sep2026.Application.Interfaces;
using Sep2026.Infrastructure.MarketData;

// Load appsettings, development user secrets, environment variables, and arguments.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

// Bind the Alpaca section to typed settings and validate before the app starts.
builder.Services.AddOptions<AlpacaOptions>()
    .Bind(builder.Configuration.GetSection(AlpacaOptions.SectionName))
    .Validate(options => !string.IsNullOrWhiteSpace(options.ApiKeyId),
        "Set Alpaca:ApiKeyId using user secrets or environment variables.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.ApiSecretKey),
        "Set Alpaca:ApiSecretKey using user secrets or environment variables.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.Feed),
        "Alpaca:Feed must not be blank.")
    .ValidateOnStart();

// Connect the Application interface to its Infrastructure implementation.
builder.Services.AddHttpClient<IMarketDataProvider, MarketDataClient>((httpClient, services) =>
{
    var options = services.GetRequiredService<IOptions<AlpacaOptions>>().Value;
    return new MarketDataClient(httpClient, options.ApiKeyId, options.ApiSecretKey, options.Feed);
});

using var host = builder.Build();
await host.StartAsync();

try
{
    // Resolve services here to start an application workflow. Resolving the provider
    // constructs the client but does not send an HTTP request.
    var marketDataProvider = host.Services.GetRequiredService<IMarketDataProvider>();
    Console.WriteLine("Startup complete. The market data provider is ready.");

    // Example for a future workflow:
    // var quote = await marketDataProvider.GetMarketQuoteAsync("AAPL");
}
finally
{
    // This is a one-shot console app: finish the workflow, then shut down.
    await host.StopAsync();
}
