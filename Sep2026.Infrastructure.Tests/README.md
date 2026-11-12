# Infrastructure tests

Run mapper and input-validation tests from the solution directory:

```powershell
dotnet test Sep2026.Infrastructure.Tests/Sep2026.Infrastructure.Tests.csproj
```

SQL Server integration tests are skipped until `GEMINO_TEST_SQLSERVER` is supplied.
It is a connection string to a development/test SQL Server instance, using a login
that can create and remove a test database. For a local SQL Express installation:

```powershell
dotnet test Sep2026.Infrastructure.Tests/Sep2026.Infrastructure.Tests.csproj --environment 'GEMINO_TEST_SQLSERVER=Server=.\SQLEXPRESS;Database=master;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;Connect Timeout=5;'
```

This environment setting applies to the test process. The fixture creates a
randomly named `GeminoRepositoryTests_<guid>` database and runs a copy of the schema
bootstrap with that name. It inserts its own test rows, uses nonpooled connections,
and removes only its generated test database afterward. The configured initial
catalog is replaced with `master` for setup. It never reads or writes `Gemino` data.
The example certificate-trust setting is for local development.

Mapper tests check preserved identity, enums and currency, optional quote metadata,
empty holdings, invalid saved state, and missing or mismatched relationships.
Argument tests verify rejection of empty IDs and pre-canceled reads before a
database connection opens. SQL Server tests verify actual relationship loading,
decimal precision, missing and empty portfolios, and that changing a returned
Domain snapshot cannot save database changes through this read-only repository.

Portfolio summary query tests additionally verify user isolation, inclusion of
active and archived portfolios, descending dates and ascending ID tie-breaks,
empty results, and selection followed by a complete detail read. SQL command
capture verifies that the summary operation reads only scalar Portfolio columns
without joins or relationship loads.
