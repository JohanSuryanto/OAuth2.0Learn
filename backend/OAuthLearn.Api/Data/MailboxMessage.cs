namespace OAuthLearn.Api.Data;

/// <summary>A message "sent" by the app, captured by the development mailbox instead of being emailed.</summary>
public class MailboxMessage
{
    public Guid Id { get; set; }

    public required string ToAddress { get; set; }

    public required string Subject { get; set; }

    public required string BodyText { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
