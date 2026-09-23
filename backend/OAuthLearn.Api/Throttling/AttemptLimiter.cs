using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using OAuthLearn.Api.Data;

namespace OAuthLearn.Api.Throttling;

/// <summary>
/// Database-backed throttling (research R7). Keys are HMAC-SHA256(Auth:LookupHashKey, bucket:value), so no plain
/// emails or IPs are stored. All times come from <see cref="TimeProvider"/>.
/// </summary>
public class AttemptLimiter(AppDbContext db, TimeProvider time, IConfiguration config)
{
    public static class Buckets
    {
        public const string SignInFailEmail = "signin_fail_email";
        public const string SignInFailIp = "signin_fail_ip";
        public const string RegisterIp = "register_ip";
        public const string ResetEmail = "reset_email";
        public const string ResetIp = "reset_ip";
    }

    private static readonly TimeSpan Retention = TimeSpan.FromHours(24);

    public static string ClientIp(HttpContext ctx) => ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    public byte[] Key(string bucket, string value)
    {
        var secret = Convert.FromBase64String(config["Auth:LookupHashKey"]
            ?? throw new InvalidOperationException("Auth:LookupHashKey is not configured."));
        return HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(bucket + ":" + value));
    }

    public async Task RecordAsync(string bucket, string value, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        db.RateLimitEvents.Add(new RateLimitEvent { Bucket = bucket, KeyHash = Key(bucket, value), OccurredAt = now });
        await db.SaveChangesAsync(ct);

        if (Random.Shared.Next(100) == 0)
        {
            var cutoff = now - Retention;
            await db.RateLimitEvents.Where(e => e.OccurredAt < cutoff).ExecuteDeleteAsync(ct);
        }
    }

    public Task<int> CountSinceAsync(string bucket, string value, TimeSpan window, CancellationToken ct)
    {
        var key = Key(bucket, value);
        var since = time.GetUtcNow() - window;
        return db.RateLimitEvents.CountAsync(e => e.Bucket == bucket && e.KeyHash == key && e.OccurredAt > since, ct);
    }

    /// <summary>
    /// Locked when the <paramref name="limit"/> newest failures fall within <paramref name="window"/> of each other
    /// and the newest is less than <paramref name="window"/> old: "refused until 15 minutes after the last failure".
    /// </summary>
    public async Task<bool> IsLockedOutAsync(string bucket, string value, int limit, TimeSpan window, CancellationToken ct)
    {
        var key = Key(bucket, value);
        var recent = await db.RateLimitEvents.AsNoTracking()
            .Where(e => e.Bucket == bucket && e.KeyHash == key)
            .OrderByDescending(e => e.OccurredAt)
            .Take(limit)
            .Select(e => e.OccurredAt)
            .ToListAsync(ct);

        if (recent.Count < limit)
        {
            return false;
        }

        var newest = recent[0];
        var oldest = recent[^1];
        return time.GetUtcNow() - newest < window && newest - oldest <= window;
    }

    public Task ClearAsync(string bucket, string value, CancellationToken ct)
    {
        var key = Key(bucket, value);
        return db.RateLimitEvents.Where(e => e.Bucket == bucket && e.KeyHash == key).ExecuteDeleteAsync(ct);
    }
}
