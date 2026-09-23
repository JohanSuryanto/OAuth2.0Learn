using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using OAuthLearn.Api.Auth;
using OAuthLearn.Api.Data;

namespace OAuthLearn.Api.Tests;

[Collection(PostgresCollection.Name)]
public class UserServiceTests(PostgresFixture db)
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 23, 8, 0, 0, TimeSpan.Zero));

    private UserService CreateService(AppDbContext context) => new(context, _time);

    private static string NewSubject() => $"sub-{Guid.NewGuid():N}";

    [Fact]
    public async Task First_upsert_inserts_a_new_user()
    {
        var sub = NewSubject();
        await using var context = db.CreateDbContext();

        var user = await CreateService(context).UpsertFromGoogleAsync(sub, "A-" + sub + "@example.com", "Alice", default);

        await using var check = db.CreateDbContext();
        var stored = await check.Users.SingleAsync(u => u.GoogleSubject == sub);
        Assert.Equal(user.Id, stored.Id);
        Assert.Equal("A-" + sub + "@example.com", stored.Email);
        Assert.Equal("Alice", stored.DisplayName);
        Assert.Equal(stored.CreatedAt, stored.LastLoginAt);
        Assert.Equal(0, stored.SessionVersion);
    }

    [Fact]
    public async Task Later_upsert_updates_email_and_last_login_but_keeps_created_at_and_session_version()
    {
        var sub = NewSubject();
        await using (var context = db.CreateDbContext())
        {
            await CreateService(context).UpsertFromGoogleAsync(sub, "old-" + sub + "@example.com", "Old", default);
        }

        var createdAt = _time.GetUtcNow();
        _time.Advance(TimeSpan.FromHours(2));

        await using (var context = db.CreateDbContext())
        {
            var service = CreateService(context);
            var user = await context.Users.SingleAsync(u => u.GoogleSubject == sub);
            await service.RevokeSessionsAsync(user.Id, default);
            await service.UpsertFromGoogleAsync(sub, "new-" + sub + "@example.com", "New", default);
        }

        await using var check = db.CreateDbContext();
        var stored = await check.Users.SingleAsync(u => u.GoogleSubject == sub);
        Assert.Equal("new-" + sub + "@example.com", stored.Email);
        Assert.Equal("Old", stored.DisplayName); // spec 004 R8: Google no longer overwrites a set name
        Assert.Equal(createdAt, stored.CreatedAt);
        Assert.Equal(createdAt.AddHours(2), stored.LastLoginAt);
        Assert.Equal(1, stored.SessionVersion);
    }

    [Fact]
    public async Task Repeated_upserts_with_the_same_subject_leave_exactly_one_row()
    {
        var sub = NewSubject();
        for (var i = 0; i < 3; i++)
        {
            await using var context = db.CreateDbContext();
            await CreateService(context).UpsertFromGoogleAsync(sub, "same-" + sub + "@example.com", null, default);
        }

        await using var check = db.CreateDbContext();
        Assert.Equal(1, await check.Users.CountAsync(u => u.GoogleSubject == sub));
    }

    [Fact]
    public async Task Different_google_subject_with_the_same_email_is_refused()
    {
        // Spec 002 FR-004: one account per normalized email (feature 001 allowed two rows here).
        var email = $"{Guid.NewGuid():N}@example.com";
        await using (var context = db.CreateDbContext())
        {
            await CreateService(context).UpsertFromGoogleAsync(NewSubject(), email, null, default);
        }

        await using (var context = db.CreateDbContext())
        {
            var ex = await Assert.ThrowsAsync<SignInRejectedException>(
                () => CreateService(context).UpsertFromGoogleAsync(NewSubject(), email, null, default));
            Assert.Equal("email_conflict", ex.ReasonCode);
        }

        await using var check = db.CreateDbContext();
        Assert.Equal(1, await check.Users.CountAsync(u => u.Email == email));
    }

    [Fact]
    public async Task Revoke_increments_session_version_and_get_returns_it()
    {
        await using var context = db.CreateDbContext();
        var service = CreateService(context);
        var user = await service.UpsertFromGoogleAsync(NewSubject(), TestHelpers.UniqueEmail("revoke"), null, default);

        await service.RevokeSessionsAsync(user.Id, default);
        await service.RevokeSessionsAsync(user.Id, default);

        Assert.Equal(2, await service.GetSessionVersionAsync(user.Id, default));
        Assert.Null(await service.GetSessionVersionAsync(Guid.NewGuid(), default));
    }
}
