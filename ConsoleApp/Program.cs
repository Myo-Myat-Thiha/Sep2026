using ConsoleApp.Configuration;
using ConsoleApp.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// Load appsettings, development user secrets, environment variables, and arguments.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions
{
    ValidateScopes = true,
    ValidateOnBuild = true
}));
builder.Services.AddGeminoConsole(builder.Configuration, Console.In, Console.Out);

using var host = builder.Build();
await ConsoleSession.RunAsync(host);
