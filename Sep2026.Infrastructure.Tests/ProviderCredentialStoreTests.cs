using System.Text;
using Sep2026.Application.Security;
using Sep2026.Infrastructure.CredentialStorage;
using Xunit;

namespace Sep2026.Infrastructure.Tests;

public sealed class ProviderCredentialStoreTests
{
    [Fact]
    public async Task ReferencesAreOwnerScopedAndTemporaryBuffersAreCleared()
    {
        var vault = new FakeVault();
        var store = new WindowsProviderCredentialStore(vault);
        var owner = Guid.NewGuid();
        var reference = Guid.NewGuid();
        await store.SaveAsync(owner, reference, new("id", "secret"));
        var firstTarget = vault.Target;
        Assert.Contains(owner.ToString("N"), firstTarget);
        Assert.Contains(reference.ToString("N"), firstTarget);
        Assert.DoesNotContain("secret", firstTarget);
        Assert.Contains("secret", vault.CapturedValue);
        Assert.All(vault.Buffer!, value => Assert.Equal(0, value));
        await store.DeleteAsync(Guid.NewGuid(), reference);
        Assert.NotEqual(firstTarget, vault.Target);
        await store.DeleteAsync(owner, reference);
        Assert.Equal(firstTarget, vault.Target);
    }

    [Fact]
    public async Task FailedWritesStillClearManagedSecretBuffers()
    {
        var vault = new FakeVault { FailWrite = true };
        await Assert.ThrowsAsync<IOException>(() => new WindowsProviderCredentialStore(vault).SaveAsync(Guid.NewGuid(), Guid.NewGuid(), new("id", "secret")));
        Assert.All(vault.Buffer!, value => Assert.Equal(0, value));
    }

    [Fact]
    public async Task InvalidIdentifiersAndCancellationDoNotTouchTheVault()
    {
        var vault = new FakeVault();
        var store = new WindowsProviderCredentialStore(vault);
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await store.SaveAsync(Guid.NewGuid(), Guid.NewGuid(), new("id", "secret"), new CancellationToken(true)));
        await Assert.ThrowsAsync<ArgumentException>(async () => await store.DeleteAsync(Guid.Empty, Guid.NewGuid()));
        Assert.Null(vault.Target);
    }

    private sealed class FakeVault : ICredentialVault
    {
        public string? Target { get; private set; }
        public byte[]? Buffer { get; private set; }
        public string? CapturedValue { get; private set; }
        public bool FailWrite { get; init; }
        public void Write(string target, string owner, byte[] value)
        {
            Target = target;
            Buffer = value;
            CapturedValue = Encoding.UTF8.GetString(value);
            if (FailWrite) throw new IOException("vault unavailable");
        }
        public void Delete(string target) => Target = target;
    }
}
