using OAuthLearn.Api.Data;

namespace OAuthLearn.Api.Email;

/// <summary>"Sends" email by storing it in mailbox_messages, viewable at /dev/mailbox. Development/Testing only.</summary>
public class DevMailboxEmailSender(AppDbContext db, TimeProvider time) : IEmailSender
{
    public async Task SendAsync(string to, string subject, string bodyText, CancellationToken ct)
    {
        db.MailboxMessages.Add(new MailboxMessage
        {
            Id = Guid.CreateVersion7(),
            ToAddress = to,
            Subject = subject,
            BodyText = bodyText,
            CreatedAt = time.GetUtcNow(),
        });
        await db.SaveChangesAsync(ct);
    }
}
