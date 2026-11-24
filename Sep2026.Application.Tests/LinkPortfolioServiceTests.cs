using Sep2026.Application.DTOs;
using Sep2026.Application.Exceptions;
using Sep2026.Application.Interfaces;
using Sep2026.Application.Security;
using Sep2026.Application.Services;
using Sep2026.Domain;
using Xunit;

namespace Sep2026.Application.Tests;

public sealed class LinkPortfolioServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
    private static readonly LinkPortfolioRequest Request = new(InvestmentProvider.Alpaca, ProviderEnvironment.Paper, "  Long term  ");
    private static readonly ProviderCredentials Authorization = new("private-key-id", " exact secret ");

    [Fact]
    public async Task LinksAcceptedDomainStateWithFractionalPositionsAndUnknownCost()
    {
        var fixture = new Fixture();
        var result = await fixture.Service.LinkAsync(fixture.OwnerId, Request, Authorization);

        Assert.Equal(LinkPortfolioOutcome.Linked, result.Outcome);
        var import = Assert.IsType<InitialPortfolioImport>(fixture.Import);
        Assert.NotEqual(Guid.Empty, import.PortfolioId);
        Assert.NotEqual(Guid.Empty, import.ConnectionId);
        Assert.Equal(fixture.OwnerId, import.OwnerId);
        Assert.Equal("Long term", import.DisplayName);
        Assert.Equal(CurrencyType.USD, import.Snapshot.Currency);
        var position = Assert.Single(import.Snapshot.Positions);
        Assert.Equal(1.123456789m, position.Quantity);
        Assert.Null(position.AverageCost);
        Assert.Equal("AAPL", position.Symbol);
        Assert.Equal("Stock", position.SecurityType);
        Assert.Equal(import.PortfolioId, result.Portfolio.PortfolioId);
        Assert.Equal(import.CredentialReference, fixture.SavedCredential);
        Assert.Same(Authorization, fixture.ReceivedAuthorization);
        Assert.Empty(fixture.DeletedCredentials);
        Assert.Equal(1, fixture.Lock.DisposeCount);
        Assert.DoesNotContain(Authorization.SecretKey, result.ToString());
        Assert.DoesNotContain(Authorization.KeyId, result.ToString());
    }

    [Fact]
    public async Task EmptyPositionsAndActualZeroCashAreValidAndBlankNameBecomesNull()
    {
        var fixture = new Fixture();
        fixture.Snapshot = fixture.Snapshot with { CashBalance = 0, Positions = Array.Empty<ExternalPositionDto>() };
        await fixture.Service.LinkAsync(fixture.OwnerId, Request with { DisplayName = " " }, Authorization);
        Assert.Equal(0, fixture.Import!.Snapshot.CashBalance);
        Assert.Empty(fixture.Import.Snapshot.Positions);
        Assert.Null(fixture.Import.DisplayName);
    }

    [Fact]
    public async Task ReportedZeroCostIsNotChangedToUnknown()
    {
        var fixture = new Fixture();
        fixture.Snapshot = WithPosition(fixture, position => position with { AverageCost = 0 });
        await fixture.Service.LinkAsync(fixture.OwnerId, Request, Authorization);
        Assert.Equal(0m, Assert.Single(fixture.Import!.Snapshot.Positions).AverageCost);
    }

    [Fact]
    public async Task CopiesPositionsBeforePassingThemToPersistence()
    {
        var fixture = new Fixture();
        var source = fixture.Snapshot.Positions.ToList();
        fixture.Snapshot = fixture.Snapshot with { Positions = source };
        await fixture.Service.LinkAsync(fixture.OwnerId, Request, Authorization);
        source.Clear();
        Assert.Single(fixture.Import!.Snapshot.Positions);
        Assert.Throws<NotSupportedException>(() => ((IList<PositionSnapshot>)fixture.Import.Snapshot.Positions).Clear());
    }

    [Fact]
    public async Task DoesNotReportLinkedBeforeTheStoreConfirmsItsCommit()
    {
        var fixture = new Fixture();
        var completion = new TaskCompletionSource<LinkPortfolioResultDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Create = (_, _, _) => completion.Task;
        var linking = fixture.Service.LinkAsync(fixture.OwnerId, Request, Authorization);
        Assert.False(linking.IsCompleted);
        Assert.Equal(0, fixture.Lock.DisposeCount);
        completion.SetResult(new LinkPortfolioResultDto(LinkPortfolioOutcome.Linked, fixture.Existing(fixture.Import!.PortfolioId)));
        var result = await linking.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(LinkPortfolioOutcome.Linked, result.Outcome);
        Assert.Equal(1, fixture.Lock.DisposeCount);
    }

    [Fact]
    public async Task ProviderReadReceivesTheLinkedCallerAndLockCancellationToken()
    {
        var fixture = new Fixture();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Read = async token =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return fixture.Snapshot;
        };
        var linking = fixture.Service.LinkAsync(fixture.OwnerId, Request, Authorization);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        fixture.Lock.Lost.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => linking.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(Guid.Empty, fixture.SavedCredential);
        Assert.Null(fixture.Import);
        Assert.Equal(1, fixture.Lock.DisposeCount);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task DuplicateLinkReturnsTheExistingPortfolioWithoutReadingOrSavingFinancialState(int foundOnLookup)
    {
        var fixture = new Fixture();
        var existing = fixture.Existing();
        fixture.Find = (_, _) => Task.FromResult(fixture.FindCount >= foundOnLookup ? existing : null);
        var result = await fixture.Service.LinkAsync(fixture.OwnerId, Request, Authorization);
        Assert.Equal(LinkPortfolioOutcome.AlreadyLinked, result.Outcome);
        Assert.Same(existing, result.Portfolio);
        Assert.False(fixture.SnapshotRead);
        Assert.Null(fixture.Import);
        Assert.Equal(Guid.Empty, fixture.SavedCredential);
        Assert.Empty(fixture.DeletedCredentials);
    }

    [Fact]
    public async Task DuplicateDetectedAtAtomicCreateCleansOnlyItsNewCredential()
    {
        var fixture = new Fixture();
        var existing = fixture.Existing();
        fixture.Create = (_, _, _) => Task.FromResult(new LinkPortfolioResultDto(LinkPortfolioOutcome.AlreadyLinked, existing));
        var result = await fixture.Service.LinkAsync(fixture.OwnerId, Request, Authorization);
        Assert.Equal(existing, result.Portfolio);
        Assert.Equal(fixture.SavedCredential, Assert.Single(fixture.DeletedCredentials));
    }

    [Fact]
    public async Task RejectsUnknownOwnerBeforeCallingTheProvider()
    {
        var fixture = new Fixture { OwnerExists = false };
        await Assert.ThrowsAsync<KeyNotFoundException>(() => fixture.Service.LinkAsync(fixture.OwnerId, Request, Authorization));
        Assert.False(fixture.AccountVerified);
        Assert.Null(fixture.Import);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task InvalidInputDoesNotAccessAnyDependency(int invalidInput)
    {
        var fixture = new Fixture();
        var request = invalidInput switch
        {
            1 => Request with { Provider = (InvestmentProvider)99 },
            2 => Request with { Environment = (ProviderEnvironment)99 },
            3 => Request with { Environment = ProviderEnvironment.Live },
            4 => Request with { DisplayName = new string('n', 201) },
            _ => Request
        };
        await Assert.ThrowsAnyAsync<Exception>(() => fixture.Service.LinkAsync(
            invalidInput == 0 ? Guid.Empty : fixture.OwnerId,
            invalidInput == 5 ? null! : request, Authorization));
        Assert.Equal(0, fixture.OwnerChecks);
    }

    [Fact]
    public async Task NullCredentialsAndPreCancellationDoNotAccessDependencies()
    {
        var fixture = new Fixture();
        await Assert.ThrowsAsync<ArgumentNullException>(() => fixture.Service.LinkAsync(fixture.OwnerId, Request, null!));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Service.LinkAsync(fixture.OwnerId, Request, Authorization, new CancellationToken(true)));
        Assert.Equal(0, fixture.OwnerChecks);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" account ")]
    public async Task InvalidVerifiedIdentityCannotReachPersistence(string accountId)
    {
        var fixture = new Fixture { AccountId = accountId };
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Service.LinkAsync(fixture.OwnerId, Request, Authorization));
        Assert.Equal(0, fixture.FindCount);
        Assert.Equal(Guid.Empty, fixture.SavedCredential);
    }

    [Fact]
    public async Task ProviderFailureBeforeSnapshotCannotStoreCredentials()
    {
        var fixture = new Fixture();
        var failure = new TimeoutException("provider timeout");
        fixture.Read = _ => Task.FromException<ExternalPortfolioSnapshotDto>(failure);
        Assert.Same(failure, await Assert.ThrowsAsync<TimeoutException>(() => fixture.Service.LinkAsync(fixture.OwnerId, Request, Authorization)));
        Assert.Equal(Guid.Empty, fixture.SavedCredential);
        Assert.Equal(1, fixture.Lock.DisposeCount);
    }

    [Theory]
    [InlineData("incomplete")]
    [InlineData("wrong-account")]
    [InlineData("null-positions")]
    [InlineData("null-position")]
    [InlineData("stale")]
    [InlineData("future")]
    [InlineData("reversed-times")]
    [InlineData("missing-time")]
    [InlineData("missing-observed-time")]
    [InlineData("duplicate-asset")]
    [InlineData("duplicate-symbol")]
    public async Task InvalidSnapshotsLeaveCredentialsAndDatabaseUntouched(string scenario)
    {
        var fixture = new Fixture();
        var snapshot = fixture.Snapshot;
        var position = Assert.Single(snapshot.Positions);
        fixture.Snapshot = scenario switch
        {
            "incomplete" => snapshot with { IsComplete = false },
            "wrong-account" => snapshot with { ExternalAccountId = "other" },
            "null-positions" => snapshot with { Positions = null! },
            "null-position" => snapshot with { Positions = new ExternalPositionDto[] { null! } },
            "stale" => snapshot with { FetchStartedAt = Now.AddMinutes(-3) },
            "future" => snapshot with { FetchCompletedAt = Now.AddSeconds(1) },
            "reversed-times" => snapshot with { FetchCompletedAt = Now.AddSeconds(-1) },
            "missing-time" => snapshot with { FetchStartedAt = default },
            "missing-observed-time" => snapshot with { ProviderObservedAt = default(DateTimeOffset) },
            "duplicate-asset" => snapshot with { Positions = new[] { position, position with { Symbol = "MSFT" } } },
            "duplicate-symbol" => snapshot with { Positions = new[] { position, position with { ExternalAssetId = "asset2" } } },
            _ => throw new InvalidOperationException()
        };
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Service.LinkAsync(fixture.OwnerId, Request, Authorization));
        Assert.Equal(Guid.Empty, fixture.SavedCredential);
        Assert.Null(fixture.Import);
        Assert.Equal(1, fixture.Lock.DisposeCount);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("currency")]
    [InlineData("position-currency")]
    [InlineData("asset-class")]
    [InlineData("negative-cash")]
    [InlineData("cash-precision")]
    [InlineData("zero-quantity")]
    [InlineData("negative-quantity")]
    [InlineData("quantity-precision")]
    [InlineData("negative-cost")]
    [InlineData("cost-precision")]
    [InlineData("quantity-range")]
    [InlineData("cash-range")]
    public async Task UnsupportedFinancialStateIsNeverPartiallyImported(string scenario)
    {
        var fixture = new Fixture();
        fixture.Snapshot = scenario switch
        {
            "currency" => fixture.Snapshot with { Currency = "GBP" },
            "negative-cash" => fixture.Snapshot with { CashBalance = -1m },
            "cash-precision" => fixture.Snapshot with { CashBalance = 0.123456789m },
            "cash-range" => fixture.Snapshot with { CashBalance = 100000000000m },
            _ => WithPosition(fixture, position => scenario switch
            {
                "short" => position with { Side = ExternalPositionSide.Short },
                "position-currency" => position with { Currency = "EUR" },
                "asset-class" => position with { AssetClass = "crypto" },
                "zero-quantity" => position with { Quantity = 0 },
                "negative-quantity" => position with { Quantity = -1m },
                "quantity-precision" => position with { Quantity = 0.1234567891m },
                "quantity-range" => position with { Quantity = 10000000000000000000m },
                "negative-cost" => position with { AverageCost = -1m },
                "cost-precision" => position with { AverageCost = 0.123456789m },
                _ => throw new InvalidOperationException()
            })
        };
        await Assert.ThrowsAsync<NotSupportedException>(() => fixture.Service.LinkAsync(fixture.OwnerId, Request, Authorization));
        Assert.Equal(Guid.Empty, fixture.SavedCredential);
        Assert.Null(fixture.Import);
    }

    [Fact]
    public async Task WrongOwnerStoreResultIsRejectedWithoutLeakingItsPortfolio()
    {
        var fixture = new Fixture();
        fixture.Find = (_, _) => Task.FromResult<LinkedPortfolioDto?>(fixture.Existing() with { OwnerId = Guid.NewGuid() });
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Service.LinkAsync(fixture.OwnerId, Request, Authorization));
        Assert.False(fixture.SnapshotRead);
    }

    [Fact]
    public async Task ConfirmedRollbackCleansNewCredentialsAndPreservesTheFailure()
    {
        var fixture = new Fixture();
        var failure = new PortfolioLinkNotCommittedException("rolled back");
        fixture.Create = (_, _, _) => Task.FromException<LinkPortfolioResultDto>(failure);
        Assert.Same(failure, await Assert.ThrowsAsync<PortfolioLinkNotCommittedException>(() => fixture.Service.LinkAsync(fixture.OwnerId, Request, Authorization)));
        Assert.Equal(fixture.SavedCredential, Assert.Single(fixture.DeletedCredentials));
    }

    [Fact]
    public async Task InterruptedCredentialSaveCleansItsKnownReferenceWithoutStartingDatabaseSave()
    {
        var fixture = new Fixture();
        fixture.SaveCredential = _ => Task.FromException(new IOException("protected store failed"));
        await Assert.ThrowsAsync<IOException>(() => fixture.Service.LinkAsync(fixture.OwnerId, Request, Authorization));
        Assert.Equal(fixture.SavedCredential, Assert.Single(fixture.DeletedCredentials));
        Assert.Null(fixture.Import);
    }

    [Fact]
    public async Task CleanupFailurePreservesTheOriginalFailureToo()
    {
        var fixture = new Fixture();
        var rollback = new PortfolioLinkNotCommittedException("rolled back");
        var cleanup = new IOException("cleanup failed");
        fixture.Create = (_, _, _) => Task.FromException<LinkPortfolioResultDto>(rollback);
        fixture.DeleteCredential = _ => Task.FromException(cleanup);
        var failure = await Assert.ThrowsAsync<AggregateException>(() => fixture.Service.LinkAsync(fixture.OwnerId, Request, Authorization));
        Assert.Contains(rollback, failure.InnerExceptions);
        Assert.Contains(cleanup, failure.InnerExceptions);
    }

    [Fact]
    public async Task UnknownCommitKeepsCredentialsWhenRecoveryCannotFindAConfirmedLink()
    {
        var fixture = new Fixture();
        var failure = new TimeoutException("save response lost");
        fixture.Create = (_, _, _) => Task.FromException<LinkPortfolioResultDto>(failure);
        var unknown = await Assert.ThrowsAsync<PortfolioLinkCommitUnknownException>(() => fixture.Service.LinkAsync(fixture.OwnerId, Request, Authorization));
        Assert.Equal(fixture.Import!.PortfolioId, unknown.AttemptedPortfolioId);
        Assert.Same(failure, unknown.InnerException);
        Assert.Empty(fixture.DeletedCredentials);
        Assert.Equal(3, fixture.FindCount);
    }

    [Fact]
    public async Task UnknownCommitRecoveryFailureAlsoKeepsCredentials()
    {
        var fixture = new Fixture();
        fixture.Create = (_, _, _) => Task.FromException<LinkPortfolioResultDto>(new TimeoutException());
        fixture.Find = (_, _) => fixture.FindCount > 2
            ? Task.FromException<LinkedPortfolioDto?>(new IOException("recovery failed"))
            : Task.FromResult<LinkedPortfolioDto?>(null);
        await Assert.ThrowsAsync<PortfolioLinkCommitUnknownException>(() => fixture.Service.LinkAsync(fixture.OwnerId, Request, Authorization));
        Assert.Empty(fixture.DeletedCredentials);
    }

    [Fact]
    public async Task LostSaveResponseCanBeReconciledToTheCommittedPortfolio()
    {
        var fixture = new Fixture();
        fixture.Create = (import, _, _) =>
        {
            fixture.Find = (_, _) => Task.FromResult<LinkedPortfolioDto?>(fixture.Existing(import.PortfolioId));
            return Task.FromException<LinkPortfolioResultDto>(new TimeoutException());
        };
        var result = await fixture.Service.LinkAsync(fixture.OwnerId, Request, Authorization);
        Assert.Equal(LinkPortfolioOutcome.Linked, result.Outcome);
        Assert.Equal(fixture.Import!.PortfolioId, result.Portfolio.PortfolioId);
        Assert.Empty(fixture.DeletedCredentials);
    }

    [Fact]
    public async Task CancellationDuringSaveUsesIndependentRecoveryAndNeverDeletesPossibleCommittedCredentials()
    {
        var fixture = new Fixture();
        using var caller = new CancellationTokenSource();
        fixture.Create = (_, _, _) =>
        {
            caller.Cancel();
            fixture.Find = (_, recovery) =>
            {
                Assert.False(recovery.IsCancellationRequested);
                return Task.FromResult<LinkedPortfolioDto?>(fixture.Existing(fixture.Import!.PortfolioId));
            };
            return Task.FromException<LinkPortfolioResultDto>(new OperationCanceledException(caller.Token));
        };
        var result = await fixture.Service.LinkAsync(fixture.OwnerId, Request, Authorization, caller.Token);
        Assert.Equal(LinkPortfolioOutcome.Linked, result.Outcome);
        Assert.Empty(fixture.DeletedCredentials);
    }

    [Fact]
    public async Task CallerCancellationAfterConfirmedCommitDoesNotTurnSuccessIntoFailure()
    {
        var fixture = new Fixture();
        using var caller = new CancellationTokenSource();
        fixture.Create = (import, _, _) =>
        {
            caller.Cancel();
            return Task.FromResult(new LinkPortfolioResultDto(LinkPortfolioOutcome.Linked, fixture.Existing(import.PortfolioId)));
        };
        Assert.Equal(LinkPortfolioOutcome.Linked, (await fixture.Service.LinkAsync(fixture.OwnerId, Request, Authorization, caller.Token)).Outcome);
        Assert.Empty(fixture.DeletedCredentials);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationBeforeDatabaseSaveCleansStagedCredentialsWithAnIndependentToken(bool loseLock)
    {
        var fixture = new Fixture();
        using var caller = new CancellationTokenSource();
        fixture.SaveCredential = token =>
        {
            Assert.True(token.CanBeCanceled);
            if (loseLock) fixture.Lock.Lost.Cancel(); else caller.Cancel();
            return Task.CompletedTask;
        };
        fixture.DeleteCredential = token => { Assert.False(token.IsCancellationRequested); return Task.CompletedTask; };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Service.LinkAsync(fixture.OwnerId, Request, Authorization, caller.Token));
        Assert.Null(fixture.Import);
        Assert.Equal(fixture.SavedCredential, Assert.Single(fixture.DeletedCredentials));
        Assert.Equal(1, fixture.Lock.DisposeCount);
    }

    [Fact]
    public async Task LostLockStopsWorkBeforePositionsOrCredentials()
    {
        var fixture = new Fixture();
        fixture.Lock.Lost.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Service.LinkAsync(fixture.OwnerId, Request, Authorization));
        Assert.False(fixture.SnapshotRead);
        Assert.Equal(Guid.Empty, fixture.SavedCredential);
    }

    [Fact]
    public void CredentialsAreRedactedAndAreNotSilentlyTrimmed()
    {
        Assert.Equal(" exact secret ", Authorization.SecretKey);
        Assert.DoesNotContain(Authorization.KeyId, Authorization.ToString());
        Assert.DoesNotContain(Authorization.SecretKey, Authorization.ToString());
        Assert.Throws<ArgumentException>(() => new ProviderCredentials(" ", "secret"));
        Assert.Throws<ArgumentException>(() => new ProviderCredentials("id", " "));
    }

    [Fact]
    public void DependenciesAreRequired()
    {
        var fixture = new Fixture();
        Assert.Throws<ArgumentNullException>(() => new LinkPortfolioService(null!, fixture, fixture));
        Assert.Throws<ArgumentNullException>(() => new LinkPortfolioService(fixture, null!, fixture));
        Assert.Throws<ArgumentNullException>(() => new LinkPortfolioService(fixture, fixture, null!));
    }

    private static ExternalPortfolioSnapshotDto WithPosition(Fixture fixture, Func<ExternalPositionDto, ExternalPositionDto> change)
        => fixture.Snapshot with { Positions = new[] { change(Assert.Single(fixture.Snapshot.Positions)) } };

    private sealed class Fixture : IInvestmentAccountProvider, ILinkedPortfolioStore, IProviderCredentialStore
    {
        public Guid OwnerId { get; } = Guid.NewGuid();
        public string AccountId { get; set; } = "external-account/ABC";
        public bool OwnerExists { get; set; } = true;
        public int OwnerChecks { get; private set; }
        public int FindCount { get; private set; }
        public bool AccountVerified { get; private set; }
        public bool SnapshotRead { get; private set; }
        public InitialPortfolioImport? Import { get; private set; }
        public Guid SavedCredential { get; private set; }
        public ProviderCredentials? ReceivedAuthorization { get; private set; }
        public List<Guid> DeletedCredentials { get; } = new();
        public FakeLock Lock { get; } = new();
        public ExternalPortfolioSnapshotDto Snapshot { get; set; } = new("external-account/ABC", " usd ", 100m,
            new[] { new ExternalPositionDto("asset-id", " aapl ", " US_EQUITY ", " usd ", ExternalPositionSide.Long, 1.123456789m, null) }, Now, Now, true);
        public Func<string, CancellationToken, Task<LinkedPortfolioDto?>>? Find { get; set; }
        public Func<CancellationToken, Task<ExternalPortfolioSnapshotDto>>? Read { get; set; }
        public Func<InitialPortfolioImport, IPortfolioLinkLock, CancellationToken, Task<LinkPortfolioResultDto>>? Create { get; set; }
        public Func<CancellationToken, Task>? SaveCredential { get; set; }
        public Func<CancellationToken, Task>? DeleteCredential { get; set; }
        public LinkPortfolioService Service => new(this, this, this, new FixedClock());

        public LinkedPortfolioDto Existing(Guid? portfolioId = null) => new(portfolioId ?? Guid.NewGuid(), OwnerId,
            InvestmentProvider.Alpaca, ProviderEnvironment.Paper, AccountId, "Existing name", Now);

        public Task<bool> OwnerExistsAsync(Guid ownerId, CancellationToken cancellationToken = default)
        {
            Assert.Equal(OwnerId, ownerId);
            OwnerChecks++;
            return Task.FromResult(OwnerExists);
        }

        public Task<string> GetAccountIdAsync(InvestmentProvider provider, ProviderEnvironment environment,
            ProviderCredentials credentials, CancellationToken cancellationToken = default)
        {
            Assert.Equal(InvestmentProvider.Alpaca, provider);
            Assert.Equal(ProviderEnvironment.Paper, environment);
            AccountVerified = true;
            return Task.FromResult(AccountId);
        }

        public Task<ExternalPortfolioSnapshotDto> ReadSnapshotAsync(InvestmentProvider provider, ProviderEnvironment environment,
            string externalAccountId, ProviderCredentials credentials, CancellationToken cancellationToken = default)
        {
            Assert.Equal(AccountId, externalAccountId);
            SnapshotRead = true;
            return Read?.Invoke(cancellationToken) ?? Task.FromResult(Snapshot);
        }

        public Task<LinkedPortfolioDto?> FindAsync(Guid ownerId, InvestmentProvider provider, ProviderEnvironment environment,
            string externalAccountId, CancellationToken cancellationToken = default)
        {
            Assert.Equal(OwnerId, ownerId);
            Assert.Equal(InvestmentProvider.Alpaca, provider);
            Assert.Equal(ProviderEnvironment.Paper, environment);
            Assert.Equal(AccountId, externalAccountId);
            FindCount++;
            return Find?.Invoke(externalAccountId, cancellationToken) ?? Task.FromResult<LinkedPortfolioDto?>(null);
        }

        public Task<IPortfolioLinkLock> AcquireLinkLockAsync(Guid ownerId, InvestmentProvider provider, ProviderEnvironment environment,
            string externalAccountId, CancellationToken cancellationToken = default)
            => Task.FromResult<IPortfolioLinkLock>(Lock);

        public Task<LinkPortfolioResultDto> CreateAsync(InitialPortfolioImport import, IPortfolioLinkLock linkLock,
            CancellationToken cancellationToken = default)
        {
            Assert.Same(Lock, linkLock);
            Assert.Equal(0, Lock.DisposeCount);
            Import = import;
            return Create?.Invoke(import, linkLock, cancellationToken)
                ?? Task.FromResult(new LinkPortfolioResultDto(LinkPortfolioOutcome.Linked, Existing(import.PortfolioId)));
        }

        public Task SaveAsync(Guid ownerId, Guid reference, ProviderCredentials credentials, CancellationToken cancellationToken = default)
        {
            Assert.Equal(OwnerId, ownerId);
            SavedCredential = reference;
            ReceivedAuthorization = credentials;
            return SaveCredential?.Invoke(cancellationToken) ?? Task.CompletedTask;
        }

        public Task DeleteAsync(Guid ownerId, Guid reference, CancellationToken cancellationToken = default)
        {
            Assert.Equal(OwnerId, ownerId);
            DeletedCredentials.Add(reference);
            return DeleteCredential?.Invoke(cancellationToken) ?? Task.CompletedTask;
        }
    }

    private sealed class FakeLock : IPortfolioLinkLock
    {
        public CancellationTokenSource Lost { get; } = new();
        public CancellationToken LostToken => Lost.Token;
        public int DisposeCount { get; private set; }
        public ValueTask DisposeAsync() { DisposeCount++; return ValueTask.CompletedTask; }
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
