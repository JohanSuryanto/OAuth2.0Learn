using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OAuthLearn.Api.Data;

namespace OAuthLearn.Api.Auth;

/// <summary><paramref name="Session"/> is null without a cookie ticket (spec 003 contract).</summary>
public record MeResponse(string Email, string? Name, SessionTimingDto? Session);

/// <summary>/api/auth/* endpoints (contracts/auth-api.md).</summary>
public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        // Opened in the popup. Starts the Google Authorization Code flow. No returnUrl: no open redirect.
        group.MapGet("/login", () => Results.Challenge(
                new AuthenticationProperties { RedirectUri = "/api/auth/popup-complete", IsPersistent = false },
                [GoogleDefaults.AuthenticationScheme]))
            .AllowAnonymous();

        // Opened in the popup by a signed-in user to confirm it's them (spec 004, research R6). Google is asked to
        // prompt for credentials again; the callback verifies the linked account and does NOT sign in.
        group.MapGet("/reauth/google", (ClaimsPrincipal user) =>
            {
                if (AuthClaims.UserId(user) is not { } userId || AuthClaims.CurrentSession(user) is not { } current)
                {
                    return Results.Unauthorized();
                }

                var properties = new AuthenticationProperties { RedirectUri = "/api/auth/popup-complete?result=reauth_ok" };
                properties.Items[GoogleAuthEvents.PurposeKey] = GoogleAuthEvents.ReauthPurpose;
                properties.Items[GoogleAuthEvents.ReauthUserKey] = userId.ToString();
                properties.Items[GoogleAuthEvents.ReauthSidKey] = current.Sid.ToString();
                return Results.Challenge(properties, [GoogleDefaults.AuthenticationScheme]);
            })
            .RequireAuthorization();

        // Where the popup lands after /signin-google. Sends it back to the dashboard's own origin.
        group.MapGet("/popup-complete", (string? error, string? result, IConfiguration config) =>
            {
                var outcome = (error, result) switch
                {
                    (null, "reauth_ok") => "reauth_ok",
                    (null, _) => "success",
                    ("access_denied", _) => "access_denied",
                    ("reauth_mismatch", _) => "reauth_mismatch",
                    _ => "signin_failed",
                };
                return Results.Redirect($"{config["Frontend:Origin"]}/auth-complete.html?result={outcome}");
            })
            .AllowAnonymous();

        group.MapGet("/me", async (
                HttpContext ctx,
                IOptionsMonitor<CookieAuthenticationOptions> cookieOptions,
                TimeProvider time,
                IConfiguration config) =>
            {
                // Cached per request by the cookie handler; no second decryption.
                var cookieAuth = await ctx.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                var session = SessionTiming.Compute(
                    cookieAuth.Properties,
                    ctx.User,
                    time.GetUtcNow(),
                    cookieOptions.Get(CookieAuthenticationDefaults.AuthenticationScheme),
                    SessionValidator.AbsoluteLifetime(config));

                return Results.Ok(new MeResponse(
                    ctx.User.FindFirstValue(ClaimTypes.Email) ?? "",
                    ctx.User.FindFirstValue(ClaimTypes.Name),
                    session));
            })
            .RequireAuthorization();

        // "Stay signed in" (spec 004, research R7): re-issues the cookie with a fresh idle window; auth_time is kept,
        // so the 8-hour limit can't move.
        group.MapPost("/session/extend", async (
                HttpContext ctx,
                AppDbContext db,
                IOptionsMonitor<CookieAuthenticationOptions> cookieOptions,
                TimeProvider time,
                IConfiguration config) =>
            {
                if (AuthClaims.UserId(ctx.User) is not { } userId || AuthClaims.CurrentSession(ctx.User) is not { } current)
                {
                    return Results.BadRequest();
                }

                var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId, ctx.RequestAborted);
                await SessionIssuer.ReissueAsync(ctx, user, current.Sid, current.AuthTime);

                var now = time.GetUtcNow();
                var options = cookieOptions.Get(CookieAuthenticationDefaults.AuthenticationScheme);
                var renewed = new AuthenticationProperties { IssuedUtc = now, ExpiresUtc = now + options.ExpireTimeSpan };
                var session = SessionTiming.Compute(renewed, ctx.User, now, options, SessionValidator.AbsoluteLifetime(config));
                return Results.Ok(new MeResponse(user.Email, user.DisplayName, session));
            })
            .AddEndpointFilter<RequireFetchHeaderFilter>()
            .RequireAuthorization();

        group.MapPost("/logout", async (HttpContext ctx, UserService users, SessionStore sessions, ILoggerFactory loggerFactory) =>
            {
                var logger = loggerFactory.CreateLogger("OAuthLearn.Auth.Logout");
                if (ctx.User.Identity?.IsAuthenticated == true && AuthClaims.UserId(ctx.User) is { } userId)
                {
                    try
                    {
                        if (AuthClaims.CurrentSession(ctx.User) is { } current)
                        {
                            // This device only (spec 004, FR-015).
                            await sessions.RevokeAsync(userId, current.Sid, ctx.RequestAborted);
                        }
                        else
                        {
                            // A cookie from before per-device sessions: fall back to the old "everywhere" logout.
                            await users.RevokeSessionsAsync(userId, ctx.RequestAborted);
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // Keep the cookie so the client knows logout did not fully happen.
                        logger.LogError(ex, "Logout failed: could not revoke the session. UserId={UserId}", userId);
                        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
                    }

                    logger.LogInformation("User signed out on this device. UserId={UserId}", userId);
                }

                await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return Results.NoContent();
            })
            // CSRF guard on top of SameSite=Lax: cross-site forms cannot set custom headers.
            .AddEndpointFilter<RequireFetchHeaderFilter>()
            .AllowAnonymous();

        return app;
    }
}
