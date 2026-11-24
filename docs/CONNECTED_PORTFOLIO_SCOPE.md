# Connected portfolio management: scope and feature direction

Updated 20 November 2026 following the project scope clarification.

This is the product direction and implementation proposal. The **Link Portfolio**
Application service/contracts are implemented with fake-based tests; production
provider/storage integration and synchronization remain planned. Existing profile
sign-in and saved portfolio browsing remain the current executable features.

## Project purpose

Gemino is a user's orchestration and analysis layer across external investment
providers. Users connect accounts they already hold elsewhere and view their
portfolios together in Gemino. Alpaca is the candidate for the first adapter;
the architecture should allow additional providers later.

In this scope, **create portfolio means connect an external investment account**.
Use **Link Portfolio** as the feature name in the future UI. The
[Link Portfolio implementation plan](LINK_PORTFOLIO_IMPLEMENTATION_PLAN.md)
describes the proposed first release. A successful connection
registers a local representation of the selected external account and imports
its financial state. It does not create a new account at the provider.

The first integration is proposed as read-only. Order submission, transfers,
manual deposits/withdrawals, and manual position entry are outside that first
release. A later expansion requires explicit workflows and provider capabilities.

## Ownership and sources of truth

| Information | Authority / meaning |
| --- | --- |
| Gemino user profile | Local owner identity and local email/password sign-in |
| Provider connection | Authorization to a provider for that Gemino owner |
| External account identity | Provider-issued identifier, scoped by provider and environment |
| Portfolio balance, positions, and account currency | External provider; Gemino persists the most recently accepted synchronized state |
| Cost basis and account values | Preserve the provider's meaning, availability, and timestamps; do not assume all providers calculate them identically |
| Display alias and local archive preference | Gemino preferences, independent of provider account status |
| Historical bars and latest market prices | Market-data source with explicit feed, timestamp, currency, and adjustment settings |
| Derived analytics | Gemino calculations identifying input timestamps, data coverage, and methodology |

A Gemino login does not authenticate an external account. Provider credentials or
tokens need their own authorization and storage lifecycle. Never send a Gemino
password to a provider or include provider secrets in portfolio DTOs.

The first mapping is one selected external account to one local portfolio,
where the provider supports that interpretation. A provider connection may expose
multiple accounts. Verify the first provider's account model before finalizing
the schema; some providers may expose subaccounts or other portfolio groupings.

## Link Portfolio user experience

1. Sign in to the local Gemino profile and choose Link Portfolio.
2. Select a supported provider and environment, such as paper or live where
   applicable. Keep those environments distinct throughout storage and display.
3. Complete the provider-supported authorization flow. Verify the mechanism and
   available read permissions before implementation; do not promise that every
   provider uses OAuth or that every credential can be restricted to read-only.
4. Resolve accessible accounts and let the user select one where there is a
   choice. Derive external identity from a verified provider response rather than
   trusting a typed account ID as proof of access.
5. Retrieve and validate account information and the complete positions result.
6. Save the local portfolio, external mapping, and accepted initial snapshot.
7. Show the portfolio with provider, environment, display alias, connection state,
   and last successful synchronization time.

A verified account with zero cash or no positions is valid when the provider
actually reports those values. Until the first import succeeds, show a pending
or failed connection rather than an apparently active portfolio with invented
zero balances. Local portfolio IDs remain internal stable identities.

## Separate connection, portfolio, and synchronization state

Proposed persisted responsibilities, to be designed through a versioned SQL
script before database-first scaffolding:

| Record / fields | Responsibility |
| --- | --- |
| Provider connection | Local owner, provider, environment, credential reference, authorization/connection state |
| External-account mapping | Connection, verified external account ID, local portfolio ID, provider-specific account metadata |
| Local portfolio and holdings | Last accepted financial snapshot and local display preferences |
| Synchronization metadata | Last attempt/success, provider observation timestamps if available, generation/version, outcome, sanitized error summary |

These are responsibilities, not a requirement for one table per row in the table
above. Finalize the smallest schema that preserves their distinct meanings.

Prevent duplicate links using an owner/provider/environment/external-account
identity constraint, including links attempted through different connections.
Keep account identifiers opaque rather than assuming a UUID format. Relinking
the same account should reuse its established local identity under a defined
reconnection policy. A failed connection attempt must not create duplicates.
Any future cross-owner linking policy needs its own explicit decision.

Store secrets in a suitable device credential store and persist references where
practical. Token refresh/revocation is provider-specific. The existing static
market-data configuration must not become a shared mutable credential slot for
different users' accounts. Authenticate each private request with its intended
connection and enforce owner checks in Application services, not only in the UI.

Keep local archived state separate from provider state and connectivity. Archiving
a local view is not disconnecting an account or closing it at the provider. The
archive/sync policy must be explicit; an archive alone must not silently revoke
credentials. Existing `PortfolioStatus` cannot describe all these states.

## Account synchronization and reconciliation

Account synchronization is its own use case, callable manually and, later, by a
background schedule. It does not require interactive provider prompts during
ordinary authorized refreshes; expired or revoked access produces an actionable
reauthorization state.

The synchronization sequence is:

1. Resolve an owner-authorized connection and acquire a per-account run lock.
2. Fetch account balances and all position pages outside a local SQL transaction.
3. Normalize provider fields, resolve security identities, and validate the
   complete candidate snapshot. Preserve provider timestamps and provenance.
4. In one short local transaction, reconcile cash, holdings, snapshot metadata,
   and portfolio concurrency state.
5. Publish the accepted snapshot and success timestamp only after commit.

Do not call `DepositCash`, `WithdrawCash`, `ApplyBuy`, or `ApplySell` to reconstruct
an external snapshot. A current position does not contain the complete execution
history needed to replay those operations correctly. Import validated state
through a dedicated mapping/reconciliation path.

Use original row versions or a synchronization generation and prevent a slower,
older run from overwriting newer accepted data. A local transaction makes the
local update atomic; it cannot make separate external API responses an atomic
provider snapshot. Use provider snapshot/version support if available, otherwise
define and expose a consistency policy, including bounded retries for mismatches.

Positions absent from an authoritative, complete successful positions response
may be reconciled as closed/removed according to the chosen retention policy.
An error, missing field, or incomplete page sequence must never be interpreted
as an empty account. Repeating a successful sync should preserve stable holding
and portfolio identities without duplicate rows.

On failure, retain the last successful snapshot, record the failure, and show
that data as stale. Connection authorization status and last-sync success remain
separate facts. Disconnecting stops future account access and releases/revokes
credentials where supported; retention of prior snapshots is a separate user
choice and must not trigger deletion implicitly.

## Model compatibility review before importing real accounts

The current Domain and SQL schema were designed around a simpler manually
managed portfolio. Reuse validated read/mapping code where appropriate, but
review these assumptions before accepting external data:

- `HoldingModel.ShareCount` and SQL `Holdings.ShareCount` use `int`; fractional
  positions need compatible decimal quantities across storage, Domain, DTOs,
  calculations, and rendering.
- Cash and quantities currently reject negative values; margin balances, shorts,
  and other provider account types need an explicit supported model or a clear
  unsupported-account error.
- Nonempty holdings currently require a positive average purchase price;
  unavailable cost basis needs a representation for unknown, rather than zero.
- Local portfolio type values and currency rules may not match every provider.
  Preserve provider metadata and use an explicit mapping; do not guess Individual
  or USD for an unknown account type/currency.
- `Securities.TickerSymbol` is currently globally unique. Broader instruments,
  venues, currencies, and symbol changes need stable provider/instrument identity
  mapping, not a ticker-only match.
- Existing portfolio `CreatedAt` represents local creation. Record connection,
  import, and provider account-creation times separately when needed.
- Portfolio archive behavior, connection status, and provider account status need
  distinct meanings; imported provider state must not be blocked merely because
  a local view is archived.
- Portfolio Domain methods currently print to the console. Presentation belongs
  in the host when these workflows are revised.

Until the necessary model changes exist, reject unsupported snapshots explicitly
and retain previous data. Never round fractional positions, clamp negative cash,
or fabricate missing costs just to satisfy existing constructors.

## Account data and historical market data are separate pipelines

| Pipeline | Inputs | Updates | Typical authorization |
| --- | --- | --- | --- |
| Private account sync | External account balances, positions, and metadata | That owner's connected portfolio snapshot and sync state | Connection-specific authorization |
| Market-data ETL | Historical OHLCV bars | Shared, source-identified market history | Configured market-data access |
| Analytics/query services | Accepted account snapshots plus compatible prices/history | Derived read DTOs or explicitly designed analytical snapshots | Gemino owner access for private account inputs |

The [historical market-data ETL plan](HISTORICAL_MARKET_DATA_ETL.md) remains valid
as a separate feature. Security references may be shared where instrument
identity matches. Market-price collection must never change account cash or
position quantities. Account sync must not treat a market-data request as
authorization to access a private investment account.

Market history can support charts and security returns. Actual portfolio
performance, realized profit/loss, and cash-flow-adjusted returns require
appropriate account history/executions/cash flows and methodology; current
positions plus OHLCV bars alone are insufficient.

## Updated implementation order and completion criteria

1. Define the first provider's account model, supported positions/currencies, and
   authorization mechanism. Start with one provider and a read-only adapter.
2. Finalize provider connection and external-account identity contracts, then
   the versioned SQL changes and database-first mappings. Reconcile any existing
   unlinked portfolios explicitly rather than assigning a provider by guesswork.
3. Implement provider account/position reads and validate a candidate snapshot
   with fake provider responses before enabling real persistence.
4. Implement Link Portfolio with duplicate protection, initial
   snapshot persistence, and owner isolation.
5. Implement manual refresh, atomic reconciliation, stale-state display,
   reconnection, and disconnect behavior.
6. Add scheduled account refresh after manual operation is reliable. Build the
   independent historical ETL feature according to its own plan.
7. Add combined views and analytics with explicit source and freshness metadata.

The detailed plan proposes `LinkPortfolioService` for linking/initial import and
`PortfolioSyncService` for refresh. Reauthorization and disconnect are follow-up
lifecycle operations. Retain `PortfolioService` / query services for reads.
Introduce contracts with their first implementation rather than adding empty
interfaces now. Infrastructure adapters own provider-specific HTTP details and
secret access; persistence owns transactional reconciliation.

Acceptance checks must cover wrong-owner access, duplicate links, account
environment separation, incomplete position responses, expired authorization,
unsupported fractional/negative/unknown-cost data, concurrent/out-of-order sync,
rollback after a failed local write, and recovery without losing the last good
snapshot. A local display alias must not affect external account identity.

This scope document defines the product direction. The Application service now
implements the first workflow against contracts/fakes; production provider
authorization, schema changes, persistence, presentation, and scheduled jobs remain
future work. See the Application guide and detailed plan for implementation status.
