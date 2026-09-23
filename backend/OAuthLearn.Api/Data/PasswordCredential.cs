namespace OAuthLearn.Api.Data;

/// <summary>
/// A user's password (data-model.md: password_credentials). <see cref="PendingHash"/> is set by registration and
/// only becomes <see cref="ActiveHash"/> after email verification with the same password.
/// </summary>
public class PasswordCredential
{
    public Guid UserId { get; set; }

    public string? ActiveHash { get; set; }

    public string? PendingHash { get; set; }

    public DateTimeOffset? ActiveSetAt { get; set; }

    public DateTimeOffset? PendingSetAt { get; set; }
}
