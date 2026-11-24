using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Sep2026.Application.Interfaces;
using Sep2026.Application.Security;

namespace Sep2026.Infrastructure.CredentialStorage;

public sealed class WindowsProviderCredentialStore : IProviderCredentialStore
{
    private readonly ICredentialVault vault;
    public WindowsProviderCredentialStore() : this(new WindowsCredentialVault()) { }
    internal WindowsProviderCredentialStore(ICredentialVault vault) => this.vault = vault;

    public Task SaveAsync(Guid ownerId, Guid reference, ProviderCredentials credentials, CancellationToken cancellationToken = default)
    {
        var target = Target(ownerId, reference);
        ArgumentNullException.ThrowIfNull(credentials);
        cancellationToken.ThrowIfCancellationRequested();
        var blob = JsonSerializer.SerializeToUtf8Bytes(new { credentials.KeyId, credentials.SecretKey });
        try
        {
            if (blob.Length > 2560) throw new ArgumentException("The credentials exceed the protected storage limit.");
            vault.Write(target, ownerId.ToString("N"), blob);
        }
        finally { CryptographicOperations.ZeroMemory(blob); }
        // If cancellation arrives after the write, the Application service owns
        // compensation. Do not misreport a successful native write as not written.
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid ownerId, Guid reference, CancellationToken cancellationToken = default)
    {
        var target = Target(ownerId, reference);
        cancellationToken.ThrowIfCancellationRequested();
        vault.Delete(target);
        return Task.CompletedTask;
    }

    private static string Target(Guid ownerId, Guid reference)
    {
        if (ownerId == Guid.Empty || reference == Guid.Empty)
            throw new ArgumentException("An owner and credential reference are required.");
        return $"Gemino:ProviderCredential:{ownerId:N}:{reference:N}";
    }
}

internal interface ICredentialVault
{
    void Write(string target, string owner, byte[] value);
    void Delete(string target);
}

internal sealed class WindowsCredentialVault : ICredentialVault
{
    public void Write(string target, string owner, byte[] value)
    {
        RequireWindows();
        var pointer = Marshal.AllocHGlobal(value.Length);
        try
        {
            Marshal.Copy(value, 0, pointer, value.Length);
            var credential = new NativeCredential
            {
                Type = 1, TargetName = target, UserName = owner,
                CredentialBlobSize = (uint)value.Length, CredentialBlob = pointer,
                Persist = 2 // Local-machine persistence, still scoped to this OS user.
            };
            if (!CredWriteW(ref credential, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not save the provider credentials in Windows Credential Manager.");
        }
        finally
        {
            for (var index = 0; index < value.Length; index++) Marshal.WriteByte(pointer, index, 0);
            Marshal.FreeHGlobal(pointer);
        }
    }

    public void Delete(string target)
    {
        RequireWindows();
        if (!CredDeleteW(target, 1, 0))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != 1168) // Missing references are already cleaned up.
                throw new Win32Exception(error, "Could not remove the staged provider credentials.");
        }
    }

    private static void RequireWindows()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Provider credential storage currently requires Windows Credential Manager.");
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
    private static extern bool CredWriteW(ref NativeCredential credential, uint flags);

    [DllImport("Advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDeleteW(string target, uint type, uint flags);
}
