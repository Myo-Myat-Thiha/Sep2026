using System.Net.Mail;

namespace Sep2026.Domain;

/// <summary>Shared rules for owner profiles, independent of storage or portfolios.</summary>
public static class UserProfileRules
{
    public const int MaxNameLength = 200;
    public const int MaxEmailLength = 254;

    public static string NormalizeName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var normalized = name.Trim();
        ValidateName(normalized);
        return normalized;
    }

    public static string NormalizeEmail(string email)
    {
        ArgumentNullException.ThrowIfNull(email);
        var normalized = email.Trim();
        ValidateEmail(normalized);
        return normalized;
    }

    // Validation-only methods deliberately preserve saved values during restoration.
    public static void ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Length > MaxNameLength)
            throw new ArgumentException($"Name must not exceed {MaxNameLength} characters.", nameof(name));
    }

    public static void ValidateEmail(string email)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        if (email.Length > MaxEmailLength)
            throw new ArgumentException($"Email must not exceed {MaxEmailLength} characters.", nameof(email));

        if (!MailAddress.TryCreate(email, out var address) ||
            address.DisplayName.Length != 0 ||
            !string.Equals(address.Address, email, StringComparison.Ordinal) ||
            email.Any(char.IsControl) ||
            address.Host.StartsWith('[') ||
            !address.Host.Contains('.') ||
            address.Host.Split('.').Any(label => label.Length == 0))
        {
            throw new FormatException(
                "Email must be one plain address with a dotted domain, such as user@example.com.");
        }
    }
}
