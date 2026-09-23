namespace OAuthLearn.Api.Data;

/// <summary>A person with an account, reached by Google, by password, or both (data-model.md: users).</summary>
public class User
{
    public Guid Id { get; set; }

    /// <summary>Google's stable account identifier (<c>sub</c>); null for password-only accounts.</summary>
    public string? GoogleSubject { get; set; }

    /// <summary>Email as entered or as given by Google (displayed).</summary>
    public required string Email { get; set; }

    /// <summary><c>lower(trim(email))</c>; unique. All lookups by email use this.</summary>
    public required string EmailNormalized { get; set; }

    public string? DisplayName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastLoginAt { get; set; }

    /// <summary>Null until ownership of the email is proven; the account is inactive while null.</summary>
    public DateTimeOffset? EmailVerifiedAt { get; set; }

    /// <summary><see cref="UserCreatedVia.Google"/> or <see cref="UserCreatedVia.Password"/>.</summary>
    public string CreatedVia { get; set; } = UserCreatedVia.Google;

    /// <summary>Incremented on logout, password reset, and risky account linking; older sessions are rejected.</summary>
    public int SessionVersion { get; set; }

    public PasswordCredential? PasswordCredential { get; set; }
}

public static class UserCreatedVia
{
    public const string Google = "google";
    public const string Password = "password";
}
