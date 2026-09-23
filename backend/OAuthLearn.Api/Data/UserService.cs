using Microsoft.EntityFrameworkCore;
using OAuthLearn.Api.Auth;

namespace OAuthLearn.Api.Data;

public class UserService(AppDbContext db, TimeProvider time)
{
    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    /// <summary>
    /// Finds or creates the account for a Google sign-in (Google has already confirmed the email is verified).
    /// 1. Match by Google <c>sub</c> (feature 001).
    /// 2. Otherwise link to the account that has this email (spec 002, FR-014): the Google identity proves
    ///    ownership, so any never-verified password is discarded and sessions of an unverified account end.
    /// 3. Otherwise create a new account.
    /// </summary>
    public async Task<User> UpsertFromGoogleAsync(
        string googleSubject, string email, string? displayName, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var normalized = NormalizeEmail(email);

        var user = await db.Users
            .Include(u => u.PasswordCredential)
            .SingleOrDefaultAsync(u => u.GoogleSubject == googleSubject, ct);

        if (user is not null)
        {
            if (user.EmailNormalized != normalized
                && await db.Users.AnyAsync(u => u.EmailNormalized == normalized && u.Id != user.Id, ct))
            {
                throw new SignInRejectedException("email_conflict", "Google email belongs to another account");
            }

            user.Email = email;
            user.EmailNormalized = normalized;
            // Google's name only fills an empty display name; a name the user chose is kept (spec 004, R8).
            user.DisplayName ??= displayName;
            user.LastLoginAt = now;
        }
        else
        {
            user = await db.Users
                .Include(u => u.PasswordCredential)
                .SingleOrDefaultAsync(u => u.EmailNormalized == normalized, ct);

            if (user is not null)
            {
                if (user.GoogleSubject is not null)
                {
                    // The email already belongs to a different Google account.
                    throw new SignInRejectedException("email_conflict", "Email belongs to another Google account");
                }

                await LinkGoogleAsync(user, googleSubject, displayName, now, ct);
            }
            else
            {
                user = new User
                {
                    Id = Guid.CreateVersion7(),
                    GoogleSubject = googleSubject,
                    Email = email,
                    EmailNormalized = normalized,
                    DisplayName = displayName,
                    CreatedAt = now,
                    LastLoginAt = now,
                    EmailVerifiedAt = now,
                    CreatedVia = UserCreatedVia.Google,
                    SessionVersion = 0,
                };
                db.Users.Add(user);
            }
        }

        await db.SaveChangesAsync(ct);
        return user;
    }

    private async Task LinkGoogleAsync(
        User user, string googleSubject, string? displayName, DateTimeOffset now, CancellationToken ct)
    {
        var wasUnverified = user.EmailVerifiedAt is null;
        var hadPending = false;

        user.GoogleSubject = googleSubject;
        user.EmailVerifiedAt ??= now;
        user.DisplayName ??= displayName;
        user.LastLoginAt = now;

        var credential = user.PasswordCredential;
        if (credential?.PendingHash is not null)
        {
            // Whoever set this password never proved they own the email; the real owner just did.
            hadPending = true;
            credential.PendingHash = null;
            credential.PendingSetAt = null;
            if (credential.ActiveHash is null)
            {
                db.PasswordCredentials.Remove(credential);
            }
        }

        if (wasUnverified || hadPending)
        {
            user.SessionVersion++;
        }

        await db.OneTimeTokens
            .Where(t => t.UserId == user.Id && t.Purpose == TokenPurposes.VerifyEmail && t.UsedAt == null)
            .ExecuteDeleteAsync(ct);
    }

    /// <summary>Returns the user's current session version, or null if the user does not exist.</summary>
    public Task<int?> GetSessionVersionAsync(Guid userId, CancellationToken ct) =>
        db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => (int?)u.SessionVersion)
            .SingleOrDefaultAsync(ct);

    /// <summary>Invalidates every existing session of the user ("log out everywhere", FR-019).</summary>
    public Task RevokeSessionsAsync(Guid userId, CancellationToken ct) =>
        db.Users
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.SessionVersion, u => u.SessionVersion + 1), ct);
}
