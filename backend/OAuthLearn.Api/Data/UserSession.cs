namespace OAuthLearn.Api.Data;

/// <summary>One signed-in device (spec 004, data-model.md: user_sessions). Its id is the cookie's <c>sid</c> claim.</summary>
public class UserSession
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    /// <summary>"Signed in" time shown in the sessions list.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Same as the cookie's auth_time claim; the 8-hour limit is measured from here.</summary>
    public DateTimeOffset AuthTime { get; set; }

    /// <summary>Sign-in or latest re-authentication / password change (the 10-minute rule).</summary>
    public DateTimeOffset LastAuthAt { get; set; }

    /// <summary>Refreshed at most every 5 minutes.</summary>
    public DateTimeOffset LastSeenAt { get; set; }

    /// <summary>"Chrome on Windows"; null means "Unknown device".</summary>
    public string? DeviceLabel { get; set; }

    /// <summary>"203.0.113.x"; never the full address.</summary>
    public string? IpMasked { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }
}
