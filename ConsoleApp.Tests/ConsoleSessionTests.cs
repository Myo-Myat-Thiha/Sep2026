using ConsoleApp.Configuration;
using ConsoleApp.Hosting;
using ConsoleApp.Presentation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Sep2026.Application.DTOs;
using Sep2026.Application.Interfaces;
using Sep2026.Domain;
using Sep2026.Infrastructure.Persistence;
using Xunit;

namespace ConsoleApp.Tests;

public sealed class ConsoleSessionTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Navigation_uses_a_fresh_scope_for_each_load_and_disposes_it_before_input()
    {
        var user = Guid.NewGuid();
        var otherUser = Guid.NewGuid();
        var scenario = new Scenario();
        using var output = new StringWriter();
        using var input = new ScopeCheckingReader($"{user}\n1\nr\nb\nr\nu\n{otherUser}\nq\n", scenario);
        using var host = CreateHost(input, output, scenario);

        await ConsoleSession.RunAsync(host).WaitAsync(Timeout);

        Assert.Equal(new[] { user, user, otherUser }, scenario.UserRequests);
        Assert.Equal(new[] { scenario.PortfolioId, scenario.PortfolioId }, scenario.DetailRequests);
        Assert.Equal(5, scenario.Instances.Count);
        Assert.Equal(5, scenario.Instances.Select(instance => instance.Context.ContextId.InstanceId).Distinct().Count());
        AssertScopesDisposed(scenario);
        Assert.True(host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopped.IsCancellationRequested);
        Assert.Contains("Goodbye", output.ToString());
    }

    [Theory]
    [InlineData("q\n")]
    [InlineData("")]
    public async Task Quit_and_eof_stop_the_host_without_resolving_data_or_api_services(string text)
    {
        var scenario = new Scenario();
        using var output = new StringWriter();
        using var host = CreateHost(new StringReader(text), output, scenario);

        await ConsoleSession.RunAsync(host).WaitAsync(Timeout);

        Assert.Empty(scenario.Instances);
        Assert.Contains("Goodbye", output.ToString());
        Assert.True(host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopped.IsCancellationRequested);
    }

    [Fact]
    public async Task Failed_list_can_be_retried_with_a_fresh_scope_and_no_exception_details_on_screen()
    {
        var scenario = new Scenario { FailNextList = true };
        using var output = new StringWriter();
        using var input = new ScopeCheckingReader($"{Guid.NewGuid()}\nr\nq\n", scenario);
        using var host = CreateHost(input, output, scenario);

        await ConsoleSession.RunAsync(host).WaitAsync(Timeout);

        Assert.Equal(2, scenario.Instances.Count);
        AssertScopesDisposed(scenario);
        Assert.Contains("The database request failed", output.ToString());
        Assert.Contains("Individual", output.ToString());
        Assert.DoesNotContain("private diagnostic", output.ToString());
    }

    [Fact]
    public async Task Missing_detail_and_empty_list_are_rendered_as_saved_data_outcomes()
    {
        var scenario = new Scenario { MissingDetail = true };
        using var output = new StringWriter();
        using var host = CreateHost(new StringReader($"{Guid.NewGuid()}\n1\nq\n"), output, scenario);
        await ConsoleSession.RunAsync(host).WaitAsync(Timeout);
        Assert.Contains("was not found", output.ToString());
        Assert.DoesNotContain("Error:", output.ToString());
        AssertScopesDisposed(scenario);

        var empty = new Scenario { EmptyList = true };
        using var emptyOutput = new StringWriter();
        using var emptyHost = CreateHost(new StringReader($"{Guid.NewGuid()}\nq\n"), emptyOutput, empty);
        await ConsoleSession.RunAsync(emptyHost).WaitAsync(Timeout);
        Assert.Contains("No portfolios found", emptyOutput.ToString());
        Assert.Empty(empty.DetailRequests);
        AssertScopesDisposed(empty);
    }

    [Fact]
    public async Task Host_shutdown_cancels_a_blocked_input_wait()
    {
        using var input = new BlockingReader();
        using var output = new StringWriter();
        using var host = CreateHost(input, output, new Scenario());
        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        var session = ConsoleSession.RunAsync(host);
        try
        {
            await input.Started.Task.WaitAsync(Timeout);
            lifetime.StopApplication();
            await session.WaitAsync(Timeout);
            Assert.True(lifetime.ApplicationStopped.IsCancellationRequested);
            Assert.Contains("Goodbye", output.ToString());
        }
        finally
        {
            input.Release();
            await input.Finished.Task.WaitAsync(Timeout);
        }
    }

    [Fact]
    public async Task Host_shutdown_cancels_the_active_data_request_and_disposes_its_scope()
    {
        var scenario = new Scenario { BlockList = true };
        using var output = new StringWriter();
        using var host = CreateHost(new StringReader($"{Guid.NewGuid()}\n"), output, scenario);
        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        var session = ConsoleSession.RunAsync(host);
        try
        {
            await scenario.RequestStarted.Task.WaitAsync(Timeout);
        }
        finally
        {
            lifetime.StopApplication();
        }
        await session.WaitAsync(Timeout);

        Assert.True(scenario.RequestCancelled);
        AssertScopesDisposed(scenario);
        Assert.DoesNotContain("Error:", output.ToString());
        Assert.Contains("Goodbye", output.ToString());
    }

    [Fact]
    public async Task External_session_token_also_stops_the_host()
    {
        var scenario = new Scenario { BlockList = true };
        using var host = CreateHost(new StringReader($"{Guid.NewGuid()}\n"), TextWriter.Null, scenario);
        using var cancellation = new CancellationTokenSource();
        var session = ConsoleSession.RunAsync(host, cancellation.Token);
        try
        {
            await scenario.RequestStarted.Task.WaitAsync(Timeout);
        }
        finally
        {
            cancellation.Cancel();
        }
        await session.WaitAsync(Timeout);
        Assert.True(scenario.RequestCancelled);
        AssertScopesDisposed(scenario);
        Assert.True(host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopped.IsCancellationRequested);
    }

    [Fact]
    public async Task Host_stops_when_a_presentation_error_escapes()
    {
        using var host = CreateHost(new ThrowingReader(), TextWriter.Null, new Scenario());
        await Assert.ThrowsAsync<IOException>(() => ConsoleSession.RunAsync(host).WaitAsync(Timeout));
        Assert.True(host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopped.IsCancellationRequested);
    }

    [Fact]
    public async Task Production_registration_starts_without_api_credentials_and_enforces_scoped_services()
    {
        using var host = CreateHost(new StringReader("q\n"), TextWriter.Null);
        try
        {
            await host.StartAsync();
            Assert.NotNull(host.Services.GetRequiredService<PortfolioConsoleApp>());
            Assert.Throws<InvalidOperationException>(() => host.Services.GetRequiredService<IPortfolioService>());
            await using var scope = host.Services.CreateAsyncScope();
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<IPortfolioService>());
            Assert.Same(scope.ServiceProvider.GetRequiredService<GeminoDbContext>(),
                scope.ServiceProvider.GetRequiredService<GeminoDbContext>());
            Assert.Throws<OptionsValidationException>(() => scope.ServiceProvider.GetRequiredService<IMarketDataProvider>());
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Theory]
    [InlineData("iex", true)]
    [InlineData("", false)]
    public void Api_settings_are_validated_when_the_provider_is_requested(string feed, bool valid)
    {
        using var host = CreateHost(new StringReader("q\n"), TextWriter.Null, apiSettings: new()
        {
            ["Alpaca:ApiKeyId"] = "test-key",
            ["Alpaca:ApiSecretKey"] = "test-secret",
            ["Alpaca:Feed"] = feed
        });
        using var scope = host.Services.CreateScope();
        if (valid)
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<IMarketDataProvider>());
        else
            Assert.Throws<OptionsValidationException>(() => scope.ServiceProvider.GetRequiredService<IMarketDataProvider>());
    }

    [Fact]
    public void Missing_connection_string_fails_registration_with_a_clear_message()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddGeminoConsole(configuration, TextReader.Null, TextWriter.Null));
        Assert.Contains("ConnectionStrings:Gemino", exception.Message);
    }

    private static IHost CreateHost(TextReader input, TextWriter output, Scenario? scenario = null,
        Dictionary<string, string?>? apiSettings = null)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            DisableDefaults = true,
            EnvironmentName = "Test"
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            // Context construction is real; fake services below perform no SQL operations.
            ["ConnectionStrings:Gemino"] = "Server=unused-test-server;Database=Gemino;Integrated Security=True;Connect Timeout=1"
        });
        if (apiSettings is not null) builder.Configuration.AddInMemoryCollection(apiSettings);
        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        }));
        builder.Services.AddGeminoConsole(builder.Configuration, input, output);
        builder.Services.AddSingleton<IHostLifetime, TestHostLifetime>();
        if (scenario is not null)
        {
            builder.Services.RemoveAll<IPortfolioService>();
            builder.Services.AddScoped<IPortfolioService>(provider =>
                new RecordingService(provider.GetRequiredService<GeminoDbContext>(), scenario));
        }
        return builder.Build();
    }

    private static void AssertScopesDisposed(Scenario scenario)
    {
        Assert.All(scenario.Instances, instance =>
        {
            Assert.True(instance.DisposedAsync);
            Assert.Throws<ObjectDisposedException>(() => instance.Context.ChangeTracker);
        });
    }

    private sealed class Scenario
    {
        internal Guid PortfolioId { get; } = Guid.NewGuid();
        internal List<Guid> UserRequests { get; } = new();
        internal List<Guid> DetailRequests { get; } = new();
        internal List<RecordingService> Instances { get; } = new();
        internal TaskCompletionSource RequestStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool FailNextList { get; set; }
        internal bool EmptyList { get; init; }
        internal bool MissingDetail { get; init; }
        internal bool BlockList { get; init; }
        internal bool RequestCancelled { get; set; }
    }

    private sealed class RecordingService : IPortfolioService, IAsyncDisposable
    {
        private readonly Scenario scenario;
        internal GeminoDbContext Context { get; }
        internal bool DisposedAsync { get; private set; }

        internal RecordingService(GeminoDbContext context, Scenario scenario)
        {
            Context = context;
            this.scenario = scenario;
            scenario.Instances.Add(this);
        }

        public async Task<IReadOnlyList<PortfolioSummaryDto>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            scenario.UserRequests.Add(userId);
            if (scenario.BlockList)
            {
                scenario.RequestStarted.TrySetResult();
                try
                {
                    await Task.Delay(System.Threading.Timeout.Infinite, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    scenario.RequestCancelled = true;
                    throw;
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (scenario.FailNextList)
            {
                scenario.FailNextList = false;
                throw new TimeoutException("private diagnostic");
            }
            return scenario.EmptyList ? Array.Empty<PortfolioSummaryDto>() : new[]
            {
                new PortfolioSummaryDto(scenario.PortfolioId, PortfolioType.Individual,
                    CurrencyType.GBP, 10m, PortfolioStatus.Active, DateTimeOffset.UtcNow)
            };
        }

        public Task<PortfolioDetailsDto?> GetByIdAsync(Guid portfolioId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            scenario.DetailRequests.Add(portfolioId);
            return Task.FromResult(scenario.MissingDetail ? null : new PortfolioDetailsDto(portfolioId,
                scenario.UserRequests.Last(), 10m, "Individual", "GBP", "Active", DateTimeOffset.UtcNow,
                Array.Empty<HoldingDetailsDto>()));
        }

        public async ValueTask DisposeAsync()
        {
            await Task.Yield();
            DisposedAsync = true;
        }
    }

    private sealed class ScopeCheckingReader(string text, Scenario scenario) : StringReader(text)
    {
        public override ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            AssertScopesDisposed(scenario);
            return base.ReadLineAsync(cancellationToken);
        }
    }

    private sealed class ThrowingReader : TextReader
    {
        public override ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
            => ValueTask.FromException<string?>(new IOException("Input disconnected"));
    }

    private sealed class TestHostLifetime : IHostLifetime
    {
        public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
