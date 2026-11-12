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

`ConsoleApp/Program.cs` registers both boundaries before `builder.Build()`:

```csharp
builder.Services.AddScoped<IPortfolioRepository, PortfolioRepository>();
builder.Services.AddScoped<IPortfolioQueries, PortfolioQueries>();
builder.Services.AddScoped<IPortfolioService, PortfolioService>();
```

Inside the existing scope, after obtaining a portfolio ID:

```csharp
var service = scope.ServiceProvider.GetRequiredService<IPortfolioService>();
var summaries = await service.GetByUserIdAsync(userId);
// After a portfolio is selected:
var details = await service.GetByIdAsync(portfolioId);

if (details is null)
    Console.WriteLine("Portfolio not found.");
else
    Console.WriteLine($"{details.PortfolioId}: {details.Holdings.Count} holdings");
```

Startup resolves the service to verify wiring; it does not invoke the read method
or make a database/API request. Existing Alpaca configuration validation still
applies to the console host.

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
