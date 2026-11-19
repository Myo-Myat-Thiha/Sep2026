using Sep2026.Application.DTOs;
using Sep2026.Application.Exceptions;
using Sep2026.Application.Interfaces;
using Sep2026.Application.Services;
using Xunit;

namespace Sep2026.Application.Tests;

public sealed class UserServiceTests
{
    private const string Name = "Alice";
    private const string Email = "Alice@example.com";
    private const string Password = "Correct horse battery staple!";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task InvalidNewPasswordsDoNotAccessStorage()
    {
        var store = new StubStore();
        await Assert.ThrowsAsync<ArgumentException>(() => new UserService(store).CreateAsync(Name, Email, "short"));
        Assert.Empty(store.Calls);
    }

    [Fact]
    public async Task SignInNormalizesEmailButPreservesTheExactPasswordAndToken()
    {
        var profile = new UserDto(Guid.NewGuid(), Name, Email);
        var store = new StubStore { Authentication = (_, _, _) => Task.FromResult<UserDto?>(profile) };
        using var cancellation = new CancellationTokenSource();
        var result = await new UserService(store).SignInAsync($" {Email} ", $" {Password} ", cancellation.Token);
        Assert.Same(profile, result);
        Assert.Equal(Email, store.CheckedEmail);
        Assert.Equal($" {Password} ", store.ReceivedPassword);
        Assert.Equal(cancellation.Token, store.CheckToken);
        Assert.Equal(new[] { "authenticate" }, store.Calls);
    }

    [Fact]
    public async Task FailedSignInReturnsNullAndNeverCreatesAProfile()
    {
        var store = new StubStore();
        Assert.Null(await new UserService(store).SignInAsync(Email, "wrong password"));
        Assert.Equal(new[] { "authenticate" }, store.Calls);
        Assert.Null(store.Inserted);
    }

    [Fact]
    public async Task EmptyAndOversizedSignInPasswordsDoNotAccessStorage()
    {
        var store = new StubStore();
        var service = new UserService(store);
        Assert.Null(await service.SignInAsync(Email, ""));
        Assert.Null(await service.SignInAsync(Email, new string('a', 129)));
        Assert.Empty(store.Calls);
    }

    [Fact]
    public async Task InitialPasswordSetupNormalizesEmailAndForwardsTheSecretAndToken()
    {
        var store = new StubStore();
        using var cancellation = new CancellationTokenSource();
        Assert.True(await new UserService(store).SetInitialPasswordAsync($" {Email} ", Password, cancellation.Token));
        Assert.Equal(Email, store.CheckedEmail);
        Assert.Equal(Password, store.ReceivedPassword);
        Assert.Equal(cancellation.Token, store.CheckToken);
        Assert.Equal(new[] { "setup" }, store.Calls);
    }

    [Fact]
    public async Task SignInAndSetupHonorPreCancellation()
    {
        var store = new StubStore();
        var service = new UserService(store);
        var cancelled = new CancellationToken(true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SignInAsync(Email, Password, cancelled));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SetInitialPasswordAsync(Email, Password, cancelled));
        Assert.Empty(store.Calls);
    }

    [Fact]
    public void RequiresAStore() => Assert.Throws<ArgumentNullException>(() => new UserService(null!));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RejectsNullFieldsWithoutAccessingStorage(bool missingName)
    {
        var store = new StubStore();
        await Assert.ThrowsAsync<ArgumentNullException>(() => new UserService(store)
            .CreateAsync(missingName ? null! : Name, missingName ? Email : null!, Password));
        Assert.Empty(store.Calls);
    }

    [Theory]
    [InlineData("  ", "alice@example.com", typeof(ArgumentException))]
    [InlineData("Alice", "not-an-email", typeof(FormatException))]
    public async Task RejectsInvalidInputBeforeAccessingStorage(string name, string email, Type expectedException)
    {
        var store = new StubStore();
        await Assert.ThrowsAsync(expectedException, () => new UserService(store).CreateAsync(name, email, Password));
        Assert.Empty(store.Calls);
    }

    [Fact]
    public async Task NormalizesInputPreservesCasingAndReturnsTheSavedIdentity()
    {
        var store = new StubStore();
        using var cancellation = new CancellationTokenSource();
        var result = await new UserService(store).CreateAsync("  Zoë O'Connor  ", " Alice+Reports@Example.COM ", Password, cancellation.Token);

        Assert.Equal(new[] { "check", "create" }, store.Calls);
        Assert.Equal("Alice+Reports@Example.COM", store.CheckedEmail);
        var inserted = Assert.IsType<UserDto>(store.Inserted);
        Assert.NotEqual(Guid.Empty, inserted.UserId);
        Assert.Equal(new UserDto(inserted.UserId, "Zoë O'Connor", "Alice+Reports@Example.COM"), result);
        Assert.Equal(cancellation.Token, store.CheckToken);
        Assert.Equal(cancellation.Token, store.CreateToken);
    }

    [Fact]
    public async Task GeneratesDifferentIdentitiesForDifferentCreationAttempts()
    {
        var service = new UserService(new StubStore());
        var first = await service.CreateAsync(Name, Email, Password);
        var second = await service.CreateAsync("Bob", "bob@example.com", Password);
        Assert.NotEqual(first.UserId, second.UserId);
    }

    [Fact]
    public async Task ExistingEmailPreventsInsertion()
    {
        var store = new StubStore { Check = (_, _) => Task.FromResult(true) };
        await Assert.ThrowsAsync<EmailAlreadyExistsException>(() => new UserService(store).CreateAsync(Name, Email, Password));
        Assert.Equal(new[] { "check" }, store.Calls);
    }

    [Fact]
    public async Task LookupFailurePreventsInsertionAndIsPreserved()
    {
        var failure = new TimeoutException("lookup failed");
        var store = new StubStore { Check = (_, _) => Task.FromException<bool>(failure) };
        Assert.Same(failure, await Assert.ThrowsAsync<TimeoutException>(() => new UserService(store).CreateAsync(Name, Email, Password)));
        Assert.Equal(new[] { "check" }, store.Calls);
    }

    [Fact]
    public async Task SaveFailureIsNotReportedAsASuccessfulCreation()
    {
        var failure = new InvalidOperationException("save failed");
        var store = new StubStore { Create = (_, _) => Task.FromException(failure) };
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => new UserService(store).CreateAsync(Name, Email, Password)));
        Assert.Equal(new[] { "check", "create" }, store.Calls);
    }

    [Fact]
    public async Task DuplicateEmailDetectedDuringSaveRetainsItsUnderlyingFailure()
    {
        var cause = new InvalidOperationException("database conflict");
        var failure = new EmailAlreadyExistsException("Email already saved.", cause);
        var store = new StubStore { Create = (_, _) => Task.FromException(failure) };
        var result = await Assert.ThrowsAsync<EmailAlreadyExistsException>(() => new UserService(store).CreateAsync(Name, Email, Password));
        Assert.Same(failure, result);
        Assert.Same(cause, result.InnerException);
    }

    [Fact]
    public async Task PrecancelledRequestsDoNotAccessStorage()
    {
        var store = new StubStore();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new UserService(store).CreateAsync(Name, Email, Password, new CancellationToken(true)));
        Assert.Empty(store.Calls);
    }

    [Fact]
    public async Task CancellationAfterTheLookupPreventsInsertion()
    {
        using var cancellation = new CancellationTokenSource();
        var store = new StubStore { Check = (_, _) => { cancellation.Cancel(); return Task.FromResult(false); } };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new UserService(store).CreateAsync(Name, Email, Password, cancellation.Token));
        Assert.Equal(new[] { "check" }, store.Calls);
    }

    [Fact]
    public async Task CancellationDuringTheSavePropagates()
    {
        using var cancellation = new CancellationTokenSource();
        var store = new StubStore { Create = (_, token) => { cancellation.Cancel(); return Task.FromCanceled(token); } };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new UserService(store).CreateAsync(Name, Email, Password, cancellation.Token));
        Assert.Equal(cancellation.Token, store.CreateToken);
    }

    [Fact]
    public async Task AwaitsTheLookupAndTheSaveBeforeReturning()
    {
        var lookup = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var saveStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var save = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new StubStore
        {
            Check = (_, _) => lookup.Task,
            Create = (_, _) => { saveStarted.SetResult(); return save.Task; }
        };
        var creation = new UserService(store).CreateAsync(Name, Email, Password);
        Assert.Equal(new[] { "check" }, store.Calls);
        Assert.False(creation.IsCompleted);
        lookup.SetResult(false);
        await saveStarted.Task.WaitAsync(Timeout);
        Assert.False(creation.IsCompleted);
        save.SetResult();
        Assert.Equal(store.Inserted!.UserId, (await creation.WaitAsync(Timeout)).UserId);
    }

    [Fact]
    public async Task ConfirmedSuccessfulSaveIsNotChangedToCancellationAfterward()
    {
        using var cancellation = new CancellationTokenSource();
        var store = new StubStore { Create = (_, _) => { cancellation.Cancel(); return Task.CompletedTask; } };
        var result = await new UserService(store).CreateAsync(Name, Email, Password, cancellation.Token);
        Assert.Equal(store.Inserted!.UserId, result.UserId);
    }

    private sealed class StubStore : IUserStore
    {
        public Func<string, CancellationToken, Task<bool>> Check { get; init; } = (_, _) => Task.FromResult(false);
        public Func<UserDto, CancellationToken, Task> Create { get; init; } = (_, _) => Task.CompletedTask;
        public List<string> Calls { get; } = new();
        public string? CheckedEmail { get; private set; }
        public UserDto? Inserted { get; private set; }
        public CancellationToken CheckToken { get; private set; }
        public CancellationToken CreateToken { get; private set; }

        public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default)
        {
            Calls.Add("check");
            CheckedEmail = email;
            CheckToken = cancellationToken;
            return Check(email, cancellationToken);
        }

        public Task CreateAsync(Guid userId, string name, string email, string password, CancellationToken cancellationToken = default)
        {
            var user = new UserDto(userId, name, email);
            Calls.Add("create");
            Inserted = user;
            CreateToken = cancellationToken;
            return Create(user, cancellationToken);
        }

        public Task<UserDto?> AuthenticateAsync(string email, string password, CancellationToken cancellationToken = default)
        {
            Calls.Add("authenticate");
            CheckedEmail = email;
            CheckToken = cancellationToken;
            ReceivedPassword = password;
            return Authentication(email, password, cancellationToken);
        }

        public Task<bool> SetInitialPasswordAsync(string email, string password, CancellationToken cancellationToken = default)
        {
            Calls.Add("setup");
            CheckedEmail = email;
            CheckToken = cancellationToken;
            ReceivedPassword = password;
            return Task.FromResult(true);
        }

        public string? ReceivedPassword { get; private set; }
        public Func<string, string, CancellationToken, Task<UserDto?>> Authentication { get; init; }
            = (_, _, _) => Task.FromResult<UserDto?>(null);
    }
}
