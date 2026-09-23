using OAuthLearn.Api.Passwords;

namespace OAuthLearn.Api.Tests;

public class PasswordPolicyTests
{
    private static readonly CommonPasswords Common = new();
    private const string Email = "alice@example.com";

    [Fact]
    public void Common_list_is_loaded_without_header_lines()
    {
        Assert.InRange(Common.Count, 9_000, 10_100);
        Assert.False(Common.Contains("# Top 10,000 most common passwords."));
        Assert.True(Common.Contains("QWERTY"));
    }

    [Theory]
    [InlineData(9, "password_too_short")]
    [InlineData(10, null)]
    [InlineData(128, null)]
    [InlineData(129, "password_too_long")]
    public void Password_length_boundaries(int length, string? expected)
    {
        var password = string.Concat(Enumerable.Repeat("xk7", 50))[..length];

        Assert.Equal(expected, PasswordPolicy.ValidateNewPassword(password, Email, Common));
    }

    [Theory]
    [InlineData("alice@example.com")]
    [InlineData(" ALICE@Example.com ")]
    public void Password_equal_to_email_is_rejected(string password) =>
        Assert.Equal("password_is_email", PasswordPolicy.ValidateNewPassword(password, Email, Common));

    [Theory]
    [InlineData("qwertyuiop")]
    [InlineData("BasketBall")]
    public void Common_passwords_are_rejected_in_any_case(string password) =>
        Assert.Equal("password_common", PasswordPolicy.ValidateNewPassword(password, Email, Common));

    [Fact]
    public void Passphrase_without_composition_rules_is_accepted() =>
        Assert.Null(PasswordPolicy.ValidateNewPassword("correct horse battery staple", Email, Common));

    [Theory]
    [InlineData("alice@example.com", null)]
    [InlineData("  alice@example.com  ", null)]
    [InlineData("alice@localhost", "email_invalid")]
    [InlineData("Alice <alice@example.com>", "email_invalid")]
    [InlineData("alice", "email_invalid")]
    [InlineData("", "email_invalid")]
    [InlineData(null, "email_invalid")]
    public void Email_validation(string? email, string? expected) =>
        Assert.Equal(expected, PasswordPolicy.ValidateEmail(email));

    [Fact]
    public void Email_length_limit_is_254()
    {
        var domain = "@example.com";
        Assert.Null(PasswordPolicy.ValidateEmail(new string('a', 254 - domain.Length) + domain));
        Assert.Equal("email_invalid", PasswordPolicy.ValidateEmail(new string('a', 255 - domain.Length) + domain));
    }

    [Fact]
    public void Display_name_limit_is_100_after_trimming()
    {
        Assert.Null(PasswordPolicy.ValidateDisplayName("  " + new string('a', 100) + "  "));
        Assert.Equal("display_name_too_long", PasswordPolicy.ValidateDisplayName(new string('a', 101)));
        Assert.Null(PasswordPolicy.ValidateDisplayName(null));
    }
}
