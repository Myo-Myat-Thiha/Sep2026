# Gemino console presentation

The console UI uses `IPortfolioService` for saved-data reads and `UserService`
for local sign-in, owner profiles, and initial password setup. The signed-in
profile lives only in the console session; there is no JWT or remote registration.

## Planned connected-account workflow

The [updated project scope](../../docs/CONNECTED_PORTFOLIO_SCOPE.md) replaces the
proposed standalone portfolio-creation form with **Link Portfolio**.
The future UI will select a provider, authorize/select an external account,
import its state, and display source, environment, connection status, and last
successful sync. Account authorization is separate from local Gemino sign-in.

The current menus do not connect accounts or synchronize private provider data.
When this workflow is implemented, keep pending/failed initial imports distinct
from successful empty portfolios and retain stale saved data after refresh
failures. A display alias is a local preference; cash and positions come from the
provider. See the [implementation plan](../../docs/LINK_PORTFOLIO_IMPLEMENTATION_PLAN.md)
for the proposed `L: Link Portfolio` and `S: Refresh from provider` actions.

## Files

| File | Responsibility |
| --- | --- |
| `ConsoleInput.cs` | Read user selections, profile forms, and menu commands |
| `PortfolioRenderer.cs` | Render summaries, details, holdings, empty states, and friendly messages |
| `PortfolioConsoleApp.cs` | Coordinate creation/navigation and resolve Application services in a fresh scope per operation |
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

1. Enter an email and password, `N` to create a profile, `P` to initialize a legacy
   profile's password on this trusted device, or `Q` to exit. GUIDs cannot sign in.
2. The application loads that user's saved portfolio summaries, including active
   and archived portfolios, using `IPortfolioService.GetByUserIdAsync`.
3. Enter a displayed row number to load that portfolio's complete details using
   `GetByIdAsync`. Numbers correspond to the displayed IDs and ordering.
4. In the detail screen, press Enter or `B` to return to the cached list.

| Screen | Commands |
| --- | --- |
| Sign-in | Email/password; `N` new profile, `P` initial password, `Q` exit at email |
| Create owner profile | Name/email/password/confirmation; `B` cancel or `Q` exit at name/email |
| Initial password | Existing email, new password, confirmation; cannot replace a password |
| Portfolio list | Row number, `R` reload list, `U` sign out, `Q` exit |
| Portfolio details | Enter/`B` back, `R` reload saved details, `U` sign out, `Q` exit |

Commands are case-insensitive. Invalid input is explained and prompted again.
End-of-input also exits. Ctrl+C cancels the session and its active request.
`R` rereads Gemino; it does not request live market prices.
Passwords are case-sensitive, are never trimmed, and are hidden during interactive
entry. Backspace edits and Escape cancels; blank passwords and EOF cancel too.
Command letters in passwords are literal. New passwords require 15-128 characters
and matching confirmation; spaces are allowed and preserved.

## Create an owner profile

Choose `N` at the email prompt. Enter a name, email, password, and confirmation; matching confirmation starts
creation. Blank/invalid fields and duplicate emails show a message and allow
another form entry. `B` returns to user selection, while `Q` or EOF exits without
submitting an incomplete form. These command letters are reserved at both fields.

Input is collected before opening a scope. The service trims/validates the fields,
checks the email, and awaits storage. Only a confirmed save displays the profile
name and email. The new profile is selected automatically,
and the console loads its initially empty portfolio list in a separate scope.

An unexpected write failure returns to user selection without automatically
retrying. Its outcome may be uncertain, so check saved profiles before submitting
the same creation again. Technical errors are logged at Debug level; the screen
shows a friendly message. Ctrl+C cancels an active request and disposes its scope.

## Displayed data

Each interactive screen clears before rendering. Reads, sign-in, creation, and
initial setup show a loading message that is replaced by the result or error.
Lists identify the owner by name and email. Redirected output and test writers
are not cleared. Tests can inject a clear callback independently of a terminal.

Sign-in failures use a common message; unknown users, wrong passwords, missing
hashes, and malformed hashes cannot sign in. `U` drops the session and requires
another password. Details belonging to another owner are not displayed.

After applying script `002`, legacy profiles have null hashes. `P` sets the first
password on this trusted device only, then returns to sign-in. A conditional SQL
update prevents concurrent setup attempts from overwriting each other. This does
not reset existing passwords. It is local application access control, not database
encryption: someone controlling the device/database can still access local data.

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
| `IPortfolioService`, repository, queries, `UserService`, `IUserStore`, `GeminoDbContext` | Scoped | One context for a single data action |
| `IMarketDataProvider` | Transient typed HTTP client | Constructed on demand by HTTP client factory |

The UI holds `IServiceScopeFactory`. Each create, list, detail, or reload creates an async
scope, resolves the relevant Application service, awaits the operation, and disposes the scope before
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
Profile tests exercise the real Application service with a fake store, covering
creation and selection, corrections after validation/duplicate failures, incomplete
forms, uncertain save outcomes, and shutdown during creation.
Sign-in tests cover non-echoing password entry, confirmation, rejected GUID login,
incorrect credentials, sign-out, initial setup, and ownership checks. Renderer
tests verify clearing on loading, result, and error screens.
