# Listing portfolios by user: LINQ walkthrough

`PortfolioQueries.GetByUserIdAsync` implements the Application interface
`IPortfolioQueries`. `PortfolioService` delegates user-list requests here, while
its existing detail operation continues to use `IPortfolioRepository`.

## Where the requested behaviors live

| Behavior | Implementation |
| --- | --- |
| Reject an empty user ID | Guard clause at the start of the query method; Application service also validates its own boundary |
| Honor cancellation | Early cancellation check and token passed to `ToListAsync` |
| Match only this user's portfolios | `Where(portfolio => portfolio.OwnerId == userId)` |
| Include active and archived | No status filter; the result includes the saved `Status` |
| Stable ordering | `OrderByDescending(CreatedAt).ThenBy(PortfolioId)` |
| Summary data only | `Select` of six scalar columns, without any `Include` |
| Empty result | `ToListAsync` produces an empty list, and DTO conversion preserves it |

## Reading the LINQ chain

```csharp
var portfolios = await _context.Portfolios
    .AsNoTracking()
    .Where(portfolio => portfolio.OwnerId == userId)
    .OrderByDescending(portfolio => portfolio.CreatedAt)
    .ThenBy(portfolio => portfolio.PortfolioId)
    .Select(portfolio => new
    {
        portfolio.PortfolioId,
        portfolio.PortfolioType,
        portfolio.CurrencyType,
        portfolio.CashBalance,
        portfolio.Status,
        portfolio.CreatedAt
    })
    .ToListAsync(cancellationToken);
```

`Where` becomes the SQL filter. `OrderByDescending` and `ThenBy` determine the
SQL ordering. `Select` determines the columns retrieved. These operations build a
query expression; `ToListAsync` executes it and reads the results asynchronously.
The owner ID is supplied as a query parameter by EF Core.

The anonymous `new { ... }` object holds only the selected scalar values; EF does
not construct or restore Domain portfolios for this operation. Scalar projection
does not materialize tracked entity objects, and `AsNoTracking` also communicates
the read-only intent explicitly.

Conceptually, SQL Server receives:

```sql
SELECT PortfolioId, PortfolioType, CurrencyType, CashBalance, Status, CreatedAt
FROM dbo.Portfolios
WHERE OwnerId = @userId
ORDER BY CreatedAt DESC, PortfolioId ASC;
```

After the database call, ordinary in-memory LINQ validates and converts the SQL
byte/string values to `PortfolioType`, `CurrencyType`, and `PortfolioStatus`, then
creates `PortfolioSummaryDto` records. The DTO uses concrete enum types instead of
the generic `Enum` base type. Unsupported stored values raise `InvalidDataException`
with the portfolio ID. Enum conversion is kept separate from the SQL translation.

The result is a read-only collection. Both a user with no portfolios and an unknown
user ID produce an empty result; this method does not query the Users table to
distinguish those cases. There is no pagination or status restriction in this
initial all-portfolios-for-one-user operation.

## Tests

Application tests verify list delegation without calling the detail repository.
Infrastructure argument tests reject empty IDs and canceled calls before opening
a connection. SQL Server integration tests seed unrelated owners, active and
archived portfolios, equal creation dates, and existing holdings. They verify
ownership filtering, tie-break ordering, empty results, and selection followed by
the existing detail read. A command interceptor verifies that only one SELECT is
executed and no holdings, securities, users, row versions, or joins are loaded.

Run SQL Server tests using the instructions in the
[Infrastructure test guide](../../../Sep2026.Infrastructure.Tests/README.md).
