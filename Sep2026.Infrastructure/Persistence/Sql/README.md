# SQL Server database bootstrap

Run `001_CreatePortfolioDatabase.sql` against your chosen SQL Server instance.
It creates the `Gemino` database if it is missing, followed by four portfolio tables
and the `MarketPriceSnapshots` history table in the `dbo` schema. No application
packages are needed to run it. Broker order and execution tables are deferred.

## Running the script

In SQL Server Management Studio, connect to your SQL Server instance, open the
script, and execute the complete file. The login needs permission to create the
database and its tables. If the database already exists, permission to create its
missing tables is sufficient.

If you have already created `Users`, `Portfolios`, `Securities`, and `Holdings` in
`Gemino` using the earlier definitions, run this updated file in full. It skips
those tables and adds `MarketPriceSnapshots`. It does not create `Trades`, and
any pre-existing `Trades` table is left untouched. Existing tables are assumed to
have the earlier keys and columns, including `Securities.SecurityId`.

Alternatively, from the solution directory, using Windows authentication and
replacing the example server name if necessary:

```powershell
sqlcmd -S ".\SQLEXPRESS" -E -b -i ".\Sep2026.Infrastructure\Persistence\Sql\001_CreatePortfolioDatabase.sql"
```

`GO` separates batches in SSMS and sqlcmd. Database creation happens before the
transaction that creates the tables. If table creation fails, changes in that
schema transaction roll back; a newly created database may remain empty.

The script does not drop objects or delete data. Re-running it skips existing
tables and does not alter or verify their definitions. Use separately versioned
`ALTER TABLE` scripts for subsequent schema changes; this file is the initial
bootstrap, not an ongoing schema synchronizer. The database name is fixed in the
script; change every occurrence if you choose a different name.

## Schema choices

| Table | Purpose |
| --- | --- |
| `Users` | Portfolio owners, with unique email addresses |
| `Portfolios` | Cash balance, owner, portfolio type, currency, status, and concurrency token |
| `Securities` | Security identity and type, with an optional cached market quote |
| `Holdings` | Whole-share quantities and average purchase prices per portfolio/security |
| `MarketPriceSnapshots` | Historical observed market prices with event time, receipt time, provider, and feed |

- IDs for the four portfolio tables are `uniqueidentifier`, matching the existing
  Domain IDs. SQL defaults generate IDs for callers that omit them; callers can
  supply their existing IDs. Price-history rows use an automatically generated
  `bigint` identity key.
- Cash and prices use `decimal(19,8)`. The application must use compatible precision
  and rounding rules. Share quantities use `int`, matching the current whole-share
  model. Negative balances and quantities are rejected.
- `PortfolioType` uses the existing enum values: 0 Individual, 1 Joint,
  2 Retirement, 3 Education, 4 Trust, 5 Corporate. `Status` is 0 Active or 1 Archived.
- Currencies are stored as uppercase three-letter codes, matching the seven
  supported Domain currencies. Repository mapping must translate between these
  codes and the Domain's `CurrencyType` enum. Every price snapshot records its own
  currency. No currency conversion is performed by the schema.
- A holding is unique on `(PortfolioId, SecurityId)`. Empty holdings may keep their
  previous average price, matching the current sell behavior.
- Emails and ticker symbols have case-insensitive uniqueness. The initial model
  assumes one security per ticker in the supported market; expand the natural key
  if exchange-specific or multi-market instruments are introduced.
- A cached quote requires both `CurrentValue` and `QuoteAt`, or neither. Its
  currency is stored in `Securities.CurrencyType`. NULL price means unavailable.
- Foreign keys use SQL Server's default `NO ACTION` behavior. Existing holdings
  prevent deletion of their referenced portfolios or securities. Price history
  also prevents deletion of its referenced securities.

## Price history for analysis

`MarketPriceSnapshots` records the single latest-trade price exposed by the
current `MarketQuote` model. These are public market observations, independent
of a particular portfolio or an executed order in a broker account. Bid/ask quotes,
OHLCV bars, broker orders, and account executions can have separate tables later.

| Column | Source / meaning |
| --- | --- |
| `SecurityId` | Existing local security being priced |
| `Price` | `MarketQuote.Price`, using `decimal(19,8)` |
| `CurrencyType` | `MarketQuote.CurrencyType` |
| `MarketTimestamp` | `MarketQuote.QuoteAt`, the provider's market-event time |
| `ReceivedAt` | UTC storage time, defaulted by SQL Server |
| `Provider` | Provider name, for example `Alpaca` |
| `Feed` | Data feed, for example `iex`; store explicitly for each observation |

The collection workflow will supply provider/feed metadata in addition to the
existing `MarketQuote` fields. No prices or provider credentials are seeded.

The unique observation key is `(SecurityId, Provider, Feed, CurrencyType,
MarketTimestamp)`. Repeated polling of the same observation must be handled as a
duplicate by the future ingestion workflow. A deliberate provider correction to
that observation can update the existing record. Prices must be nonnegative and
provider/feed names must not be blank. Unavailable prices have no snapshot row.

The time-range index supports looking up one security's history by market time.
Retain `Securities.CurrentValue` and `QuoteAt` as an optional latest-price cache;
the collection workflow must maintain that cache explicitly if it uses it. An
older snapshot must not overwrite a newer cache entry. No automatic trigger is
installed to synchronize the cache.

`datetimeoffset(7)` stores timestamps to 100-nanosecond precision. This table
represents one price snapshot per observation key, not a complete tick-by-tick
market trade archive. A future tick archive would need provider event identifiers
and a suitable timestamp representation to preserve finer event distinctions.

## Transactions and concurrency

`Portfolios.RowVersion` and `Holdings.RowVersion` are SQL Server-generated
concurrency tokens. EF Core can reverse-engineer these columns when the database
is scaffolded. Their SQL Server behavior is documented in
[rowversion](https://learn.microsoft.com/en-us/sql/t-sql/data-types/rowversion-transact-sql).

The schema prepares for concurrency checking; the future write workflow must
include the original row version in its update checks and handle conflicts.
Every cash/holding synchronization must also update the portfolio row in the same
transaction. Updating a holding alone does not advance the portfolio row version.
Holding insertion conflicts may surface as unique-constraint violations.

When broker synchronization is introduced, save the related portfolio and holding
changes using one shared database transaction. The broker remains authoritative
for account executions and balances; a successful order-submission response alone
must not be treated as a filled order. Perform external API calls outside the local
database transaction and reconcile confirmed broker events with local state.

The script does not install trading workflows, broker order/execution tables,
triggers, or stored procedures to synchronize balances. Those can be designed
when the external integration is introduced. Price collection does not alter cash
or holding quantities.

## Next step: EF Core database-first

After creating the database, scaffold the context and persistence entities into
`Sep2026.Infrastructure/Persistence`. Keep generated entities separate from Domain
models and put custom repository mappings outside generated files. EF Core and
its SQL Server provider have not been installed as part of this script change.

See Microsoft's [reverse engineering guidance](https://learn.microsoft.com/en-us/ef/core/managing-schemas/scaffolding/).
