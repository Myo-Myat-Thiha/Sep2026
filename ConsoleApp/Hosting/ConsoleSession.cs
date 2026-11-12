using ConsoleApp.Presentation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ConsoleApp.Hosting;

internal static class ConsoleSession
{
    internal static async Task RunAsync(IHost host, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(
            lifetime.ApplicationStopping, cancellationToken);

        try
        {
            await host.StartAsync(stopping.Token);
            var app = host.Services.GetRequiredService<PortfolioConsoleApp>();
            await app.RunAsync(stopping.Token);
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            // Shutdown requested while starting or running the console session.
        }
        finally
        {
            // Q/EOF ends the host too. Ctrl+C follows this same cleanup path.
            lifetime.StopApplication();
            await host.StopAsync(CancellationToken.None);
        }
    }
}
