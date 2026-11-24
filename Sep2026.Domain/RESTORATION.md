# Restoring saved Domain state

The public constructors create new objects with new IDs and initial defaults.
The public `Restore(...)` factories preserve existing IDs and state by calling
private constructors. The same validation protects both construction paths.

Infrastructure reads EF entities and passes their values to these
factories. Domain does not accept EF entities or depend on EF Core packages.

## Validate state, then assign it

The private portfolio constructor checks the proposed state before assigning it:

```csharp
if (portfolioId == Guid.Empty)
    throw new ArgumentException("A portfolio ID is required.", nameof(portfolioId));

ArgumentOutOfRangeException.ThrowIfNegative(cashBalance);

if (!Enum.IsDefined(status))
    throw new ArgumentOutOfRangeException(nameof(status));
```

It also validates the owner ID, portfolio type, currency, creation timestamp,
holdings, duplicate identities, and portfolio/holding relationships. Once those
checks pass, it assigns the saved values directly to the fields and properties.

For example, `this.cashBalance = cashBalance` restores the saved balance. Calling
`DepositCash(cashBalance)` would perform a new operation, invoke `EnsureActive`,
and print a deposit message. An archived portfolio can be valid saved state while
still prohibiting new deposits and trades. Restoration therefore does not call
`DepositCash`, `WithdrawCash`, `ApplyBuy`, `ApplySell`, or `ArchivePortfolio`.

Restoration also avoids generating replacement IDs, using the current time for
saved timestamps, or setting saved collections through business-action methods.
Invalid saved state produces a validation exception instead of being silently
corrected or replaced by default values.

## Scope note: future external account imports

The [connected portfolio scope](../docs/CONNECTED_PORTFOLIO_SCOPE.md) makes external
accounts authoritative for synchronized cash and positions. Future imports must
validate/map their reported state without replaying deposits or trades. The
mutation methods discussed above describe current Domain behavior, not the
planned public feature set for a read-only connected account.

Before accepting real snapshots, review whole-share quantities, nonnegative
balances/positions, required cost basis, account-type mappings, and the separation
between local archival and provider connection state. Unsupported external data
must produce an explicit failure while preserving the previous accepted snapshot,
rather than being changed to satisfy existing restoration constraints.

## User input and saved values

`UserProfileRules` is the shared owner-profile policy. The Application creation
procedure uses its normalization methods directly. Normalization methods validate
their returned values; storage uses validation-only methods to check supplied
profile fields without changing them. Owner profiles have no separate Domain
aggregate or restoration factory.

New owner profiles trim surrounding whitespace before
validation. Names must be nonblank and at most 200 UTF-16 code units; Unicode,
internal spaces, apostrophes, and hyphens are allowed. Emails must be nonblank
and at most 254 UTF-16 code units. These limits match the SQL Server `nvarchar`
columns. Casing is preserved for both fields.

New passwords must contain 15-128 characters and must not be blank. They are
never trimmed or normalized. `ValidatePassword` enforces this policy; hashing
and verification belong to Infrastructure.

Email validation uses `MailAddress.TryCreate` plus application rules: one plain
address, no display name, comments, address list, or control characters, and a
dotted domain with no empty labels. Domain literals and local-only hosts are
outside this application's accepted email format. This checks syntax, not mailbox
existence or ownership. Duplicate-email checking belongs to the Application and
database persistence flow.

The validation-only methods check values as supplied. A name with surrounding
spaces can remain valid; an email with surrounding spaces is not a plain address
and is rejected rather than trimmed. Values exceeding field limits are also
rejected even when trimming could make them fit. `UserProfileRulesTests` check
normalization, format policy, Unicode names, and length boundaries directly.

## Example: restore an archived portfolio

```csharp
var portfolio = PortfolioModel.Restore(
    portfolioId: Guid.Parse("405063d1-0829-4e98-a212-f81925568989"),
    cashBalance: 1200m,
    holdings: Array.Empty<HoldingModel>(),
    ownerId: Guid.Parse("c4cac859-cec2-4416-914f-f47bdc65d7e6"),
    portfolioType: PortfolioType.Retirement,
    currencyType: CurrencyType.GBP,
    createdAt: new DateTimeOffset(2024, 2, 3, 10, 30, 0, TimeSpan.Zero),
    status: PortfolioStatus.Archived);
```

The object retains the supplied ID, balance, date, and archived status. Its
normal operational restrictions remain active after restoration.

## Collections and relationships

Portfolio copies its incoming holdings collection before keeping it. Clearing
or appending to the caller's original list cannot change the restored collection.
The contained Domain objects are not cloned; their own operations govern changes.

Portfolio restoration validates unique holding IDs and security IDs, matching portfolio
IDs, and non-null children. Callers must supply complete loaded collections;
an empty collection means that no children were loaded into the Domain object.
`PortfolioRepository` deliberately loads the required relationships before
returning a complete aggregate.

An empty holding may have zero average purchase price or retain a positive
historical average. A nonempty holding requires a positive average. Negative
quantities and prices are rejected. `HoldingModel.PortfolioId` is immutable.

## Security quote state

`SecurityModel` is concrete. New instances default to type `Stock`, and callers
can supply a different nonblank type. `Restore(securityId, tickerSymbol,
securityType, latestQuote)` preserves existing identity and an optional quote.

`LatestQuote` is an immutable `MarketQuote`; `CurrentValue` derives from its price.
Use `UpdateMarketQuote(quote)` to replace the full quote, including currency and
provider timestamp. The old price-only `UpdateCurrentValue` method has been
replaced so a new price cannot accidentally keep unrelated quote metadata.

Both security and quote tickers are trimmed and normalized to uppercase. A quote
must match its security's ticker. Quote currencies are normalized to the supported
uppercase codes, and prices and timestamps are validated. Changing a security's
ticker clears its old quote so the prior instrument's price cannot be reused.

An absent quote means valuation is unknown. A quote with price zero is a known
zero valuation. `SecurityMapper` creates a `MarketQuote` from
`Security.CurrentValue`, `CurrencyType`, and `QuoteAt` when cached quote data is
present, and passes null when it is absent. SQL row versions remain on EF entities.

## Verify the restoration contract

Run the Domain tests from the solution directory:

```powershell
dotnet test Sep2026.Domain.Tests/Sep2026.Domain.Tests.csproj
```

The tests do not require a SQL Server connection or provider credentials. They
exercise factories through their public API and check:

- IDs, dates, enums, balances, and relationships survive restoring an archived graph.
- Restoration produces no business-operation console messages.
- Invalid IDs, enums, amounts, child ownership, null entries, and duplicates fail.
- Empty and sold-out holdings can be restored correctly.
- Changing the caller's lists cannot mutate the restored collections.
- Missing market quotes remain unknown; a zero market price remains a known value.
- Market quote updates retain currency and timestamp and reject a different symbol.
- Adding shares updates the quantity, average price, and cash balance correctly.

The tests have access to existing internal Domain operations through a test-only
friend assembly. Those operations have not been exposed as new public trading APIs.

Keep original EF row versions with the tracked Infrastructure entities when
implementing repositories. Restoration does not generate concurrency tokens.
