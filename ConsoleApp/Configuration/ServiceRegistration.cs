using ConsoleApp.Presentation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sep2026.Application.Interfaces;
using Sep2026.Application.Services;
using Sep2026.Application.Policies;
using Sep2026.Infrastructure.AccountProviders;
using Sep2026.Infrastructure.CredentialStorage;
using Sep2026.Infrastructure.MarketData;
using Sep2026.Infrastructure.Persistence;
using Sep2026.Infrastructure.Persistence.Queries;
using Sep2026.Infrastructure.Persistence.Repositories;
using Sep2026.Infrastructure.Persistence.Stores;

namespace ConsoleApp.Configuration;

internal static class ServiceRegistration
{
    internal static IServiceCollection AddGeminoConsole(
        this IServiceCollection services,
        IConfiguration configuration,
        TextReader input,
        TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);

        var connectionString = configuration.GetConnectionString("Gemino");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Set ConnectionStrings:Gemino in configuration.");

        services.AddDbContext<GeminoDbContext>(options => options.UseSqlServer(connectionString));
        services.AddScoped<IPortfolioRepository, PortfolioRepository>();
        services.AddScoped<IPortfolioQueries, PortfolioQueries>();
        services.AddScoped<IPortfolioService, PortfolioService>();
        services.AddScoped<IUserStore, SqlServerUserStore>();
        services.AddScoped<UserService>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<PortfolioImportPolicy>();
        services.AddScoped<ILinkedPortfolioStore, SqlServerLinkedPortfolioStore>();
        services.AddSingleton<IProviderCredentialStore, WindowsProviderCredentialStore>();
        services.AddScoped<LinkPortfolioService>();
        services.AddHttpClient<IInvestmentAccountProvider, AlpacaAccountClient>(client =>
            client.Timeout = TimeSpan.FromSeconds(30))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
            .RedactLoggedHeaders(_ => true);

        // Saved-data browsing needs no API credentials. These settings are validated
        // when an API-backed feature resolves IMarketDataProvider.
        services.AddOptions<AlpacaOptions>()
            .Bind(configuration.GetSection(AlpacaOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.ApiKeyId),
                "Set Alpaca:ApiKeyId using user secrets or environment variables.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.ApiSecretKey),
                "Set Alpaca:ApiSecretKey using user secrets or environment variables.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.Feed),
                "Alpaca:Feed must not be blank.");

        services.AddHttpClient<IMarketDataProvider, MarketDataClient>((httpClient, provider) =>
        {
            var options = provider.GetRequiredService<IOptions<AlpacaOptions>>().Value;
            return new MarketDataClient(httpClient, options.ApiKeyId, options.ApiSecretKey, options.Feed);
        });

        // Session-long UI components hold no scoped Application or database services.
        services.AddSingleton(new ConsoleInput(input, output));
        services.AddSingleton(new PortfolioRenderer(output));
        services.AddSingleton<PortfolioConsoleApp>();
        return services;
    }
}
