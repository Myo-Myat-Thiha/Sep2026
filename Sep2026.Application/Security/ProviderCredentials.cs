namespace Sep2026.Application.Security;

// Transient input for the proposed local key-based flow. This is deliberately
// not a record: generated record string representations would expose secrets.
public sealed class ProviderCredentials
{
    public string KeyId { get; }
    public string SecretKey { get; }

    public ProviderCredentials(string keyId, string secretKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(secretKey);
        KeyId = keyId;
        SecretKey = secretKey;
    }

    public override string ToString() => "[Redacted provider credentials]";
}
