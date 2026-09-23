using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using OAuthLearn.Api.Data;

namespace OAuthLearn.Api.Auth;

/// <summary>
/// Issues the same cookie session as Google sign-in (002 research R9), so SessionValidator, /me, logout and the
/// 60 min / 8 h limits apply unchanged. Every sign-in creates a user_sessions row (spec 004).
/// </summary>
public static class SessionIssuer
{
    /// <summary>A new sign-in: new session row, new ticket (replacing any old cookie).</summary>
    public static async Task SignInAsync(HttpContext ctx, User user)
    {
        var now = ctx.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();
        var session = await ctx.RequestServices.GetRequiredService<SessionStore>()
            .CreateAsync(user.Id, ctx, now, ctx.RequestAborted);
        await IssueAsync(ctx, user, session.Id, now);
    }

    /// <summary>
    /// Re-issues the current session's cookie (research R3): same sid and auth_time (so the 8-hour limit doesn't
    /// move), current session_version and display name, and a fresh 60-minute idle window.
    /// </summary>
    public static Task ReissueAsync(HttpContext ctx, User user, Guid sid, DateTimeOffset authTime) =>
        IssueAsync(ctx, user, sid, authTime);

    private static Task IssueAsync(HttpContext ctx, User user, Guid sid, DateTimeOffset authTime)
    {
        var identity = new ClaimsIdentity(
            AuthClaims.ForUser(user, user.GoogleSubject ?? user.Id.ToString(), sid, authTime),
            CookieAuthenticationDefaults.AuthenticationScheme);

        return ctx.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = false });
    }
}
