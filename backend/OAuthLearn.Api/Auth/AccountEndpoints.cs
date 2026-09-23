using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using OAuthLearn.Api.Data;
using OAuthLearn.Api.Throttling;

namespace OAuthLearn.Api.Auth;

public record AccountSummaryResponse(
    string Email,
    string? DisplayName,
    string[] Methods,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSignInAt);

public record SessionInfoResponse(
    Guid Id,
    string? Device,
    string? IpMasked,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt,
    bool Current);

public record ProfileRequest(string? DisplayName);

public record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);

public record SetPasswordRequest(string? NewPassword);

public record ReauthPasswordRequest(string? Password);

/// <summary>/api/account/*: the signed-in user's own account (specs 003 and 004, contracts/account-api.md).</summary>
public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var account = app.MapGroup("/api/account").RequireAuthorization();

        account.MapGet("", async (ClaimsPrincipal principal, AppDbContext db, CancellationToken ct) =>
        {
            var user = await LoadUserAsync(principal, db, ct);
            return user is null ? Results.Unauthorized() : Results.Ok(Summary(user));
        });

        // Everything below changes state: CSRF header required (FR-021).
        var changes = account.MapGroup("").AddEndpointFilter<RequireFetchHeaderFilter>();

        // ---- Profile (US2) ----

        changes.MapPost("/profile", async (ProfileRequest request, HttpContext ctx, AccountSecurityService security) =>
        {
            if (Signed(ctx.User) is not var (userId, sid, authTime))
            {
                return Results.Unauthorized();
            }

            var result = await security.UpdateProfileAsync(userId, request.DisplayName, ctx.RequestAborted);
            if (result.Status != AccountStatus.Ok)
            {
                return ValidationFailed(result.Errors!);
            }

            // New Name claim now, so the header and /me change without signing in again (FR-005, SC-003).
            await SessionIssuer.ReissueAsync(ctx, result.User!, sid, authTime);
            return Results.Ok(Summary(result.User!));
        });

        // ---- Passwords & re-authentication (US3) ----

        changes.MapPost("/password/change", async (ChangePasswordRequest request, HttpContext ctx, AccountSecurityService security) =>
        {
            if (Signed(ctx.User) is not var (userId, sid, authTime))
            {
                return Results.Unauthorized();
            }

            var result = await security.ChangePasswordAsync(
                userId, sid, request.CurrentPassword, request.NewPassword, AttemptLimiter.ClientIp(ctx), ctx.RequestAborted);
            switch (result.Status)
            {
                case AccountStatus.Ok:
                    // session_version moved on; keep this device signed in (research R3).
                    await SessionIssuer.ReissueAsync(ctx, result.User!, sid, authTime);
                    return Results.NoContent();
                case AccountStatus.TooManyAttempts:
                    return TooManyAttempts();
                default:
                    return ValidationFailed(result.Errors!);
            }
        });

        changes.MapPost("/password/set", async (SetPasswordRequest request, HttpContext ctx, AccountSecurityService security) =>
        {
            if (Signed(ctx.User) is not var (userId, sid, _))
            {
                return Results.Unauthorized();
            }

            var result = await security.SetPasswordAsync(userId, sid, request.NewPassword, ctx.RequestAborted);
            return result.Status switch
            {
                AccountStatus.Ok => Results.NoContent(),
                AccountStatus.ReauthRequired => Results.Json(
                    new { code = "reauth_required", methods = await security.ReauthMethodsAsync(userId, ctx.RequestAborted) },
                    statusCode: StatusCodes.Status403Forbidden),
                _ => ValidationFailed(result.Errors!),
            };
        });

        changes.MapPost("/reauth/password", async (ReauthPasswordRequest request, HttpContext ctx, AccountSecurityService security) =>
        {
            if (Signed(ctx.User) is not var (userId, sid, _))
            {
                return Results.Unauthorized();
            }

            var result = await security.ReauthWithPasswordAsync(
                userId, sid, request.Password, AttemptLimiter.ClientIp(ctx), ctx.RequestAborted);
            return result.Status switch
            {
                AccountStatus.Ok => Results.NoContent(),
                AccountStatus.TooManyAttempts => TooManyAttempts(),
                _ => ValidationFailed(result.Errors!),
            };
        });

        // ---- Sessions (US4) ----

        account.MapGet("/sessions", async (HttpContext ctx, SessionStore sessions) =>
        {
            if (Signed(ctx.User) is not var (userId, sid, _))
            {
                return Results.Unauthorized();
            }

            var list = await sessions.ListValidAsync(userId, ctx.RequestAborted);
            var response = list
                .Select(s => new SessionInfoResponse(s.Id, s.DeviceLabel, s.IpMasked, s.CreatedAt, s.LastSeenAt, s.Id == sid))
                .OrderByDescending(s => s.Current)
                .ThenByDescending(s => s.LastSeenAt)
                .ToList();
            return Results.Ok(response);
        });

        changes.MapPost("/sessions/{id:guid}/revoke", async (Guid id, HttpContext ctx, SessionStore sessions, ILoggerFactory logs) =>
        {
            if (Signed(ctx.User) is not var (userId, sid, _))
            {
                return Results.Unauthorized();
            }

            if (id == sid)
            {
                return ValidationFailed(new Dictionary<string, string> { ["id"] = "use_logout" });
            }

            var valid = (await sessions.ListValidAsync(userId, ctx.RequestAborted)).Any(s => s.Id == id);
            if (!valid || !await sessions.RevokeAsync(userId, id, ctx.RequestAborted))
            {
                return Results.NotFound();
            }

            logs.CreateLogger("OAuthLearn.Auth.Sessions").LogInformation("Session ended by user. UserId={UserId}", userId);
            return Results.NoContent();
        });

        changes.MapPost("/sessions/revoke-others", async (HttpContext ctx, AppDbContext db, SessionStore sessions, ILoggerFactory logs) =>
        {
            if (Signed(ctx.User) is not var (userId, sid, authTime))
            {
                return Results.Unauthorized();
            }

            var user = await db.Users.SingleAsync(u => u.Id == userId, ctx.RequestAborted);
            user.SessionVersion++; // also ends sessions from before per-device tracking (research R1)
            await db.SaveChangesAsync(ctx.RequestAborted);
            await sessions.RevokeAllExceptAsync(userId, sid, ctx.RequestAborted);
            await SessionIssuer.ReissueAsync(ctx, user, sid, authTime);

            logs.CreateLogger("OAuthLearn.Auth.Sessions").LogInformation("Other sessions ended by user. UserId={UserId}", userId);
            return Results.NoContent();
        });

        return app;
    }

    /// <summary>The signed-in user and current session from the cookie; null without a tracked session.</summary>
    private static (Guid UserId, Guid Sid, DateTimeOffset AuthTime)? Signed(ClaimsPrincipal principal) =>
        AuthClaims.UserId(principal) is { } userId && AuthClaims.CurrentSession(principal) is { } current
            ? (userId, current.Sid, current.AuthTime)
            : null;

    private static async Task<User?> LoadUserAsync(ClaimsPrincipal principal, AppDbContext db, CancellationToken ct) =>
        AuthClaims.UserId(principal) is { } userId
            ? await db.Users.AsNoTracking().Include(u => u.PasswordCredential).SingleOrDefaultAsync(u => u.Id == userId, ct)
            : null;

    private static AccountSummaryResponse Summary(User user) =>
        new(user.Email, user.DisplayName, Methods(user), user.CreatedAt, user.LastLoginAt);

    /// <summary>"google" if linked to Google; "password" only for an active (verified) password (FR-012).</summary>
    private static string[] Methods(User user)
    {
        var methods = new List<string>(2);
        if (user.GoogleSubject is not null)
        {
            methods.Add("google");
        }

        if (user.PasswordCredential?.ActiveHash is not null)
        {
            methods.Add("password");
        }

        return [.. methods];
    }

    private static IResult TooManyAttempts() =>
        Results.Json(new { code = "too_many_attempts" }, statusCode: StatusCodes.Status429TooManyRequests);

    private static IResult ValidationFailed(IReadOnlyDictionary<string, string> errors) =>
        Results.Json(new { errors }, statusCode: StatusCodes.Status400BadRequest);
}
