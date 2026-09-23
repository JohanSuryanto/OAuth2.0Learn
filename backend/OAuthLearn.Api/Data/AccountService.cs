using Microsoft.EntityFrameworkCore;
using OAuthLearn.Api.Email;
using OAuthLearn.Api.Passwords;
using OAuthLearn.Api.Throttling;
using static OAuthLearn.Api.Throttling.AttemptLimiter;

namespace OAuthLearn.Api.Data;

public enum AccountStatus
{
    Ok,
    Invalid,
    TooManyAttempts,
    TokenInvalid,
    PasswordMismatch,
    InvalidCredentials,
    EmailNotVerified,
    ReauthRequired,
}

public sealed record AccountResult(AccountStatus Status, IReadOnlyDictionary<string, string>? Errors = null, User? User = null)
{
    public static readonly AccountResult Ok = new(AccountStatus.Ok);

    public static AccountResult Invalid(string field, string code) =>
        new(AccountStatus.Invalid, new Dictionary<string, string> { [field] = code });

    public static AccountResult Of(AccountStatus status) => new(status);
}

/// <summary>
/// Email/password flows (spec 002). Responses never reveal whether an email is registered: registration always
/// hashes before branching, sign-in always runs two hash checks, forgot-password always "accepts".
/// Nothing here logs passwords, tokens, or the email of a failed attempt (FR-018).
/// </summary>
public class AccountService(
    AppDbContext db,
    PasswordHashing hashing,
    CommonPasswords commonPasswords,
    TokenService tokens,
    SessionStore sessions,
    IEmailSender email,
    AttemptLimiter limiter,
    IConfiguration config,
    TimeProvider time,
    ILogger<AccountService> logger)
{
    private static readonly TimeSpan SignInWindow = TimeSpan.FromMinutes(15);
    private const int SignInEmailLimit = 5;
    private const int SignInIpLimit = 20;
    private static readonly TimeSpan HourWindow = TimeSpan.FromHours(1);
    private const int RegisterIpLimit = 5;
    private const int ResetEmailLimit = 3;
    private const int ResetIpLimit = 10;

    private string Origin => config["Frontend:Origin"]!;

    // ---- Registration (US1) ----

    public async Task<AccountResult> RegisterAsync(
        string? emailInput, string? password, string? displayName, string clientIp, CancellationToken ct)
    {
        var errors = new Dictionary<string, string>();
        AddIfError(errors, "email", PasswordPolicy.ValidateEmail(emailInput));
        AddIfError(errors, "password", PasswordPolicy.ValidateNewPassword(password, emailInput ?? "", commonPasswords));
        AddIfError(errors, "displayName", PasswordPolicy.ValidateDisplayName(displayName));
        if (errors.Count > 0)
        {
            return new AccountResult(AccountStatus.Invalid, errors);
        }

        if (await limiter.CountSinceAsync(Buckets.RegisterIp, clientIp, HourWindow, ct) >= RegisterIpLimit)
        {
            logger.LogWarning("Registration throttled. Bucket={Bucket}", Buckets.RegisterIp);
            return AccountResult.Of(AccountStatus.TooManyAttempts);
        }

        await limiter.RecordAsync(Buckets.RegisterIp, clientIp, ct);

        // Hash before looking anything up so every branch costs the same (research R2).
        var hash = hashing.Hash(password!);
        var enteredEmail = emailInput!.Trim();
        var normalized = UserService.NormalizeEmail(enteredEmail);
        var now = time.GetUtcNow();

        var user = await db.Users
            .Include(u => u.PasswordCredential)
            .SingleOrDefaultAsync(u => u.EmailNormalized == normalized, ct);

        if (user?.PasswordCredential?.ActiveHash is not null)
        {
            // Already has a password: change nothing, tell the owner (FR-012).
            var (noticeSubject, noticeBody) = EmailTemplates.RegistrationAttempt(Origin);
            await email.SendAsync(user.Email, noticeSubject, noticeBody, ct);
            logger.LogInformation("Registration requested for an existing account. UserId={UserId}", user.Id);
            return AccountResult.Ok;
        }

        if (user is null)
        {
            user = new User
            {
                Id = Guid.CreateVersion7(),
                GoogleSubject = null,
                Email = enteredEmail,
                EmailNormalized = normalized,
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim(),
                CreatedAt = now,
                LastLoginAt = now,
                EmailVerifiedAt = null,
                CreatedVia = UserCreatedVia.Password,
            };
            db.Users.Add(user);
        }

        // New account, Google-only account, or an earlier unverified registration: the latest password wins and
        // older links stop working (research R4).
        user.PasswordCredential ??= new PasswordCredential { UserId = user.Id };
        user.PasswordCredential.PendingHash = hash;
        user.PasswordCredential.PendingSetAt = now;
        await db.SaveChangesAsync(ct);

        await SendVerificationAsync(user, ct);
        logger.LogInformation("Registration requested. UserId={UserId}", user.Id);
        return AccountResult.Ok;
    }

    // ---- Verification (US1) ----

    public async Task<AccountResult> VerifyEmailAsync(string? token, string? password, string clientIp, CancellationToken ct)
    {
        var record = await tokens.FindValidAsync(token, TokenPurposes.VerifyEmail, ct);
        if (record is null)
        {
            return AccountResult.Of(AccountStatus.TokenInvalid);
        }

        var user = await db.Users.Include(u => u.PasswordCredential).SingleAsync(u => u.Id == record.UserId, ct);
        if (await IsSignInThrottledAsync(user.EmailNormalized, clientIp, ct))
        {
            return AccountResult.Of(AccountStatus.TooManyAttempts);
        }

        var credential = user.PasswordCredential;
        if (hashing.Verify(credential?.PendingHash, password ?? "") == PasswordCheck.Failed)
        {
            await RecordSignInFailureAsync(user.EmailNormalized, clientIp, ct);
            logger.LogInformation("Email verification failed: password mismatch. UserId={UserId}", user.Id);
            return AccountResult.Of(AccountStatus.PasswordMismatch);
        }

        var now = time.GetUtcNow();
        credential!.ActiveHash = credential.PendingHash;
        credential.ActiveSetAt = now;
        credential.PendingHash = null;
        credential.PendingSetAt = null;
        user.EmailVerifiedAt ??= now;
        user.LastLoginAt = now;
        record.UsedAt = now;
        await db.SaveChangesAsync(ct);
        await limiter.ClearAsync(Buckets.SignInFailEmail, user.EmailNormalized, ct);

        logger.LogInformation("Email verified; signed in. UserId={UserId}", user.Id);
        return new AccountResult(AccountStatus.Ok, User: user);
    }

    // ---- Sign-in (US2, US3) ----

    public async Task<AccountResult> SignInAsync(string? emailInput, string? password, string clientIp, CancellationToken ct)
    {
        var normalized = UserService.NormalizeEmail(emailInput ?? "");
        password ??= "";

        // Refused attempts are not counted (FR-011).
        if (await IsSignInThrottledAsync(normalized, clientIp, ct))
        {
            return AccountResult.Of(AccountStatus.TooManyAttempts);
        }

        var user = normalized.Length == 0
            ? null
            : await db.Users.Include(u => u.PasswordCredential).SingleOrDefaultAsync(u => u.EmailNormalized == normalized, ct);
        var credential = user?.PasswordCredential;

        // Always exactly two hash checks, whatever exists (research R2).
        var active = hashing.Verify(credential?.ActiveHash, password);
        var pending = hashing.Verify(credential?.PendingHash, password);

        if (user is not null && active != PasswordCheck.Failed && user.EmailVerifiedAt is not null)
        {
            user.LastLoginAt = time.GetUtcNow();
            if (active == PasswordCheck.SuccessRehashNeeded)
            {
                credential!.ActiveHash = hashing.Hash(password);
            }

            await db.SaveChangesAsync(ct);
            await limiter.ClearAsync(Buckets.SignInFailEmail, normalized, ct);
            logger.LogInformation("Password sign-in succeeded. UserId={UserId}", user.Id);
            return new AccountResult(AccountStatus.Ok, User: user);
        }

        if (user is not null && pending != PasswordCheck.Failed)
        {
            // Right password, email not verified yet: send a fresh link (FR-012). Not a failure.
            await SendVerificationAsync(user, ct);
            logger.LogInformation("Password sign-in for unverified account; new link sent. UserId={UserId}", user.Id);
            return AccountResult.Of(AccountStatus.EmailNotVerified);
        }

        await RecordSignInFailureAsync(normalized, clientIp, ct);
        logger.LogInformation("Password sign-in failed.");
        return AccountResult.Of(AccountStatus.InvalidCredentials);
    }

    // ---- Password reset (US5) ----

    public async Task<AccountResult> ForgotPasswordAsync(string? emailInput, string clientIp, CancellationToken ct)
    {
        var emailError = PasswordPolicy.ValidateEmail(emailInput);
        if (emailError is not null)
        {
            return AccountResult.Invalid("email", emailError);
        }

        var normalized = UserService.NormalizeEmail(emailInput!);
        if (await limiter.CountSinceAsync(Buckets.ResetEmail, normalized, HourWindow, ct) >= ResetEmailLimit
            || await limiter.CountSinceAsync(Buckets.ResetIp, clientIp, HourWindow, ct) >= ResetIpLimit)
        {
            // Same answer as success (FR-016).
            logger.LogWarning("Password reset request throttled.");
            return AccountResult.Ok;
        }

        await limiter.RecordAsync(Buckets.ResetEmail, normalized, ct);
        await limiter.RecordAsync(Buckets.ResetIp, clientIp, ct);

        var user = await db.Users.SingleOrDefaultAsync(u => u.EmailNormalized == normalized, ct);
        if (user is not null)
        {
            var token = await tokens.CreateAsync(user.Id, TokenPurposes.ResetPassword, TokenService.ResetPasswordLifetime, ct);
            var (subject, body) = EmailTemplates.ResetPassword(Origin, token);
            await email.SendAsync(user.Email, subject, body, ct);
        }

        logger.LogInformation("Password reset requested.");
        return AccountResult.Ok;
    }

    public async Task<AccountResult> ResetPasswordAsync(string? token, string? newPassword, CancellationToken ct)
    {
        var record = await tokens.FindValidAsync(token, TokenPurposes.ResetPassword, ct);
        if (record is null)
        {
            return AccountResult.Of(AccountStatus.TokenInvalid);
        }

        var user = await db.Users.Include(u => u.PasswordCredential).SingleAsync(u => u.Id == record.UserId, ct);
        var passwordError = PasswordPolicy.ValidateNewPassword(newPassword, user.Email, commonPasswords);
        if (passwordError is not null)
        {
            return AccountResult.Invalid("newPassword", passwordError);
        }

        var now = time.GetUtcNow();
        user.PasswordCredential ??= new PasswordCredential { UserId = user.Id };
        user.PasswordCredential.ActiveHash = hashing.Hash(newPassword!);
        user.PasswordCredential.ActiveSetAt = now;
        user.PasswordCredential.PendingHash = null;
        user.PasswordCredential.PendingSetAt = null;
        user.EmailVerifiedAt ??= now;
        user.SessionVersion++; // every existing session ends (FR-016)
        await db.SaveChangesAsync(ct);

        await tokens.DeleteAllForUserAsync(user.Id, ct);
        await sessions.RevokeAllExceptAsync(user.Id, null, ct); // every device, including this one (spec 004, US4-5)
        await limiter.ClearAsync(Buckets.SignInFailEmail, user.EmailNormalized, ct);

        logger.LogInformation("Password reset completed. UserId={UserId}", user.Id);
        return AccountResult.Ok;
    }

    // ---- helpers ----

    private async Task SendVerificationAsync(User user, CancellationToken ct)
    {
        var token = await tokens.CreateAsync(user.Id, TokenPurposes.VerifyEmail, TokenService.VerifyEmailLifetime, ct);
        var (subject, body) = EmailTemplates.VerifyEmail(Origin, token);
        await email.SendAsync(user.Email, subject, body, ct);
    }

    private async Task<bool> IsSignInThrottledAsync(string normalizedEmail, string clientIp, CancellationToken ct)
    {
        if (await limiter.IsLockedOutAsync(Buckets.SignInFailEmail, normalizedEmail, SignInEmailLimit, SignInWindow, ct))
        {
            logger.LogWarning("Sign-in throttled. Bucket={Bucket}", Buckets.SignInFailEmail);
            return true;
        }

        if (await limiter.IsLockedOutAsync(Buckets.SignInFailIp, clientIp, SignInIpLimit, SignInWindow, ct))
        {
            logger.LogWarning("Sign-in throttled. Bucket={Bucket}", Buckets.SignInFailIp);
            return true;
        }

        return false;
    }

    private async Task RecordSignInFailureAsync(string normalizedEmail, string clientIp, CancellationToken ct)
    {
        await limiter.RecordAsync(Buckets.SignInFailEmail, normalizedEmail, ct);
        await limiter.RecordAsync(Buckets.SignInFailIp, clientIp, ct);
    }

    private static void AddIfError(Dictionary<string, string> errors, string field, string? code)
    {
        if (code is not null)
        {
            errors[field] = code;
        }
    }
}
