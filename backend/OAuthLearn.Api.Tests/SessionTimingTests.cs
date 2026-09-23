using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using OAuthLearn.Api.Auth;

namespace OAuthLearn.Api.Tests;

/// <summary>Session timing on /api/auth/me mirrors the server's real rules (spec 003, research R3).</summary>
[Collection(PostgresCollection.Name)]
public sealed class SessionTimingTests(PostgresFixture db) : IDisposable
{
    private const string Password = "correct horse battery";
    private readonly TestAppFactory _factory = new(db);

    public void Dispose() => _factory.Dispose();

    private async Task<HttpClient> SignedInClientAsync()
    {
        var email = TestHelpers.UniqueEmail("timing");
        var client = _factory.CreateHttpsClient();
        await client.RegisterAndVerifyAsync(db, email, Password); // verification signs the client in
        return client;
    }

    private static async Task<JsonElement> SessionAsync(HttpClient client)
    {
        var me = await client.GetAsync("/api/auth/me");
        me.EnsureSuccessStatusCode();
        return (await me.JsonAsync()).GetProperty("session");
    }

    private static int Idle(JsonElement session) => session.GetProperty("idleSecondsLeft").GetInt32();

    private static int Absolute(JsonElement session) => session.GetProperty("absoluteSecondsLeft").GetInt32();

    [Fact]
    public async Task Right_after_sign_in_shows_60_minutes_and_8_hours()
    {
        var client = await SignedInClientAsync();

        var session = await SessionAsync(client);

        Assert.InRange(Idle(session), 3598, 3600);
        Assert.InRange(Absolute(session), 28798, 28800);
        Assert.Equal(_factory.Time.GetUtcNow().AddHours(1), session.GetProperty("idleExpiresAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task Before_half_the_window_the_expiry_does_not_move()
    {
        var client = await SignedInClientAsync();

        _factory.Time.Advance(TimeSpan.FromMinutes(10));
        var session = await SessionAsync(client);

        Assert.InRange(Idle(session), 2998, 3000);
    }

    [Fact]
    public async Task After_half_the_window_the_request_renews_the_cookie()
    {
        var client = await SignedInClientAsync();

        _factory.Time.Advance(TimeSpan.FromMinutes(40));
        Assert.InRange(Idle(await SessionAsync(client)), 3598, 3600);

        // Proof the cookie really was reissued: a minute later the new ticket (issued 1 min ago) has 59 min left.
        // Without the renewal the old ticket would still be past half its window and report 60 min again.
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        Assert.InRange(Idle(await SessionAsync(client)), 3538, 3540);
    }

    [Fact]
    public async Task Absolute_limit_keeps_counting_down_through_renewals()
    {
        var client = await SignedInClientAsync();

        for (var i = 0; i < 11; i++)
        {
            _factory.Time.Advance(TimeSpan.FromMinutes(40));
            await SessionAsync(client); // keeps the session alive
        }

        _factory.Time.Advance(TimeSpan.FromMinutes(30)); // 7 h 50 min after sign-in
        var session = await SessionAsync(client);

        Assert.InRange(Absolute(session), 598, 600);
    }

    // ---- "Stay signed in" (spec 004, US5) ----

    [Fact]
    public async Task Extend_gives_a_fresh_idle_window_but_not_more_absolute_time()
    {
        var client = await SignedInClientAsync();
        _factory.Time.Advance(TimeSpan.FromMinutes(20));
        var before = await SessionAsync(client); // idle ~40 min, no renewal yet

        var response = await client.PostJsonAsync("/api/auth/session/extend", new { });

        response.EnsureSuccessStatusCode();
        var extended = (await response.JsonAsync()).GetProperty("session");
        Assert.InRange(Idle(before), 2398, 2400);
        Assert.InRange(Idle(extended), 3598, 3600);
        Assert.Equal(Absolute(before), Absolute(extended));
        Assert.InRange(Idle(await SessionAsync(client)), 3598, 3600); // the cookie really was re-issued
    }

    [Fact]
    public async Task Extend_cannot_pass_the_8_hour_limit()
    {
        var client = await SignedInClientAsync();
        for (var i = 0; i < 11; i++)
        {
            _factory.Time.Advance(TimeSpan.FromMinutes(40));
            await SessionAsync(client);
        }

        _factory.Time.Advance(TimeSpan.FromMinutes(39)); // 7 h 59 min after sign-in
        var response = await client.PostJsonAsync("/api/auth/session/extend", new { });

        Assert.InRange(Absolute((await response.JsonAsync()).GetProperty("session")), 58, 60);
    }

    [Fact]
    public async Task Extend_requires_the_csrf_header()
    {
        var client = await SignedInClientAsync();

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, (await client.PostAsync("/api/auth/session/extend", null)).StatusCode);
    }

    // ---- Compute unit tests ----

    private static readonly DateTimeOffset Now = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);

    private static ClaimsPrincipal WithAuthTime(DateTimeOffset authTime) =>
        new(new ClaimsIdentity([new Claim(AuthClaims.AuthTime, authTime.ToUnixTimeSeconds().ToString())], "Cookies"));

    private static AuthenticationProperties Ticket(DateTimeOffset issued, DateTimeOffset expires) =>
        new() { IssuedUtc = issued, ExpiresUtc = expires };

    private static CookieAuthenticationOptions Cookie(bool sliding = true) =>
        new() { ExpireTimeSpan = TimeSpan.FromMinutes(60), SlidingExpiration = sliding };

    [Fact]
    public void Compute_returns_null_without_a_ticket_or_auth_time()
    {
        var user = WithAuthTime(Now);
        Assert.Null(SessionTiming.Compute(null, user, Now, Cookie(), TimeSpan.FromHours(8)));
        Assert.Null(SessionTiming.Compute(new AuthenticationProperties(), user, Now, Cookie(), TimeSpan.FromHours(8)));
        Assert.Null(SessionTiming.Compute(Ticket(Now, Now.AddHours(1)), new ClaimsPrincipal(new ClaimsIdentity()), Now, Cookie(), TimeSpan.FromHours(8)));
    }

    [Fact]
    public void Compute_never_renews_without_sliding_expiration()
    {
        var timing = SessionTiming.Compute(
            Ticket(Now.AddMinutes(-50), Now.AddMinutes(10)), WithAuthTime(Now.AddMinutes(-50)), Now, Cookie(sliding: false), TimeSpan.FromHours(8));

        Assert.Equal(600, timing!.IdleSecondsLeft);
    }

    [Fact]
    public void Compute_clamps_at_zero()
    {
        var timing = SessionTiming.Compute(
            Ticket(Now.AddHours(-9), Now.AddMinutes(-1)), WithAuthTime(Now.AddHours(-9)), Now, Cookie(sliding: false), TimeSpan.FromHours(8));

        Assert.Equal(0, timing!.IdleSecondsLeft);
        Assert.Equal(0, timing.AbsoluteSecondsLeft);
    }
}
