using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using OAuthLearn.Api.Data;

namespace OAuthLearn.Api.Auth;

/// <summary>
/// Cookie <c>OnValidatePrincipal</c>: runs on every authenticated request and rejects sessions that are
/// ended (their user_sessions row is revoked or missing), revoked account-wide (stale session_version),
/// older than the absolute lifetime, or cannot be verified (fail closed).
/// </summary>
public static class SessionValidator
{
    public static readonly TimeSpan DefaultAbsoluteLifetime = TimeSpan.FromHours(8);

    /// <summary>The absolute session cap (FR-020), <c>Auth:AbsoluteSessionLifetime</c>, default 8 hours.</summary>
    public static TimeSpan AbsoluteLifetime(IConfiguration config) =>
        config.GetValue<TimeSpan?>("Auth:AbsoluteSessionLifetime") ?? DefaultAbsoluteLifetime;

    public static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var services = context.HttpContext.RequestServices;
        var reason = await ValidateOrUpgradeAsync(context, services, context.HttpContext.RequestAborted);
        if (reason is null)
        {
            return;
        }

        Logger(services).LogInformation("Session rejected. Reason={Reason}", reason);
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    /// <summary>Null when the session is valid; otherwise a rejection reason (safe to log).</summary>
    private static async Task<string?> ValidateOrUpgradeAsync(
        CookieValidatePrincipalContext context, IServiceProvider services, CancellationToken ct)
    {
        var principal = context.Principal;
        if (principal is null
            || !Guid.TryParse(principal.FindFirstValue(AuthClaims.AppUserId), out var userId)
            || !int.TryParse(principal.FindFirstValue(AuthClaims.SessionVersion), out var sessionVersion)
            || !long.TryParse(principal.FindFirstValue(AuthClaims.AuthTime), out var authTimeSeconds))
        {
            return "missing_claims";
        }

        // Absolute cap (FR-020): checked before touching the database.
        var authTime = DateTimeOffset.FromUnixTimeSeconds(authTimeSeconds);
        var now = services.GetRequiredService<TimeProvider>().GetUtcNow();
        if (now - authTime > AbsoluteLifetime(services.GetRequiredService<IConfiguration>()))
        {
            return "expired_absolute";
        }

        var store = services.GetRequiredService<SessionStore>();
        try
        {
            if (Guid.TryParse(principal.FindFirstValue(AuthClaims.Sid), out var sid))
            {
                var (session, currentVersion) = await store.FindAsync(sid, userId, ct);
                if (session is null || session.RevokedAt is not null)
                {
                    return "session_ended";
                }

                if (currentVersion != sessionVersion)
                {
                    return "revoked";
                }

                await store.TouchAsync(session, ct);
                return null;
            }

            // Cookie issued before per-device sessions existed (spec 004, research R2).
            var version = await services.GetRequiredService<UserService>().GetSessionVersionAsync(userId, ct);
            if (version != sessionVersion)
            {
                return "revoked";
            }

            var legacy = await store.CreateLegacyAsync(userId, context.HttpContext, authTime, ct);
            var upgraded = new ClaimsIdentity(
                principal.Claims.Append(new Claim(AuthClaims.Sid, legacy.Id.ToString())),
                principal.Identity?.AuthenticationType ?? CookieAuthenticationDefaults.AuthenticationScheme);
            context.ReplacePrincipal(new ClaimsPrincipal(upgraded));
            context.ShouldRenew = true;
            Logger(services).LogInformation("Legacy session upgraded. UserId={UserId}", userId);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger(services).LogWarning(ex, "Could not verify session against the database.");
            return "db_unavailable";
        }
    }

    private static ILogger Logger(IServiceProvider services) =>
        services.GetRequiredService<ILoggerFactory>().CreateLogger("OAuthLearn.Auth.Session");
}
