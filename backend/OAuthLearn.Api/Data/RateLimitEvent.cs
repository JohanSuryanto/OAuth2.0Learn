namespace OAuthLearn.Api.Data;

/// <summary>One counted event for throttling; the key is an HMAC, never a plain email or IP.</summary>
public class RateLimitEvent
{
    public long Id { get; set; }

    public required string Bucket { get; set; }

    public required byte[] KeyHash { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
}
