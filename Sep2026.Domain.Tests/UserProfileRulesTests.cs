using Xunit;

namespace Sep2026.Domain.Tests;

public sealed class UserProfileRulesTests
{
    [Theory]
    [InlineData("Alice O'Connor")]
    [InlineData("မြတ်")]
    [InlineData("Zoë O'Connor-Smith")]
    [InlineData("李 小明")]
    public void NamesAreTrimmedWithoutRestrictingUnicodeOrChangingTheirContents(string name)
        => Assert.Equal(name, UserProfileRules.NormalizeName($" \t{name} \r\n"));

    [Theory]
    [InlineData("alice@example.com")]
    [InlineData("Alice+Reports@Example.COM")]
    [InlineData("first.last@reports.example.co.uk")]
    [InlineData("o'connor@example.com")]
    public void EmailsAreTrimmedWithoutChangingTheirCasing(string email)
        => Assert.Equal(email, UserProfileRules.NormalizeEmail($" \t{email} \r\n"));

    [Theory]
    [InlineData(null, typeof(ArgumentNullException))]
    [InlineData("", typeof(ArgumentException))]
    [InlineData("   ", typeof(ArgumentException))]
    [InlineData("\t\r\n", typeof(ArgumentException))]
    public void MissingFieldsAreRejectedByNormalizationAndValidation(string? value, Type exception)
    {
        Assert.Throws(exception, () => UserProfileRules.NormalizeName(value!));
        Assert.Throws(exception, () => UserProfileRules.NormalizeEmail(value!));
        Assert.Throws(exception, () => UserProfileRules.ValidateName(value!));
        Assert.Throws(exception, () => UserProfileRules.ValidateEmail(value!));
    }

    [Fact]
    public void NamesAtTheStorageLimitAreAcceptedAfterTrimming()
    {
        var name = new string('a', 200);
        Assert.Equal(name, UserProfileRules.NormalizeName($" {name} "));
        UserProfileRules.ValidateName(name);
    }

    [Fact]
    public void NamesOverTheStorageLimitAreRejected()
    {
        var name = new string('a', 201);
        Assert.Throws<ArgumentException>(() => UserProfileRules.NormalizeName(name));
        Assert.Throws<ArgumentException>(() => UserProfileRules.ValidateName(name));
    }

    [Fact]
    public void EmailsAtTheStorageLimitAreAcceptedAfterTrimming()
    {
        var email = CreateEmailWithLength(254);
        Assert.Equal(email, UserProfileRules.NormalizeEmail($" {email} "));
        UserProfileRules.ValidateEmail(email);
    }

    [Fact]
    public void EmailsOverTheStorageLimitAreRejected()
    {
        var email = CreateEmailWithLength(255);
        Assert.Throws<ArgumentException>(() => UserProfileRules.NormalizeEmail(email));
        Assert.Throws<ArgumentException>(() => UserProfileRules.ValidateEmail(email));
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("alice.example.com")]
    [InlineData("alice@")]
    [InlineData("@example.com")]
    [InlineData("alice@@example.com")]
    [InlineData("ali ce@example.com")]
    [InlineData("alice@example")]
    [InlineData("alice.example@localhost")]
    [InlineData("alice@.com")]
    [InlineData("alice@example..com")]
    [InlineData("alice@example.com.")]
    [InlineData("Alice <alice@example.com>")]
    [InlineData("<alice@example.com>")]
    [InlineData("alice@example.com,bob@example.com")]
    [InlineData("alice@example.com;bob@example.com")]
    [InlineData("alice(comment)@example.com")]
    [InlineData("alice\r\n@example.com")]
    [InlineData("alice@[127.0.0.1]")]
    public void InvalidOrUnsupportedEmailFormatsAreRejected(string email)
    {
        Assert.Throws<FormatException>(() => UserProfileRules.NormalizeEmail(email));
        Assert.Throws<FormatException>(() => UserProfileRules.ValidateEmail(email));
    }

    [Fact]
    public void ValidationDoesNotSilentlyTrimAnEmail()
    {
        const string email = " Alice@example.com ";
        Assert.Equal("Alice@example.com", UserProfileRules.NormalizeEmail(email));
        Assert.Throws<FormatException>(() => UserProfileRules.ValidateEmail(email));
    }

    [Fact]
    public void ValidationChecksTheSuppliedLengthRatherThanTrimmingToFit()
    {
        var name = $" {new string('a', 200)} ";
        Assert.Equal(new string('a', 200), UserProfileRules.NormalizeName(name));
        Assert.Throws<ArgumentException>(() => UserProfileRules.ValidateName(name));

        var email = $" {CreateEmailWithLength(254)} ";
        Assert.Equal(CreateEmailWithLength(254), UserProfileRules.NormalizeEmail(email));
        Assert.Throws<ArgumentException>(() => UserProfileRules.ValidateEmail(email));
    }

    private static string CreateEmailWithLength(int length)
        => new string('a', 64) + "@" + new string('b', 63) + "." + new string('c', 63) + "." + new string('d', length - 193);
}
