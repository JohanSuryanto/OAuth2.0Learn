using System.Security.Claims;
using OAuthLearn.Api.Data;

namespace OAuthLearn.Api.Auth;

/// <summary>Custom claims stored in the auth cookie (data-model.md: Session).</summary>
public static class AuthClaims
{
    public const string AppUserId = "app_user_id";
    public const string SessionVersion = "session_version";

    /// <summary>Sign-in time as Unix seconds (UTC); used for the absolute session cap.</summary>
    public const string AuthTime = "auth_time";

    /// <summary>The user_sessions row this cookie belongs to (spec 004).</summary>
    public const string Sid = "sid";

    /// <summary>The app's own session claims, shared by Google and password sign-in.</summary>
    public static IEnumerable<Claim> SessionClaims(User user, Guid sid, DateTimeOffset authTime) =>
    [
        new(AppUserId, user.Id.ToString()),
        new(SessionVersion, user.SessionVersion.ToString()),
        new(AuthTime, authTime.ToUnixTimeSeconds().ToString()),
        new(Sid, sid.ToString()),
    ];

    /// <summary>Full claim set for a session the app issues itself (password sign-in, verification, re-issue).</summary>
    public static List<Claim> ForUser(User user, string nameIdentifier, Guid sid, DateTimeOffset authTime)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, nameIdentifier),
            new(ClaimTypes.Email, user.Email),
        };
        if (user.DisplayName is not null)
        {
            claims.Add(new Claim(ClaimTypes.Name, user.DisplayName));
        }

        claims.AddRange(SessionClaims(user, sid, authTime));
        return claims;
    }

    /// <summary>The current cookie's sid and auth_time, when present.</summary>
    public static (Guid Sid, DateTimeOffset AuthTime)? CurrentSession(ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(Sid), out var sid)
        && long.TryParse(principal.FindFirstValue(AuthTime), out var seconds)
            ? (sid, DateTimeOffset.FromUnixTimeSeconds(seconds))
            : null;

    public static Guid? UserId(ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(AppUserId), out var id) ? id : null;
}
