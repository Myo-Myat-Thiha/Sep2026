using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Sep2026.Application.DTOs;
using Sep2026.Application.Exceptions;
using Sep2026.Application.Interfaces;
using Sep2026.Domain;
using Sep2026.Infrastructure.Persistence.Entities;
using DbPortfolio = Sep2026.Infrastructure.Persistence.Entities.Portfolio;

namespace Sep2026.Infrastructure.Persistence.Stores;

public sealed class SqlServerLinkedPortfolioStore : ILinkedPortfolioStore
{
    private readonly string connectionString;
    private readonly TimeProvider clock;

    public SqlServerLinkedPortfolioStore(GeminoDbContext context, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        connectionString = context.Database.GetConnectionString() ?? throw new ArgumentException("A SQL connection is required.", nameof(context));
        this.clock = clock ?? TimeProvider.System;
    }

    // Independent contexts keep failed transactions/tracking out of recovery reads.
    private GeminoDbContext ReadContext() => new(new DbContextOptionsBuilder<GeminoDbContext>().UseSqlServer(connectionString).Options);

    public async Task<bool> OwnerExistsAsync(Guid ownerId, CancellationToken cancellationToken = default)
    {
        await using var context = ReadContext();
        return await context.Users.AnyAsync(user => user.UserId == ownerId, cancellationToken);
    }

    public async Task<LinkedPortfolioDto?> FindAsync(Guid ownerId, InvestmentProvider provider,
        ProviderEnvironment environment, string externalAccountId, CancellationToken cancellationToken = default)
    {
        await using var context = ReadContext();
        var row = await context.ProviderConnections.AsNoTracking()
            .Where(link => link.OwnerId == ownerId && link.Provider == (byte)provider &&
                link.Environment == (byte)environment && link.ExternalAccountId == externalAccountId)
            .Select(link => new { link.PortfolioId, link.OwnerId, link.ExternalAccountId,
                link.Portfolio.DisplayName, link.LastSuccessfulSyncAt })
            .SingleOrDefaultAsync(cancellationToken);
        return row is null ? null : new LinkedPortfolioDto(row.PortfolioId, row.OwnerId, provider, environment,
            row.ExternalAccountId, row.DisplayName, row.LastSuccessfulSyncAt);
    }

    public async Task<IPortfolioLinkLock> AcquireLinkLockAsync(Guid ownerId, InvestmentProvider provider,
        ProviderEnvironment environment, string externalAccountId, CancellationToken cancellationToken = default)
    {
        if (ownerId == Guid.Empty) throw new ArgumentException("An owner ID is required.", nameof(ownerId));
        ArgumentException.ThrowIfNullOrWhiteSpace(externalAccountId);
        var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(externalAccountId)));
        var resource = $"Gemino:Link:{ownerId:N}:{(int)provider}:{(int)environment}:{identity}";
        var connection = new SqlConnection(new SqlConnectionStringBuilder(connectionString)
        { Pooling = false, ConnectRetryCount = 0 }.ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "DECLARE @result int; EXEC @result = sys.sp_getapplock @Resource=@resource, @LockMode='Exclusive', @LockOwner='Session', @LockTimeout=0; SELECT @result;";
            command.Parameters.AddWithValue("@resource", resource);
            if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) < 0)
                throw new InvalidOperationException("This account is being linked or synchronized by another operation. Try again shortly.");
            return new AccountLock(this, connection, resource, ownerId, provider, environment, externalAccountId);
        }
        catch { await connection.DisposeAsync(); throw; }
    }

    public async Task<LinkPortfolioResultDto> CreateAsync(InitialPortfolioImport import,
        IPortfolioLinkLock linkLock, CancellationToken cancellationToken = default)
    {
        // A definite failure before commit is signaled distinctly so Application
        // can safely compensate its newly staged credential.
        if (linkLock is not AccountLock accountLock || !accountLock.Matches(this, import))
            throw new PortfolioLinkNotCommittedException("The import does not own its external account lock.");
        var committing = false;
        var confirmed = false;
        await using var context = new GeminoDbContext(new DbContextOptionsBuilder<GeminoDbContext>()
            .UseSqlServer(accountLock.Connection).Options);
        IDbContextTransaction? transaction = null;
        try
        {
            accountLock.EnsureActive();
            using var work = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, accountLock.LostToken);
            var token = work.Token;
            if (import.PortfolioId == Guid.Empty || import.ConnectionId == Guid.Empty || import.CredentialReference == Guid.Empty ||
                import.Provider != InvestmentProvider.Alpaca || import.Environment != ProviderEnvironment.Paper || import.Snapshot.Currency != CurrencyType.USD)
                throw new InvalidDataException("The initial import has unsupported identity or account settings.");
            transaction = await context.Database.BeginTransactionAsync(token);
            // Initial imports are small. Serialize their shared instrument registry
            // writes inside this short transaction; HTTP never runs under this lock.
            await context.Database.ExecuteSqlRawAsync("DECLARE @result int; EXEC @result=sys.sp_getapplock @Resource=N'Gemino:InitialImport', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000; IF @result < 0 THROW 50010, 'The import writer is busy.', 1;", token);
            var duplicate = await context.ProviderConnections.Include(link => link.Portfolio).SingleOrDefaultAsync(link =>
                link.OwnerId == import.OwnerId && link.Provider == (byte)import.Provider && link.Environment == (byte)import.Environment &&
                link.ExternalAccountId == import.Snapshot.ExternalAccountId, token);
            if (duplicate is not null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return new(LinkPortfolioOutcome.AlreadyLinked, new(duplicate.PortfolioId, duplicate.OwnerId, import.Provider,
                    import.Environment, duplicate.ExternalAccountId, duplicate.Portfolio.DisplayName, duplicate.LastSuccessfulSyncAt));
            }
            var now = clock.GetUtcNow();
            var portfolio = new DbPortfolio
            {
                PortfolioId = import.PortfolioId, OwnerId = import.OwnerId, DisplayName = import.DisplayName,
                CashBalance = import.Snapshot.CashBalance, CurrencyType = import.Snapshot.Currency.ToString(),
                PortfolioType = (byte)PortfolioType.Unspecified, Status = (byte)PortfolioStatus.Active, CreatedAt = now
            };
            foreach (var position in import.Snapshot.Positions.OrderBy(item => item.ExternalAssetId, StringComparer.Ordinal))
            {
                var security = await ResolveSecurityAsync(context, import, position, token);
                portfolio.Holdings.Add(new Holding
                {
                    HoldingId = Guid.NewGuid(), PortfolioId = import.PortfolioId, Security = security,
                    ShareCount = position.Quantity, AveragePurchasePrice = position.AverageCost
                });
            }
            context.Portfolios.Add(portfolio);
            context.ProviderConnections.Add(new ProviderConnection
            {
                ConnectionId = import.ConnectionId, OwnerId = import.OwnerId, Portfolio = portfolio,
                Provider = (byte)import.Provider, Environment = (byte)import.Environment,
                ExternalAccountId = import.Snapshot.ExternalAccountId, CredentialReference = import.CredentialReference,
                LastAttemptAt = now, LastSuccessfulSyncAt = now, FetchStartedAt = import.FetchStartedAt,
                FetchCompletedAt = import.FetchCompletedAt, ProviderObservedAt = import.ProviderObservedAt,
                ExternalAccountType = import.ExternalAccountType, SyncGeneration = 1
            });
            await context.SaveChangesAsync(token);
            await accountLock.VerifySqlOwnershipAsync((SqlTransaction)transaction.GetDbTransaction(), token);
            token.ThrowIfCancellationRequested();
            committing = true;
            await transaction.CommitAsync(token);
            confirmed = true;
            return new(LinkPortfolioOutcome.Linked, new(import.PortfolioId, import.OwnerId, import.Provider,
                import.Environment, import.Snapshot.ExternalAccountId, import.DisplayName, now));
        }
        catch (Exception exception) when (!committing)
        {
            if (transaction is not null)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try { await transaction.RollbackAsync(cleanup.Token); } catch { /* Closing the session also rolls back. No COMMIT was issued. */ }
            }
            throw new PortfolioLinkNotCommittedException("The portfolio import was not committed. Existing portfolios were preserved.", exception);
        }
        finally
        {
            if (transaction is not null)
            {
                try { await transaction.DisposeAsync(); }
                catch when (confirmed) { /* Do not turn confirmed success into an uncertain outcome. */ }
            }
        }
    }

    private static async Task<Entities.Security> ResolveSecurityAsync(GeminoDbContext context, InitialPortfolioImport import,
        PositionSnapshot position, CancellationToken token)
    {
        var mapping = await context.ProviderSecurityMappings.Include(item => item.Security).SingleOrDefaultAsync(item =>
            item.Provider == (byte)import.Provider && item.Environment == (byte)import.Environment && item.ExternalAssetId == position.ExternalAssetId, token);
        var security = mapping?.Security ?? await context.Securities.SingleOrDefaultAsync(item => item.TickerSymbol == position.Symbol, token);
        if (security is not null && (security.TickerSymbol != position.Symbol || security.CurrencyType != position.Currency.ToString() ||
            !string.Equals(security.SecurityType, position.SecurityType, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("The provider instrument conflicts with the saved security identity.");
        if (mapping is not null) return mapping.Security;
        if (security is null)
        {
            security = new Entities.Security { SecurityId = Guid.NewGuid(), TickerSymbol = position.Symbol,
                SecurityType = position.SecurityType, CurrencyType = position.Currency.ToString() };
            context.Securities.Add(security);
        }
        else if (await context.ProviderSecurityMappings.AnyAsync(item => item.Provider == (byte)import.Provider &&
            item.Environment == (byte)import.Environment && item.SecurityId == security.SecurityId, token))
            throw new InvalidDataException("The symbol is already mapped to a different provider instrument.");
        context.ProviderSecurityMappings.Add(new ProviderSecurityMapping
        {
            MappingId = Guid.NewGuid(), Provider = (byte)import.Provider, Environment = (byte)import.Environment,
            ExternalAssetId = position.ExternalAssetId, Security = security
        });
        return security;
    }

    private sealed class AccountLock : IPortfolioLinkLock
    {
        private readonly SqlServerLinkedPortfolioStore owner;
        private readonly string resource;
        private readonly Guid ownerId;
        private readonly InvestmentProvider provider;
        private readonly ProviderEnvironment environment;
        private readonly string accountId;
        private readonly CancellationTokenSource lost = new();
        private bool disposed;
        public SqlConnection Connection { get; }
        public CancellationToken LostToken => lost.Token;

        public AccountLock(SqlServerLinkedPortfolioStore owner, SqlConnection connection, string resource, Guid ownerId,
            InvestmentProvider provider, ProviderEnvironment environment, string accountId)
        {
            this.owner = owner; Connection = connection; this.resource = resource; this.ownerId = ownerId;
            this.provider = provider; this.environment = environment; this.accountId = accountId;
            Connection.StateChange += Changed;
        }

        private void Changed(object? sender, StateChangeEventArgs args)
        {
            if (!disposed && args.CurrentState is ConnectionState.Broken or ConnectionState.Closed) lost.Cancel();
        }

        public bool Matches(SqlServerLinkedPortfolioStore store, InitialPortfolioImport import)
            => !disposed && ReferenceEquals(owner, store) && ownerId == import.OwnerId && provider == import.Provider &&
                environment == import.Environment && accountId == import.Snapshot.ExternalAccountId;

        public void EnsureActive()
        {
            LostToken.ThrowIfCancellationRequested();
            if (disposed || Connection.State != ConnectionState.Open) throw new InvalidOperationException("The account lock was lost.");
        }

        public async Task VerifySqlOwnershipAsync(SqlTransaction transaction, CancellationToken token)
        {
            EnsureActive();
            await using var command = Connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT CASE WHEN APPLOCK_MODE('public', @resource, 'Session') = 'Exclusive' THEN 1 ELSE 0 END;";
            command.Parameters.AddWithValue("@resource", resource);
            if (Convert.ToInt32(await command.ExecuteScalarAsync(token)) != 1) throw new InvalidOperationException("The account lock was lost.");
        }

        public async ValueTask DisposeAsync()
        {
            if (disposed) return;
            disposed = true;
            Connection.StateChange -= Changed;
            // Non-pooled session disposal releases its locks even after network
            // failure. Never mask an already confirmed import with release errors.
            try { await Connection.DisposeAsync(); } catch { }
            lost.Dispose();
        }
    }
}
