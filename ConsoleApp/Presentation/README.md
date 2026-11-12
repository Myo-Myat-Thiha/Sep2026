# Gemino console presentation

This first console UI uses the existing `IPortfolioService` contracts and DTOs.
It performs saved-data reads when the user requests them.

## Files

| File | Responsibility |
| --- | --- |
| `ConsoleInput.cs` | Read user IDs and menu commands; reject invalid values and selections |
| `PortfolioRenderer.cs` | Render summaries, details, holdings, empty states, and friendly messages |
| `PortfolioConsoleApp.cs` | Coordinate navigation and resolve the Application service in a fresh scope per load |
| `../Configuration/ServiceRegistration.cs` | Register scoped data services, singleton UI, and optional API provider |
| `../Hosting/ConsoleSession.cs` | Start the host, run the UI with its shutdown token, and always stop the host |
| `../Program.cs` | Load configuration, enable DI validation, build the host, and run the session |

The presentation depends on Application interfaces and DTOs. It does not execute
LINQ, access the context, map EF entities, or perform Domain business operations.
The input and renderer receive text readers/writers; Program supplies
`Console.In` and `Console.Out`.

## Run and navigate

From the solution directory, with the existing application configuration:

```powershell
dotnet run --project ConsoleApp/ConsoleApp.csproj
```

1. Enter a nonempty user GUID from your Gemino database. Enter `Q` to exit.
2. The application loads that user's saved portfolio summaries, including active
   and archived portfolios, using `IPortfolioService.GetByUserIdAsync`.
3. Enter a displayed row number to load that portfolio's complete details using
   `GetByIdAsync`. Numbers correspond to the displayed IDs and ordering.
4. In the detail screen, press Enter or `B` to return to the cached list.

| Screen | Commands |
| --- | --- |
| User lookup | User GUID, or `Q` to exit |
| Portfolio list | Row number, `R` reload list, `U` change user, `Q` exit |
| Portfolio details | Enter/`B` back, `R` reload saved details, `U` change user, `Q` exit |

Commands are case-insensitive. Invalid input is explained and prompted again.
End-of-input also exits. Ctrl+C cancels the session and its active request.
`R` rereads Gemino; it does not request live market prices.
Entering a different user ID is a development lookup workflow, not authentication.

## Displayed data

The list shows portfolio type, currency, cash balance, status, and creation date.
The detail screen adds portfolio and owner IDs, the full creation timestamp, and
each holding's ticker, type, ID, share quantity, average purchase price, and cached
quote. Cash and average purchase prices are labeled with portfolio currency;
cached quotes retain their own currency and market timestamp. Numbers use stable
grouping with two to eight decimal places, and full timestamps include the offset.

No quote is displayed as `Price unavailable`; a known zero price is displayed as
zero. Portfolios without holdings have an explicit empty state. No currency
conversion, portfolio totals, trading, or market API refresh is performed.

## Failed and missing reads

A successful empty list is displayed as no matching portfolios. A failed request
shows an error and permits reload, changing user, or exit. A missing detail record
explains that the selected portfolio was not found; return to the list and reload
it if the saved data has changed. Technical exceptions from the presentation
boundary are logged at Debug level, while the renderer prints a friendly message.
The host's existing logging configuration remains in effect.

## Startup and service lifetimes

`ConnectionStrings:Gemino` is required at registration. Starting the host does not
query SQL Server. Alpaca settings are validated only when `IMarketDataProvider`
is requested; the saved-data browser does not need API credentials.

| Service | Lifetime | Reason |
| --- | --- | --- |
| `PortfolioConsoleApp`, `ConsoleInput`, `PortfolioRenderer` | Singleton | One interactive session and its input/output |
| `IPortfolioService`, repository, queries, `GeminoDbContext` | Scoped | One context for a single data action |
| `IMarketDataProvider` | Transient typed HTTP client | Constructed on demand by HTTP client factory |

The UI holds `IServiceScopeFactory`. Each list, detail, or reload creates an async
scope, resolves `IPortfolioService`, awaits the read, and disposes the scope before
the next input prompt. Returning to the cached list does not create a scope.
Only detached DTOs remain while waiting for input. DI scope/build validation is
enabled in every environment to catch invalid lifetime dependencies early.

`ConsoleSession` passes a token linked to `IHostApplicationLifetime.ApplicationStopping`
into the browser. Input and Application calls receive it. `Q` and EOF return
normally, while Ctrl+C requests cancellation through the Generic Host. Cleanup
always stops the host; expected shutdown cancellation does not display a read error.

Some console readers block inside `ReadLineAsync`, so input runs on a worker and
the session's await honors cancellation independently. An underlying read that
ignores cancellation can remain blocked until input arrives or the process exits;
the cancelled session does not start another read. Tests release their blocked
readers before finishing.

## Tests

```powershell
dotnet test ConsoleApp.Tests/ConsoleApp.Tests.csproj
```

Tests use injected readers/writers and a fake Application service. Host tests use
the production registrations with real DI scopes and context construction, but
execute no SQL or HTTP requests. They cover input validation, navigation, reload,
changing user, missing/empty results, retry, rendering, API configuration on
demand, scope disposal before input, and shutdown during input or an active read.
