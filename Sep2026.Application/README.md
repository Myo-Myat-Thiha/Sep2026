# Application contracts and portfolio details

Application already contains `IPortfolioRepository`, `IMarketDataProvider`, and
`AssetDto`. These define the boundaries used by Infrastructure implementations.

The Portfolio use case also has a caller-facing contract:

| Type | Responsibility |
| --- | --- |
| `IPortfolioRepository` | Load a complete Domain portfolio from persistence |
| `IPortfolioQueries` | List an owner's portfolio summaries without loading detail graphs |
| `IPortfolioService` | Return portfolio details suitable for a caller |
| `PortfolioService` | Coordinate the repository read and map Domain state into DTOs |
| `PortfolioDetailsDto` | Portfolio identity, balance, currency, status, timestamp, and holdings |
| `PortfolioSummaryDto` | Six selection-list fields with concrete portfolio, currency, and status enums |
| `HoldingDetailsDto` | Holding identity, security description, quantity, cost, and optional quote |
| `MarketQuoteDto` | Quote symbol, price, currency, and timestamp |

```text
ConsoleApp -> IPortfolioService -> PortfolioService
    -> IPortfolioRepository -> Infrastructure PortfolioRepository -> SQL Server
    <- restored Domain portfolio
    -> PortfolioDetailsDto returned to ConsoleApp
```

The repository returns a Domain object so Application can use its validated state
and behavior. The caller-facing service returns DTOs, so ConsoleApp and a future
API can display data without receiving mutable Domain objects or EF entities.
Domain-to-DTO mapping belongs here; EF-entity-to-Domain mapping stays in
Infrastructure. Domain does not know about DTOs or the persistence classes.

`PortfolioService.GetByIdAsync` validates the ID, forwards cancellation, returns
null for a missing portfolio, and allows repository errors to propagate. It
copies the loaded scalar values into immutable records and exposes a read-only
holding list. Later changes to the source Domain objects do not alter the result.
Enums are returned as names, and quote absence stays null rather than becoming
a zero price. A quote with price zero remains a populated quote record.

`PortfolioService.GetByUserIdAsync` delegates to `IPortfolioQueries` and returns
its `PortfolioSummaryDto` list directly. It does not call the detail repository
or map full Domain portfolios. Summary DTOs use the concrete Domain enum types;
detail DTOs continue to expose their enum values as names.

The summary query rejects empty user IDs, returns an empty list when nothing
matches, includes active and archived portfolios, and orders by creation time
descending followed by portfolio ID ascending. The query projects six scalar
columns from `Portfolios`; it does not load holdings, securities, or owner objects.

Portfolio currency and quote currency are returned separately. No price refresh,
currency conversion, portfolio totals, writes, or broker calls are performed by
this read use case. Quote timestamps describe the saved observation, not the time
the DTO was requested. API refresh can be added as a separate explicit use case.

## Dependency injection and usage

`ConsoleApp/Program.cs` calls `Configuration/ServiceRegistration.cs` to register
these boundaries before `builder.Build()`:

```csharp
builder.Services.AddScoped<IPortfolioRepository, PortfolioRepository>();
builder.Services.AddScoped<IPortfolioQueries, PortfolioQueries>();
builder.Services.AddScoped<IPortfolioService, PortfolioService>();
```

After obtaining a portfolio ID, a caller creates a scope for one data action:

```csharp
await using var scope = scopeFactory.CreateAsyncScope();
var service = scope.ServiceProvider.GetRequiredService<IPortfolioService>();
var details = await service.GetByIdAsync(portfolioId, cancellationToken);

if (details is null)
    Console.WriteLine("Portfolio not found.");
else
    Console.WriteLine($"{details.PortfolioId}: {details.Holdings.Count} holdings");
```

The console presentation holds a scope factory and resolves this service for each
list, detail, or reload request. It disposes the scope before prompting again and
retains only detached DTOs between requests. Each new action receives a new scoped
context. Host shutdown cancellation is forwarded into the service. These UI
actions do not request new market prices and need no Alpaca credentials; API
configuration is validated when the market data provider is requested.

## Create User contract

`UserService.CreateAsync(string name, string email, string password, CancellationToken cancellationToken)`
returns `Task<UserDto>`. The inputs are plain parameters; the response is a DTO:

| Type | Direction | Fields |
| --- | --- | --- |
| Method parameters | Caller to service | name, email, password |
| `UserDto` | Service to caller after a successful save | UserId, Name, Email |

The parameters contain the caller's input. The response includes the generated ID.
The DTOs carry data. This operation creates a portfolio-owner profile
with a local password. `UserService` implements it as a Transaction Script using
shared `UserProfileRules` and does not load portfolios.

## Owner profile creation and storage

The contracts live in `Interfaces`; they do not expose EF entities or DbContext.

| Contract | Operation | Responsibility |
| --- | --- | --- |
| `IUserStore` | `Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default)` | Check saved users using the database's case-insensitive email comparison; honor cancellation |
| `IUserStore` | `Task CreateAsync(Guid userId, string name, string email, string password, CancellationToken cancellationToken = default)` | Hash the password and save the profile |
| `IUserStore` | `Task<UserDto?> AuthenticateAsync(string email, string password, CancellationToken cancellationToken = default)` | Return safe profile data after verification, or null |
| `IUserStore` | `Task<bool> SetInitialPasswordAsync(string email, string password, CancellationToken cancellationToken = default)` | Trusted-device bootstrap, only where a hash is null |

The `UserService.CreateAsync` flow is:

1. Reject null fields and requested cancellation. Normalize and validate the
   name, email, and password through `Domain/UserProfileRules`. Only name/email are normalized.
2. Pass the normalized email to `EmailExistsAsync`; throw `EmailAlreadyExistsException`
   if it is already saved.
3. Check cancellation again and generate a GUID.
4. Await `IUserStore.CreateAsync(userId, name, email, password, cancellationToken)`, including hashing and saving.
5. Return `UserDto` only after a successful save.

The profile procedure and storage implementation share `UserProfileRules`.
Normalization returns validated input, while validation-only methods check
supplied values without changing them.

`Infrastructure/Persistence/Stores/SqlServerUserStore` maps the three parameters into
the generated EF `User` entity, hashes the password, and awaits `SaveChangesAsync`. It creates
no portfolios and performs no broker calls. A successful insert
leaves a profile whose portfolio list is initially empty.

`UserService`, `IUserStore`, and `GeminoDbContext` are registered as scoped.
The console collects the entire form before opening a fresh write scope and
disposes it before displaying success or requesting more input. The store rejects
unrelated pending writes, so this operation cannot accidentally commit another
workflow's changes. Its attempted insertion is detached after success or failure.

Profile creation saves through its focused store. The Portfolio repository remains
read-only. An alternative device-local store can implement `IUserStore` later;
database schema, uniqueness semantics, and provider mappings still need review.

`UserStore` is a focused data-access abstraction, similar in role to a repository.
Here it works with scalar profile fields and saves its insertion immediately;
the Portfolio repository loads complete Domain portfolios. They use the same
database. `UserService` is injected directly because it has one implementation;
`IUserStore` remains the boundary for storage substitution and service tests.

`UserDto` carries the created profile back to the caller. The four portfolio
response records are grouped in `DTOs/PortfolioDtos.cs` to keep related shapes
together without changing their public types. EF entities remain in Infrastructure.

`SignInAsync` normalizes email and forwards the exact password; it returns a
`UserDto` or null. Passwords and hashes never enter DTOs. The SQL store uses the
standalone Microsoft `PasswordHasher<User>` from `Microsoft.Extensions.Identity.Core`
with Identity V3 PBKDF2-HMAC-SHA512, random salts, and 210,000 iterations.
Successful verification can conditionally upgrade an older hash. Missing or
malformed hashes cannot sign in. This component does not add web registration,
Identity tables, or JWTs.

New passwords use 15-128 characters and preserve spaces. Initial setup is only
for legacy null hashes on the trusted device. A conditional update prevents
concurrent attempts from replacing the winning hash. The console returns to
sign-in afterward. It is not a password-reset or recovery facility.

The email check is an early response, not a guarantee against simultaneous
inserts. SQL Server's unique email constraint remains authoritative. The store
translates SQL Server duplicate-key errors naming `UQ_Users_Email` into
`EmailAlreadyExistsException`, preserving the `DbUpdateException` as its inner
exception. Duplicate primary keys and other failures propagate without being
mislabeled. The service also uses this exception for its early check.

Cancellation is passed into both storage calls. Once the store confirms success,
the service returns the DTO without a post-save cancellation check. The console
does not automatically resubmit a failed write: a lost response does not establish
whether the database committed it. Duplicate or invalid input can be corrected
by entering a fresh form; other failures return to user selection.

## Tests

```powershell
dotnet test Sep2026.Application.Tests/Sep2026.Application.Tests.csproj
```

The tests use a small repository stub, requiring neither SQL Server nor API
credentials. They verify the DTO mapping, quote metadata and absence, missing
portfolios, invalid IDs, cancellation forwarding, error propagation, and detached
read-only results. Summary-list tests verify delegation to queries, forwarded user
ID and cancellation, empty results, and error propagation without detail loads.
The Application test project references only Application.

`UserServiceTests` use a fake `IUserStore` to verify validation before persistence,
normalization, identity preservation, duplicate handling, cancellation, failure
propagation, and awaiting the lookup and save. No database connection is needed.
