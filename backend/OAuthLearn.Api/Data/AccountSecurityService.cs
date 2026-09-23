using Microsoft.EntityFrameworkCore;
using OAuthLearn.Api.Auth;
using OAuthLearn.Api.Passwords;
using OAuthLearn.Api.Throttling;
using static OAuthLearn.Api.Throttling.AttemptLimiter;

namespace OAuthLearn.Api.Data;

/// <summary>
/// Signed-in account changes (spec 004): profile, change/set password, re-authentication.
/// Wrong passwords share the sign-in throttle (FR-011). Nothing here logs names, passwords or hashes.
/// </summary>
public class AccountSecurityService(
    AppDbContext db,
    PasswordHashing hashing,
    CommonPasswords commonPasswords,
    SessionStore sessions,
    AttemptLimiter limiter,
    TimeProvider time,
    ILogger<AccountSecurityService> logger)
{
    private static readonly TimeSpan SignInWindow = TimeSpan.FromMinutes(15);
    private const int SignInEmailLimit = 5;
    private const int SignInIpLimit = 20;

    public static class Codes
    {
        public const string CurrentPasswordIncorrect = "current_password_incorrect";
        public const string NoPassword = "no_password";
        public const string PasswordAlreadySet = "password_already_set";
    }

    // ---- Profile (US2) ----

    public async Task<AccountResult> UpdateProfileAsync(Guid userId, string? displayName, CancellationToken ct)
    {
        var error = PasswordPolicy.ValidateDisplayName(displayName);
        if (error is not null)
        {
            return AccountResult.Invalid("displayName", error);
        }

        var user = await db.Users.Include(u => u.PasswordCredential).SingleAsync(u => u.Id == userId, ct);
        user.DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Profile updated. UserId={UserId}", userId);
        return new AccountResult(AccountStatus.Ok, User: user);
    }

    // ---- Passwords (US3) ----

    /// <summary>Always needs the current password (FR-010). Ends every other session (FR-007).</summary>
    public async Task<AccountResult> ChangePasswordAsync(
        Guid userId, Guid sid, string? currentPassword, string? newPassword, string clientIp, CancellationToken ct)
    {
        var user = await db.Users.Include(u => u.PasswordCredential).SingleAsync(u => u.Id == userId, ct);
        if (user.PasswordCredential?.ActiveHash is null)
        {
            return AccountResult.Invalid("currentPassword", Codes.NoPassword);
        }

        if (await IsThrottledAsync(user.EmailNormalized, clientIp, ct))
        {
            return AccountResult.Of(AccountStatus.TooManyAttempts);
        }

        if (hashing.Verify(user.PasswordCredential.ActiveHash, currentPassword ?? "") == PasswordCheck.Failed)
        {
            await RecordFailureAsync(user.EmailNormalized, clientIp, ct);
            logger.LogInformation("Password change failed: wrong current password. UserId={UserId}", userId);
            return AccountResult.Invalid("currentPassword", Codes.CurrentPasswordIncorrect);
        }

        var policyError = PasswordPolicy.ValidateNewPassword(newPassword, user.Email, commonPasswords);
        if (policyError is not null)
        {
            return AccountResult.Invalid("newPassword", policyError);
        }

        var now = time.GetUtcNow();
        user.PasswordCredential.ActiveHash = hashing.Hash(newPassword!);
        user.PasswordCredential.ActiveSetAt = now;
        user.PasswordCredential.PendingHash = null;
        user.PasswordCredential.PendingSetAt = null;
        user.SessionVersion++; // also ends sessions from before per-device tracking (research R1)
        await db.SaveChangesAsync(ct);

        await sessions.RevokeAllExceptAsync(userId, sid, ct);
        await sessions.MarkAuthenticatedAsync(sid, ct);
        await limiter.ClearAsync(Buckets.SignInFailEmail, user.EmailNormalized, ct);

        logger.LogInformation("Password changed; other sessions ended. UserId={UserId}", userId);
        return new AccountResult(AccountStatus.Ok, User: user);
    }

    /// <summary>For accounts without an active password; needs a recent authentication (FR-008, FR-009).</summary>
    public async Task<AccountResult> SetPasswordAsync(Guid userId, Guid sid, string? newPassword, CancellationToken ct)
    {
        var user = await db.Users.Include(u => u.PasswordCredential).SingleAsync(u => u.Id == userId, ct);
        if (user.PasswordCredential?.ActiveHash is not null)
        {
            return AccountResult.Invalid("newPassword", Codes.PasswordAlreadySet);
        }

        var session = await sessions.GetAsync(sid, userId, ct);
        if (session is null || !sessions.IsRecentlyAuthenticated(session))
        {
            return AccountResult.Of(AccountStatus.ReauthRequired);
        }

        var policyError = PasswordPolicy.ValidateNewPassword(newPassword, user.Email, commonPasswords);
        if (policyError is not null)
        {
            return AccountResult.Invalid("newPassword", policyError);
        }

        var now = time.GetUtcNow();
        user.PasswordCredential ??= new PasswordCredential { UserId = user.Id };
        user.PasswordCredential.ActiveHash = hashing.Hash(newPassword!);
        user.PasswordCredential.ActiveSetAt = now;
        user.PasswordCredential.PendingHash = null; // a pending registration is replaced (FR-008)
        user.PasswordCredential.PendingSetAt = null;
        user.EmailVerifiedAt ??= now;
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Password set. UserId={UserId}", userId);
        return new AccountResult(AccountStatus.Ok, User: user);
    }

    /// <summary>Which re-authentication methods this account can use.</summary>
    public async Task<string[]> ReauthMethodsAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().Include(u => u.PasswordCredential).SingleAsync(u => u.Id == userId, ct);
        return user.PasswordCredential?.ActiveHash is not null ? ["password"] : user.GoogleSubject is not null ? ["google"] : [];
    }

    // ---- Re-authentication (US3) ----

    public async Task<AccountResult> ReauthWithPasswordAsync(
        Guid userId, Guid sid, string? password, string clientIp, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().Include(u => u.PasswordCredential).SingleAsync(u => u.Id == userId, ct);
        if (await IsThrottledAsync(user.EmailNormalized, clientIp, ct))
        {
            return AccountResult.Of(AccountStatus.TooManyAttempts);
        }

        // One hash check whether or not a password exists (002 research R2).
        if (hashing.Verify(user.PasswordCredential?.ActiveHash, password ?? "") == PasswordCheck.Failed)
        {
            await RecordFailureAsync(user.EmailNormalized, clientIp, ct);
            logger.LogInformation("Re-authentication failed. UserId={UserId}", userId);
            return AccountResult.Invalid("password", Codes.CurrentPasswordIncorrect);
        }

        await sessions.MarkAuthenticatedAsync(sid, ct);
        await limiter.ClearAsync(Buckets.SignInFailEmail, user.EmailNormalized, ct);
        logger.LogInformation("Re-authenticated with password. UserId={UserId}", userId);
        return AccountResult.Ok;
    }

    /// <summary>Google confirmed the user again (research R6); only the linked Google account counts.</summary>
    public async Task CompleteGoogleReauthAsync(Guid userId, Guid sid, string googleSubject, CancellationToken ct)
    {
        var linked = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.GoogleSubject).SingleOrDefaultAsync(ct);
        if (linked is null || linked != googleSubject)
        {
            logger.LogWarning("Re-authentication with a different Google account refused. UserId={UserId}", userId);
            throw new SignInRejectedException("reauth_mismatch", "Google account is not the one linked to this account");
        }

        await sessions.MarkAuthenticatedAsync(sid, ct);
        logger.LogInformation("Re-authenticated with Google. UserId={UserId}", userId);
    }

    private async Task<bool> IsThrottledAsync(string normalizedEmail, string clientIp, CancellationToken ct) =>
        await limiter.IsLockedOutAsync(Buckets.SignInFailEmail, normalizedEmail, SignInEmailLimit, SignInWindow, ct)
        || await limiter.IsLockedOutAsync(Buckets.SignInFailIp, clientIp, SignInIpLimit, SignInWindow, ct);

    private async Task RecordFailureAsync(string normalizedEmail, string clientIp, CancellationToken ct)
    {
        await limiter.RecordAsync(Buckets.SignInFailEmail, normalizedEmail, ct);
        await limiter.RecordAsync(Buckets.SignInFailIp, clientIp, ct);
    }
}
