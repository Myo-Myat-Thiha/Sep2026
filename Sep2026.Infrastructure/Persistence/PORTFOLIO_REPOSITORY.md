# Portfolio repository: first read-only implementation

One repository loads the Portfolio aggregate, including its holdings and each
holding's security. Holdings do not need an independent repository for this use
case. Security mapping is shared code, not an additional Security repository.

## Files and dependency flow

| File | Responsibility |
| --- | --- |
| `Application/Interfaces/IPortfolioRepository.cs` | Application-facing contract returning a Domain portfolio |
| `Persistence/Repositories/PortfolioRepository.cs` | EF Core query, relationship loading, and cancellation |
| `Persistence/Mappings/PortfolioMapper.cs` | Portfolio fields, currency conversion, and restoration |
| `Persistence/Mappings/HoldingMapper.cs` | Holding identity, quantities, security relationship, and restoration |
| `Persistence/Mappings/SecurityMapper.cs` | Security identity and optional quote with currency and timestamp |
| `ConsoleApp/Program.cs` | Scoped dependency registration |

```text
Application interface: IPortfolioRepository
    implemented by Infrastructure: PortfolioRepository
        queries GeminoDbContext
            SQL Server -> generated Portfolio + Holdings + Securities
        calls PortfolioMapper
            HoldingMapper -> SecurityMapper -> Domain restoration factories
        returns PortfolioModel
```

The mappers are internal, synchronous, and independent of database connections.
They call `Restore(...)` to preserve saved IDs and state. They do not call business
operations or save data. The generated context and entity files are unchanged.

## Read behavior

The user selection list uses a separate summary query. See
[the LINQ query walkthrough](Queries/README.md). The repository continues to load
full details only after a portfolio has been selected.

`GetByIdAsync(portfolioId, cancellationToken)` rejects an empty ID, honors
cancellation, and explicitly loads the full holdings/security graph using
`Include` and `ThenInclude`. It uses `AsNoTracking` for this read-only operation.
The owner graph and price-history collections are not required for restoration.

- A missing portfolio returns null.
- A portfolio with no holdings returns an empty collection.
- Both cached price and quote timestamp absent restore an unknown quote.
- Both present restore a quote, including known zero prices.
- Incomplete quotes, unsupported values, or inconsistent relationships throw
  `InvalidDataException` with the relevant record ID. Domain validation failures
  are retained as inner exceptions. Database connection failures are not hidden.
- Enum and currency conversions are validated; no unsupported default is invented.

Mapping deliberately follows only Portfolio -> Holdings -> Securities. It does
not traverse EF back-references, so it does not recursively rebuild the owner or
the portfolio from each holding.

## Using the registered repository

`Program.cs` already registers:

```csharp
builder.Services.AddScoped<IPortfolioRepository, PortfolioRepository>();
builder.Services.AddScoped<IPortfolioService, PortfolioService>();
```

Application's `PortfolioService` uses this repository and maps the Domain result
to `PortfolioDetailsDto`. Inside the existing console scope, after obtaining a
real portfolio ID from your database, a caller-facing read can be performed like this:

```csharp
var service = scope.ServiceProvider.GetRequiredService<IPortfolioService>();
var portfolio = await service.GetByIdAsync(portfolioId);

if (portfolio is null)
    Console.WriteLine("Portfolio not found.");
else
    Console.WriteLine($"{portfolio.PortfolioId}: {portfolio.Holdings.Count} holdings");
```

No query has been added to normal application startup. See the
[Application guide](../../Sep2026.Application/README.md) for the interface and DTO
boundaries. The service and repository share the existing scope.

This interface currently returns a detached Domain snapshot. Changing it does not
automatically change a generated EF entity or save anything. A future write path
must explicitly map approved changes back to tracked EF entities, preserve their
original row versions, and commit once. Detached write requests would need an
explicit concurrency-token contract. No write or trading API is introduced here.

## Tests

Mapper and repository argument tests run without SQL Server. Integration tests use
the actual SQL Server provider and an isolated database generated from the schema
script, checking complete loading, missing portfolios, empty portfolios, and the
read-only contract. See [Infrastructure test instructions](../../Sep2026.Infrastructure.Tests/README.md).
