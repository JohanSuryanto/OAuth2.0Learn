using Microsoft.EntityFrameworkCore;
using OAuthLearn.Api.Auth;

namespace OAuthLearn.Api.Data;

/// <summary>Per-device sessions (spec 004, research R1, R4).</summary>
public class SessionStore(AppDbContext db, TimeProvider time)
{
    public static readonly TimeSpan TouchInterval = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan IdleWindow = TimeSpan.FromMinutes(60);
    public static readonly TimeSpan AbsoluteWindow = TimeSpan.FromHours(8);
    public static readonly TimeSpan RecentAuthWindow = TimeSpan.FromMinutes(10);

    public async Task<UserSession> CreateAsync(Guid userId, HttpContext ctx, DateTimeOffset authTime, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var session = new UserSession
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            CreatedAt = now,
            AuthTime = authTime,
            LastAuthAt = now,
            LastSeenAt = now,
            DeviceLabel = UserAgentDescriber.Describe(ctx.Request.Headers.UserAgent),
            IpMasked = IpMasker.Mask(ctx.Connection.RemoteIpAddress),
        };
        db.UserSessions.Add(session);
        await db.SaveChangesAsync(ct);
        return session;
    }

    /// <summary>
    /// Row for a cookie issued before this feature (research R2): unknown device, times taken from auth_time.
    /// </summary>
    public async Task<UserSession> CreateLegacyAsync(Guid userId, HttpContext ctx, DateTimeOffset authTime, CancellationToken ct)
    {
        var session = new UserSession
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            CreatedAt = authTime,
            AuthTime = authTime,
            LastAuthAt = authTime,
            LastSeenAt = time.GetUtcNow(),
            DeviceLabel = null,
            IpMasked = IpMasker.Mask(ctx.Connection.RemoteIpAddress),
        };
        db.UserSessions.Add(session);
        await db.SaveChangesAsync(ct);
        return session;
    }

    /// <summary>The session (tracked) and its user's current session_version, in one query.</summary>
    public async Task<(UserSession? Session, int? SessionVersion)> FindAsync(Guid sid, Guid userId, CancellationToken ct)
    {
        var row = await (
                from s in db.UserSessions
                join u in db.Users on s.UserId equals u.Id
                where s.Id == sid && s.UserId == userId
                select new { Session = s, u.SessionVersion })
            .SingleOrDefaultAsync(ct);
        return (row?.Session, row?.SessionVersion);
    }

    public Task<UserSession?> GetAsync(Guid sid, Guid userId, CancellationToken ct) =>
        db.UserSessions.SingleOrDefaultAsync(s => s.Id == sid && s.UserId == userId, ct);

    /// <summary>Refreshes last_seen_at at most every 5 minutes (FR-013 allows ±5 min); occasionally purges old rows.</summary>
    public async Task TouchAsync(UserSession session, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        if (now - session.LastSeenAt < TouchInterval)
        {
            return;
        }

        session.LastSeenAt = now;
        await db.SaveChangesAsync(ct);

        if (Random.Shared.Next(100) == 0)
        {
            var cutoff = now - AbsoluteWindow;
            await db.UserSessions.Where(s => s.AuthTime < cutoff).ExecuteDeleteAsync(ct);
        }
    }

    /// <summary>Sessions that can still be used, as far as the server can tell.</summary>
    public Task<List<UserSession>> ListValidAsync(Guid userId, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var idleCutoff = now - IdleWindow;
        var absoluteCutoff = now - AbsoluteWindow;
        return db.UserSessions.AsNoTracking()
            .Where(s => s.UserId == userId && s.RevokedAt == null && s.LastSeenAt > idleCutoff && s.AuthTime > absoluteCutoff)
            .OrderByDescending(s => s.LastSeenAt)
            .ToListAsync(ct);
    }

    /// <summary>Ends one of the user's sessions. False when it isn't theirs or is already ended.</summary>
    public async Task<bool> RevokeAsync(Guid userId, Guid sid, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        return await db.UserSessions
            .Where(s => s.Id == sid && s.UserId == userId && s.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), ct) > 0;
    }

    /// <summary>Ends all of the user's sessions except <paramref name="keepSid"/> (all of them when null).</summary>
    public Task RevokeAllExceptAsync(Guid userId, Guid? keepSid, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        return db.UserSessions
            .Where(s => s.UserId == userId && s.RevokedAt == null && (keepSid == null || s.Id != keepSid))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), ct);
    }

    /// <summary>Records a fresh proof of identity for this session (the 10-minute rule).</summary>
    public Task MarkAuthenticatedAsync(Guid sid, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        return db.UserSessions.Where(s => s.Id == sid).ExecuteUpdateAsync(s => s.SetProperty(x => x.LastAuthAt, now), ct);
    }

    public bool IsRecentlyAuthenticated(UserSession session) => time.GetUtcNow() - session.LastAuthAt <= RecentAuthWindow;
}
