using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Sep2026.Application.Exceptions;
using Sep2026.Application.DTOs;
using Sep2026.Application.Services;
using Sep2026.Infrastructure.Persistence;
using Sep2026.Infrastructure.Persistence.Entities;
using Sep2026.Infrastructure.Persistence.Queries;
using Sep2026.Infrastructure.Persistence.Stores;
using Xunit;

namespace Sep2026.Infrastructure.Tests;

public sealed class UserStoreArgumentTests
{
    [Fact]
    public void RequiresAContext() => Assert.Throws<ArgumentNullException>(() => new SqlServerUserStore(null!));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task RejectsMissingEmailsBeforeConnecting(string? email)
    {
        await using var context = CreateContext();
        await Assert.ThrowsAnyAsync<ArgumentException>(() => new SqlServerUserStore(context).EmailExistsAsync(email!));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RejectsMissingFieldsBeforeConnecting(bool missingName)
    {
        await using var context = CreateContext();
        await Assert.ThrowsAsync<ArgumentNullException>(() => new SqlServerUserStore(context)
            .CreateAsync(Guid.NewGuid(), missingName ? null! : "Alice", missingName ? "alice@example.com" : null!));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task RejectsAnEmptyIdentityBeforeConnecting()
    {
        await using var context = CreateContext();
        await Assert.ThrowsAsync<ArgumentException>(() => new SqlServerUserStore(context)
            .CreateAsync(Guid.Empty, "Alice", "alice@example.com"));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task PrecancelledOperationsDoNotStageDataOrConnect()
    {
        await using var context = CreateContext();
        var store = new SqlServerUserStore(context);
        var token = new CancellationToken(true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.EmailExistsAsync("alice@example.com", token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.CreateAsync(Guid.NewGuid(), "Alice", "alice@example.com", token));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task DoesNotCommitUnrelatedPendingWrites()
    {
        await using var context = CreateContext();
        var pending = new User { UserId = Guid.NewGuid(), Name = "Pending", Email = "pending@example.com" };
        context.Users.Add(pending);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new SqlServerUserStore(context).CreateAsync(Guid.NewGuid(), "Alice", "alice@example.com"));
        Assert.Same(pending, Assert.Single(context.ChangeTracker.Entries()).Entity);
        Assert.Equal(EntityState.Added, context.Entry(pending).State);
    }

    [Fact]
    public async Task SaveFailureDetachesOnlyTheAttemptedInsertionAndPreservesTheFailure()
    {
        var failure = new InvalidOperationException("save intercepted");
        await using var context = CreateContext(new SaveFailureInterceptor(failure));
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new SqlServerUserStore(context).CreateAsync(Guid.NewGuid(), "Alice", "alice@example.com")));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task CancellationAfterStagingReachesSaveAndLeavesNoPendingInsert()
    {
        using var cancellation = new CancellationTokenSource();
        var interceptor = new CancelSavingInterceptor(cancellation);
        await using var context = CreateContext(interceptor);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new SqlServerUserStore(context).CreateAsync(Guid.NewGuid(), "Alice", "alice@example.com", cancellation.Token));
        Assert.Equal(cancellation.Token, interceptor.ReceivedToken);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    private static GeminoDbContext CreateContext(params IInterceptor[] interceptors)
        => new(new DbContextOptionsBuilder<GeminoDbContext>()
            .UseSqlServer("Server=unused-test-server;Database=Gemino;Integrated Security=True;Connect Timeout=1")
            .AddInterceptors(interceptors).Options);

    private sealed class SaveFailureInterceptor(Exception failure) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
            => ValueTask.FromException<InterceptionResult<int>>(failure);
    }

    private sealed class CancelSavingInterceptor(CancellationTokenSource cancellation) : SaveChangesInterceptor
    {
        internal CancellationToken ReceivedToken { get; private set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            ReceivedToken = cancellationToken;
            cancellation.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(result);
        }
    }
}

public sealed class UserStoreSqlServerTests(SqlServerFixture fixture) : IClassFixture<SqlServerFixture>
{
    [SqlServerFact]
    public async Task ServicePersistsANormalizedProfileWithItsSuppliedIdentityAndNoPortfolios()
    {
        await using var context = fixture.CreateContext();
        var email = $"Alice+{Guid.NewGuid():N}@Example.COM";
        var result = await new UserService(new SqlServerUserStore(context))
            .CreateAsync("  Zoë O'Connor  ", $" {email} ");

        await using var verification = fixture.CreateContext();
        var saved = await verification.Users.AsNoTracking().SingleAsync(user => user.UserId == result.UserId);
        Assert.Equal("Zoë O'Connor", saved.Name);
        Assert.Equal(email, saved.Email);
        Assert.Equal(saved.Name, result.Name);
        Assert.Equal(saved.Email, result.Email);
        Assert.Empty(await new PortfolioQueries(verification).GetByUserIdAsync(result.UserId));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [SqlServerFact]
    public async Task EmailLookupIsCaseInsensitiveAndDoesNotLoadTheUserGraph()
    {
        await using var context = fixture.CreateContext();
        var store = new SqlServerUserStore(context);
        var data = NewUser();
        await store.CreateAsync(data.UserId, data.Name, data.Email);
        Assert.True(await store.EmailExistsAsync(data.Email.ToUpperInvariant()));
        Assert.True(await store.EmailExistsAsync(data.Email.ToLowerInvariant()));
        Assert.False(await store.EmailExistsAsync($"{Guid.NewGuid():N}@example.com"));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [SqlServerFact]
    public async Task DuplicateEmailIsTranslatedAndLeavesTheOriginalUnchanged()
    {
        await using var context = fixture.CreateContext();
        var store = new SqlServerUserStore(context);
        var first = NewUser();
        await store.CreateAsync(first.UserId, first.Name, first.Email);
        var second = new UserDto(Guid.NewGuid(), "Other owner", first.Email.ToUpperInvariant());
        var conflict = await Assert.ThrowsAsync<EmailAlreadyExistsException>(() => store.CreateAsync(second.UserId, second.Name, second.Email));
        Assert.IsType<DbUpdateException>(conflict.InnerException);
        Assert.Empty(context.ChangeTracker.Entries());
        await using var verification = fixture.CreateContext();
        var saved = await verification.Users.SingleAsync(user => user.UserId == first.UserId);
        Assert.Equal(first.Name, saved.Name);
        Assert.Equal(first.Email, saved.Email);
        Assert.False(await verification.Users.AnyAsync(user => user.UserId == second.UserId));
    }

    [SqlServerFact]
    public async Task DuplicateIdentityIsNotMislabeledAsAnEmailConflict()
    {
        await using var context = fixture.CreateContext();
        var store = new SqlServerUserStore(context);
        var first = NewUser();
        await store.CreateAsync(first.UserId, first.Name, first.Email);
        var second = NewUser() with { UserId = first.UserId };
        await Assert.ThrowsAsync<DbUpdateException>(() => store.CreateAsync(second.UserId, second.Name, second.Email));
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.False(await store.EmailExistsAsync(second.Email));
    }

    [SqlServerFact]
    public async Task ConcurrentAttemptsCanPassBothChecksButOnlyOneEmailIsInserted()
    {
        await using var firstContext = fixture.CreateContext();
        await using var secondContext = fixture.CreateContext();
        var firstStore = new SqlServerUserStore(firstContext);
        var secondStore = new SqlServerUserStore(secondContext);
        var first = NewUser();
        var second = first with { UserId = Guid.NewGuid(), Email = first.Email.ToUpperInvariant() };
        Assert.False(await firstStore.EmailExistsAsync(first.Email));
        Assert.False(await secondStore.EmailExistsAsync(second.Email));

        var outcomes = await Task.WhenAll(Attempt(firstStore, first), Attempt(secondStore, second));

        Assert.Single(outcomes, outcome => outcome is null);
        Assert.IsType<EmailAlreadyExistsException>(Assert.Single(outcomes, outcome => outcome is not null));
        await using var verification = fixture.CreateContext();
        Assert.Equal(1, await verification.Users.CountAsync(user => user.Email == first.Email));
        Assert.Empty(firstContext.ChangeTracker.Entries());
        Assert.Empty(secondContext.ChangeTracker.Entries());
    }

    private static UserDto NewUser() => new(Guid.NewGuid(), "Owner", $"Alice+{Guid.NewGuid():N}@Example.COM");
    private static async Task<Exception?> Attempt(SqlServerUserStore store, UserDto data)
    {
        try { await store.CreateAsync(data.UserId, data.Name, data.Email); return null; }
        catch (Exception exception) { return exception; }
    }
}
