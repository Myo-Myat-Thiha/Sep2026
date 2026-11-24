using System.Data.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sep2026.Application.DTOs;
using Sep2026.Application.Exceptions;
using Sep2026.Application.Interfaces;
using Sep2026.Application.Services;
using Sep2026.Domain;

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
                    var selection = await input.ReadUserSelectionAsync(cancellationToken);
                    if (selection.Action == UserAction.Exit) break;
                    var result = selection.Action switch
                    {
                        UserAction.Create => await CreateUserAsync(cancellationToken),
                        UserAction.SetPassword => await SetInitialPasswordAsync(cancellationToken),
                        _ => await SignInAsync(selection.Email!, cancellationToken)
                    };
                    if (result.Action == UserAction.Exit) break;
                    if (result.User is null) continue;
                    if (await BrowsePortfoliosAsync(result.User, cancellationToken) == PortfolioAction.Exit)
                        break;
                    renderer.ShowWelcome();
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // A host shutdown ends the current input/request and returns gracefully.
            }

            renderer.ShowGoodbye();
        }

        private async Task<(UserAction Action, UserDto? User)> CreateUserAsync(CancellationToken cancellationToken)
        {
            renderer.ShowCreateUser();
            while (true)
            {
                // Collect all input before opening the write scope.
                var form = await input.ReadUserCreationAsync(cancellationToken);
                if (form.Action != UserAction.Create)
                {
                    if (form.Action == UserAction.Back) renderer.ShowWelcome();
                    return (form.Action, null);
                }
                try
                {
                    UserDto user;
                    renderer.ShowLoading("Creating owner profile...");
                    await using (var scope = scopeFactory.CreateAsyncScope())
                    {
                        var service = scope.ServiceProvider.GetRequiredService<UserService>();
                        user = await service.CreateAsync(form.Name!, form.Email!, form.Password!, cancellationToken);
                    }
                    renderer.ShowUserCreated(user);
                    return (UserAction.SignIn, user);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (EmailAlreadyExistsException)
                {
                    renderer.ShowError("A profile with this email already exists. Enter a different email or cancel.");
                }
                catch (Exception exception) when (exception is ArgumentException or FormatException)
                {
                    renderer.ShowError(exception is FormatException
                        ? "Enter one plain email address with a dotted domain, such as user@example.com."
                        : $"Check the name, email, and password. Use {UserProfileRules.MinPasswordLength}-{UserProfileRules.MaxPasswordLength} password characters.");
                }
                catch (Exception exception)
                {
                    logger.LogDebug(exception, "Could not confirm owner profile creation.");
                    // A lost response can follow a committed write. Never automatically resubmit it.
                    renderer.ShowError("Could not confirm profile creation. Check saved profiles before trying again.");
                    return (UserAction.Back, null);
                }
            }
        }

        private async Task<(UserAction Action, UserDto? User)> SignInAsync(string email, CancellationToken cancellationToken)
        {
            var password = await input.ReadPasswordAsync(cancellationToken);
            if (string.IsNullOrEmpty(password))
            {
                renderer.ShowWelcome();
                return (UserAction.Back, null);
            }
            renderer.ShowLoading("Signing in...");
            try
            {
                UserDto? user;
                await using (var scope = scopeFactory.CreateAsyncScope())
                    user = await scope.ServiceProvider.GetRequiredService<UserService>().SignInAsync(email, password, cancellationToken);
                if (user is not null) return (UserAction.SignIn, user);
                renderer.ShowError("Email or password is incorrect. Older profiles without a password need initial setup using P.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                logger.LogDebug(exception, "Could not sign in to the local profile.");
                renderer.ShowError("Sign-in could not be completed. Check the database connection and try again.");
            }
            return (UserAction.Back, null);
        }

        private async Task<(UserAction Action, UserDto? User)> SetInitialPasswordAsync(CancellationToken cancellationToken)
        {
            renderer.ShowPasswordSetup();
            var selection = await input.ReadPasswordSetupEmailAsync(cancellationToken);
            if (selection.Action != UserAction.SetPassword)
            {
                if (selection.Action == UserAction.Back) renderer.ShowWelcome();
                return (selection.Action, null);
            }
            var password = await input.ReadNewPasswordAsync(cancellationToken);
            if (password is null)
            {
                renderer.ShowWelcome();
                return (UserAction.Back, null);
            }
            renderer.ShowLoading("Setting the initial password...");
            try
            {
                bool saved;
                await using (var scope = scopeFactory.CreateAsyncScope())
                    saved = await scope.ServiceProvider.GetRequiredService<UserService>()
                        .SetInitialPasswordAsync(selection.Email!, password, cancellationToken);
                renderer.ShowWelcome();
                renderer.ShowNotice(saved ? "Password set. Sign in with your email and password."
                    : "Initial setup was not applied. Check the email, or sign in with the password already set.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception) when (exception is ArgumentException or FormatException)
            { renderer.ShowError("Enter a valid email and a password that meets the stated length requirement."); }
            catch (Exception exception)
            {
                logger.LogDebug(exception, "Could not confirm initial password setup.");
                renderer.ShowError("Could not confirm setup. Try signing in before repeating setup.");
            }
            return (UserAction.Back, null);
        }

        private async Task<PortfolioAction> BrowsePortfoliosAsync(UserDto user, CancellationToken cancellationToken)
        {
            IReadOnlyList<PortfolioSummaryDto>? portfolios = null;
            var reload = true;

            while (true)
            {
                if (reload)
                {
                    renderer.ShowLoadingList(user);
                    try
                    {
                        await using var scope = scopeFactory.CreateAsyncScope();
                        var service = scope.ServiceProvider.GetRequiredService<IPortfolioService>();
                        portfolios = await service.GetByUserIdAsync(user.UserId, cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        portfolios = null;
                        ShowLoadError(exception, "portfolio list", user.UserId);
                    }
                    reload = false;
                }

                // A failed request stays distinct from a successful empty result.
                if (portfolios is not null)
                    renderer.ShowPortfolioList(user, portfolios);

                var selection = await input.ReadPortfolioSelectionAsync(portfolios?.Count ?? 0, cancellationToken);
                switch (selection.Action)
                {
                    case PortfolioAction.Exit:
                    case PortfolioAction.ChangeUser:
                        return selection.Action;
                    case PortfolioAction.Reload:
                        reload = true;
                        break;
                    case PortfolioAction.Link:
                        if (await LinkPortfolioAsync(user, cancellationToken) == PortfolioAction.Exit)
                            return PortfolioAction.Exit;
                        reload = true;
                        break;
                    case PortfolioAction.Select when portfolios is not null:
                        var action = await ShowDetailsAsync(user.UserId, portfolios[selection.Index].PortfolioId, cancellationToken);
                        if (action is PortfolioAction.Exit or PortfolioAction.ChangeUser)
                            return action;
                        break;
                }
            }
        }

        private async Task<PortfolioAction> ShowDetailsAsync(Guid ownerId, Guid portfolioId, CancellationToken cancellationToken)
        {
            while (true)
            {
                renderer.ShowLoadingDetails(portfolioId);
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var service = scope.ServiceProvider.GetRequiredService<IPortfolioService>();
                    var portfolio = await service.GetByIdAsync(portfolioId, cancellationToken);
                    if (portfolio is null || portfolio.OwnerId != ownerId)
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

        private async Task<PortfolioAction> LinkPortfolioAsync(UserDto user, CancellationToken cancellationToken)
        {
            renderer.ShowLinkPortfolio();
            // Read/confirm all secret input before creating a service/SQL scope.
            var form = await input.ReadPortfolioLinkAsync(cancellationToken);
            if (form.Action != PortfolioAction.Link) return form.Action;
            renderer.ShowLoading("Verifying the Alpaca Paper account and importing its positions...");
            try
            {
                LinkPortfolioResultDto result;
                await using (var scope = scopeFactory.CreateAsyncScope())
                    result = await scope.ServiceProvider.GetRequiredService<LinkPortfolioService>().LinkAsync(user.UserId,
                        new(InvestmentProvider.Alpaca, ProviderEnvironment.Paper, form.DisplayName), form.Credentials!, cancellationToken);
                renderer.ShowLinkResult(result);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (InvestmentAccountReadException exception) { renderer.ShowError(exception.Message); }
            catch (PortfolioLinkCommitUnknownException)
            { renderer.ShowError("Could not confirm the link. Check your portfolio list before retrying; credentials were retained for recovery."); }
            catch (PortfolioLinkNotCommittedException)
            { renderer.ShowError("The portfolio import was not saved. Existing portfolios were preserved. Check the connection and retry."); }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or InvalidDataException)
            { renderer.ShowError(exception.Message); }
            catch (Exception exception)
            {
                // Do not log raw provider/vault exceptions or request objects.
                logger.LogDebug("Link Portfolio failed with {FailureType}.", exception.GetType().Name);
                renderer.ShowError("Could not complete Link Portfolio. Check the provider, database, and protected credential storage.");
            }
            return await input.WaitForContinueAsync(cancellationToken) ? PortfolioAction.Back : PortfolioAction.Exit;
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
