using System;
using System.Data.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sep2026.Application.DTOs;
using Sep2026.Application.Interfaces;

namespace ConsoleApp.Presentation
{
    internal sealed class PortfolioConsoleApp
    {
        private readonly IServiceScopeFactory scopeFactory;
        private readonly ConsoleInput input;
        private readonly PortfolioRenderer renderer;
        private readonly ILogger<PortfolioConsoleApp> logger;

        public PortfolioConsoleApp(
            IServiceScopeFactory scopeFactory,
            ConsoleInput input,
            PortfolioRenderer renderer,
            ILogger<PortfolioConsoleApp> logger)
        {
            ArgumentNullException.ThrowIfNull(scopeFactory);
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(renderer);
            ArgumentNullException.ThrowIfNull(logger);
            this.scopeFactory = scopeFactory;
            this.input = input;
            this.renderer = renderer;
            this.logger = logger;
        }

        public async Task RunAsync(CancellationToken cancellationToken = default)
        {
            renderer.ShowWelcome();
            try
            {
                while (true)
                {
                    var userId = await input.ReadUserIdAsync(cancellationToken);
                    if (!userId.HasValue) break;

                    if (await BrowsePortfoliosAsync(userId.Value, cancellationToken) == PortfolioAction.Exit)
                        break;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // A host shutdown ends the current input/request and returns gracefully.
            }

            renderer.ShowGoodbye();
        }

        private async Task<PortfolioAction> BrowsePortfoliosAsync(Guid userId, CancellationToken cancellationToken)
        {
            IReadOnlyList<PortfolioSummaryDto>? portfolios = null;
            var reload = true;

            while (true)
            {
                if (reload)
                {
                    renderer.ShowLoadingList(userId);
                    try
                    {
                        await using var scope = scopeFactory.CreateAsyncScope();
                        var service = scope.ServiceProvider.GetRequiredService<IPortfolioService>();
                        portfolios = await service.GetByUserIdAsync(userId, cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        portfolios = null;
                        ShowLoadError(exception, "portfolio list", userId);
                    }
                    reload = false;
                }

                // A failed request stays distinct from a successful empty result.
                if (portfolios is not null)
                    renderer.ShowPortfolioList(userId, portfolios);

                var selection = await input.ReadPortfolioSelectionAsync(portfolios?.Count ?? 0, cancellationToken);
                switch (selection.Action)
                {
                    case PortfolioAction.Exit:
                    case PortfolioAction.ChangeUser:
                        return selection.Action;
                    case PortfolioAction.Reload:
                        reload = true;
                        break;
                    case PortfolioAction.Select when portfolios is not null:
                        var action = await ShowDetailsAsync(portfolios[selection.Index].PortfolioId, cancellationToken);
                        if (action is PortfolioAction.Exit or PortfolioAction.ChangeUser)
                            return action;
                        break;
                }
            }
        }

        private async Task<PortfolioAction> ShowDetailsAsync(Guid portfolioId, CancellationToken cancellationToken)
        {
            while (true)
            {
                renderer.ShowLoadingDetails(portfolioId);
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var service = scope.ServiceProvider.GetRequiredService<IPortfolioService>();
                    var portfolio = await service.GetByIdAsync(portfolioId, cancellationToken);
                    if (portfolio is null)
                        renderer.ShowMissingPortfolio(portfolioId);
                    else
                        renderer.ShowPortfolioDetails(portfolio);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    ShowLoadError(exception, "portfolio details", portfolioId);
                }

                var action = await input.ReadDetailActionAsync(cancellationToken);
                if (action != PortfolioAction.Reload) return action;
            }
        }

        private void ShowLoadError(Exception exception, string operation, Guid id)
        {
            // The presentation prints a friendly message; full diagnostics are available at Debug level.
            logger.LogDebug(exception, "Could not load {Operation} for {Id}.", operation, id);
            var message = exception switch
            {
                DbException or TimeoutException => "The database request failed. Check the connection and use R to retry.",
                InvalidDataException => "The saved portfolio data could not be read. Review the stored values before retrying.",
                OperationCanceledException => "The request was interrupted. Use R to retry or choose another command.",
                _ => "The request could not be completed. Use R to retry or choose another command."
            };
            renderer.ShowError(message);
        }
    }
}
