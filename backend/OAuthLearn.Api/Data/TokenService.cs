using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace OAuthLearn.Api.Data;

/// <summary>Single-use emailed tokens (research R5): 32 random bytes, only SHA-256 stored, one active per purpose.</summary>
public class TokenService(AppDbContext db, TimeProvider time)
{
    public static readonly TimeSpan VerifyEmailLifetime = TimeSpan.FromHours(24);
    public static readonly TimeSpan ResetPasswordLifetime = TimeSpan.FromMinutes(30);

    /// <summary>Creates a token, invalidating the user's other unused tokens for the same purpose.</summary>
    public async Task<string> CreateAsync(Guid userId, string purpose, TimeSpan lifetime, CancellationToken ct)
    {
        await db.OneTimeTokens
            .Where(t => t.UserId == userId && t.Purpose == purpose && t.UsedAt == null)
            .ExecuteDeleteAsync(ct);

        var raw = RandomNumberGenerator.GetBytes(32);
        var now = time.GetUtcNow();
        db.OneTimeTokens.Add(new OneTimeToken
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            Purpose = purpose,
            TokenHash = SHA256.HashData(raw),
            CreatedAt = now,
            ExpiresAt = now + lifetime,
        });
        await db.SaveChangesAsync(ct);

        return WebEncoders.Base64UrlEncode(raw);
    }

    /// <summary>The token if it exists for this purpose, is unused and unexpired; otherwise null.</summary>
    public async Task<OneTimeToken?> FindValidAsync(string? rawToken, string purpose, CancellationToken ct)
    {
        byte[] raw;
        try
        {
            raw = WebEncoders.Base64UrlDecode(rawToken ?? "");
        }
        catch (FormatException)
        {
            return null;
        }

        if (raw.Length != 32)
        {
            return null;
        }

        var hash = SHA256.HashData(raw);
        var now = time.GetUtcNow();
        return await db.OneTimeTokens.SingleOrDefaultAsync(
            t => t.TokenHash == hash && t.Purpose == purpose && t.UsedAt == null && t.ExpiresAt > now, ct);
    }

    public Task DeleteAllForUserAsync(Guid userId, CancellationToken ct) =>
        db.OneTimeTokens.Where(t => t.UserId == userId).ExecuteDeleteAsync(ct);
}
