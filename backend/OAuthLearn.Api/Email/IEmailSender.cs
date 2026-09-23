namespace OAuthLearn.Api.Email;

/// <summary>Delivers app emails. Development/Testing use <see cref="DevMailboxEmailSender"/> (research R6).</summary>
public interface IEmailSender
{
    Task SendAsync(string to, string subject, string bodyText, CancellationToken ct);
}
