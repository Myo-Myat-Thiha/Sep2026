using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Sep2026.Application.Security;
using Sep2026.Infrastructure.CredentialStorage;
using Xunit;

namespace Sep2026.Infrastructure.Tests;

public sealed class NativeCredentialFactAttribute : FactAttribute
{
    public NativeCredentialFactAttribute()
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("GEMINO_TEST_NATIVE_CREDENTIALS") != "1")
            Skip = "Set GEMINO_TEST_NATIVE_CREDENTIALS=1 on Windows to test a disposable native credential.";
    }
}

public sealed class NativeCredentialStoreTests
{
    [NativeCredentialFact]
    public async Task WritesTheExactCredentialAndDeletesItIdempotently()
    {
        var owner = Guid.NewGuid();
        var reference = Guid.NewGuid();
        var target = $"Gemino:ProviderCredential:{owner:N}:{reference:N}";
        var store = new WindowsProviderCredentialStore();
        try
        {
            await store.SaveAsync(owner, reference, new ProviderCredentials("native-test-key", "native-test-secret"));
            Assert.True(CredReadW(target, 1, 0, out var pointer));
            try
            {
                var credential = Marshal.PtrToStructure<NativeCredential>(pointer);
                var blob = new byte[credential.CredentialBlobSize];
                try
                {
                    Marshal.Copy(credential.CredentialBlob, blob, 0, blob.Length);
                    using var json = JsonDocument.Parse(blob);
                    Assert.Equal("native-test-key", json.RootElement.GetProperty("KeyId").GetString());
                    Assert.Equal("native-test-secret", json.RootElement.GetProperty("SecretKey").GetString());
                    Assert.Equal(owner.ToString("N"), credential.UserName);
                }
                finally { CryptographicOperations.ZeroMemory(blob); }
            }
            finally { CredFree(pointer); }
        }
        finally { await store.DeleteAsync(owner, reference); }
        Assert.False(CredReadW(target, 1, 0, out var missing));
        if (missing != IntPtr.Zero) CredFree(missing);
        await store.DeleteAsync(owner, reference);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string UserName;
    }
    [DllImport("Advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredReadW(string target, uint type, uint flags, out IntPtr credential);
    [DllImport("Advapi32.dll", ExactSpelling = true)]
    private static extern void CredFree(IntPtr pointer);
}
