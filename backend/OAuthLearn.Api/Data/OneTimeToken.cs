namespace OAuthLearn.Api.Data;

/// <summary>Single-use emailed secret; only its SHA-256 is stored (data-model.md: one_time_tokens).</summary>
public class OneTimeToken
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public required string Purpose { get; set; }

    public required byte[] TokenHash { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? UsedAt { get; set; }
}

public static class TokenPurposes
{
    public const string VerifyEmail = "verify_email";
    public const string ResetPassword = "reset_password";
}
