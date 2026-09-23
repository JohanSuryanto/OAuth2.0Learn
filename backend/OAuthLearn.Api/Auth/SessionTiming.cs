using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace OAuthLearn.Api.Auth;

/// <summary>When the server will end the current session, as seen by the server (spec 003, research R3).</summary>
public record SessionTimingDto(
    int IdleSecondsLeft,
    DateTimeOffset IdleExpiresAt,
    int AbsoluteSecondsLeft,
    DateTimeOffset AbsoluteExpiresAt);

public static class SessionTiming
{
    /// <summary>
    /// Mirrors the cookie handler's sliding-expiration rule: a request renews the ticket (new expiry = now + 60 min)
    /// only once more time has elapsed since it was issued than remains before it expires. Otherwise the original
    /// expiry stands. Returns null when there is no cookie ticket (e.g. the test auth scheme).
    /// </summary>
    public static SessionTimingDto? Compute(
        AuthenticationProperties? properties,
        ClaimsPrincipal user,
        DateTimeOffset now,
        CookieAuthenticationOptions cookie,
        TimeSpan absoluteLifetime)
    {
        if (properties?.IssuedUtc is not { } issued
            || properties.ExpiresUtc is not { } expires
            || !long.TryParse(user.FindFirstValue(AuthClaims.AuthTime), out var authTimeSeconds))
        {
            return null;
        }

        var idleExpiresAt = expires;
        var allowRefresh = properties.AllowRefresh ?? true;
        if (cookie.SlidingExpiration && allowRefresh && expires - now < now - issued)
        {
            // This very request makes the cookie handler reissue the ticket.
            idleExpiresAt = now + cookie.ExpireTimeSpan;
        }

        var absoluteExpiresAt = DateTimeOffset.FromUnixTimeSeconds(authTimeSeconds) + absoluteLifetime;

        return new SessionTimingDto(
            SecondsLeft(idleExpiresAt, now),
            idleExpiresAt,
            SecondsLeft(absoluteExpiresAt, now),
            absoluteExpiresAt);
    }

    private static int SecondsLeft(DateTimeOffset end, DateTimeOffset now) =>
        (int)Math.Max(0, Math.Floor((end - now).TotalSeconds));
}
