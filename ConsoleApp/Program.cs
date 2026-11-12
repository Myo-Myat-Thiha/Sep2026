using ConsoleApp.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Sep2026.Application.Interfaces;
using Sep2026.Infrastructure.MarketData;
using Sep2026.Infrastructure.Persistence;

// Load appsettings, development user secrets, environment variables, and arguments.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

// Register a scoped context using the connection string from configuration.
var connectionString = builder.Configuration.GetConnectionString("Gemino");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Set ConnectionStrings:Gemino in configuration, user secrets, or environment variables.");
}

builder.Services.AddDbContext<GeminoDbContext>(options =>
    options.UseSqlServer(connectionString));

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
    // A console app creates its own scope for a unit of work. Future repositories
    // and application services resolved here will share the scoped DbContext.
    await using var scope = host.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<GeminoDbContext>();

    // Resolve services here to start an application workflow. Resolving the provider
    // constructs the client but does not send an HTTP request.
    var marketDataProvider = scope.ServiceProvider.GetRequiredService<IMarketDataProvider>();
    Console.WriteLine("Startup complete. The database context and market data provider are configured.");

    // Resolving the context does not open a database connection or run queries.
    // Example for a future workflow:
    // var quote = await marketDataProvider.GetMarketQuoteAsync("AAPL");
}
finally
{
    // This is a one-shot console app: finish the workflow, then shut down.
    await host.StopAsync();
}
