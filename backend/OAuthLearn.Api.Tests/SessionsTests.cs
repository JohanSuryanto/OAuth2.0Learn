using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OAuthLearn.Api.Data;

namespace OAuthLearn.Api.Tests;

/// <summary>Per-device sessions (spec 004, US4, FR-013–FR-016, research R1–R2).</summary>
[Collection(PostgresCollection.Name)]
public sealed class SessionsTests(PostgresFixture db) : IDisposable
{
    private const string Password = "correct horse battery";
    private readonly TestAppFactory _factory = new(db);

    public void Dispose() => _factory.Dispose();

    private HttpClient Client(string? userAgent = null)
    {
        var client = _factory.CreateHttpsClient();
        return userAgent is null ? client : client.WithUserAgent(userAgent);
    }

    /// <summary>A verified password account; returns the email and a signed-in client (A).</summary>
    private async Task<(string Email, HttpClient A)> AccountAsync(string? userAgent = TestHelpers.ChromeOnWindows)
    {
        var email = TestHelpers.UniqueEmail("sessions");
        var a = Client(userAgent);
        await a.RegisterAndVerifyAsync(db, email, Password);
        return (email, a);
    }

    private async Task<HttpClient> SignInAsync(string email, string? userAgent = TestHelpers.FirefoxOnLinux)
    {
        var client = Client(userAgent);
        (await client.PostJsonAsync("/api/auth/password/sign-in", new { email, password = Password })).EnsureSuccessStatusCode();
        return client;
    }

    private static async Task<List<JsonElement>> SessionsAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/account/sessions");
        response.EnsureSuccessStatusCode();
        return (await response.JsonAsync()).EnumerateArray().ToList();
    }

    private static async Task<HttpStatusCode> MeAsync(HttpClient client) => (await client.GetAsync("/api/auth/me")).StatusCode;

    // ---- Foundation ----

    [Fact]
    public async Task Each_sign_in_creates_a_session_row_with_a_device_description()
    {
        var (email, _) = await AccountAsync();
        await SignInAsync(email);

        var user = await db.LoadUserAsync(email);
        await using var context = db.CreateDbContext();
        var rows = await context.UserSessions.AsNoTracking().Where(s => s.UserId == user.Id).OrderBy(s => s.CreatedAt).ThenBy(s => s.Id).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.DeviceLabel == "Chrome on Windows");
        Assert.Contains(rows, r => r.DeviceLabel == "Firefox on Linux");
        Assert.All(rows, r => Assert.EndsWith(".x", r.IpMasked));
    }

    [Fact]
    public async Task Logout_signs_out_only_this_device()
    {
        var (email, a) = await AccountAsync();
        var b = await SignInAsync(email);

        Assert.Equal(HttpStatusCode.NoContent, (await a.PostJsonAsync("/api/auth/logout", new { })).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(a));
        Assert.Equal(HttpStatusCode.OK, await MeAsync(b));
    }

    [Fact]
    public async Task A_revoked_row_ends_that_device_on_its_next_request()
    {
        var (email, a) = await AccountAsync();
        var user = await db.LoadUserAsync(email);
        await using (var context = db.CreateDbContext())
        {
            await context.UserSessions.Where(s => s.UserId == user.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow));
        }

        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(a));
    }

    [Fact]
    public async Task Password_reset_ends_every_session()
    {
        var (email, a) = await AccountAsync();
        var b = await SignInAsync(email);

        await Client().PostJsonAsync("/api/auth/forgot-password", new { email });
        var token = await db.LatestTokenAsync(email, "/reset-password");
        Assert.Equal(HttpStatusCode.NoContent, (await Client().PostJsonAsync("/api/auth/reset-password", new { token, newPassword = "brand new passphrase" })).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(a));
        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(b));
    }

    [Fact]
    public async Task Legacy_cookie_keeps_working_and_is_listed_as_unknown_device()
    {
        var (email, a) = await AccountAsync();
        var user = await db.LoadUserAsync(email);
        var legacy = Client(TestHelpers.FirefoxOnLinux);
        await legacy.SignInAsAsync(user.Id, legacy: true);

        Assert.Equal(HttpStatusCode.OK, await MeAsync(legacy)); // upgraded on this request
        Assert.Equal(HttpStatusCode.OK, await MeAsync(legacy)); // and keeps working with the new sid

        var sessions = await SessionsAsync(a);
        Assert.Equal(2, sessions.Count);
        Assert.Contains(sessions, s => s.GetProperty("device").ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async Task Last_seen_moves_only_after_five_minutes()
    {
        var (email, a) = await AccountAsync();
        var user = await db.LoadUserAsync(email);

        async Task<DateTimeOffset> LastSeenAsync()
        {
            await using var context = db.CreateDbContext();
            return await context.UserSessions.AsNoTracking().Where(s => s.UserId == user.Id).Select(s => s.LastSeenAt).SingleAsync();
        }

        var first = await LastSeenAsync();
        _factory.Time.Advance(TimeSpan.FromMinutes(4));
        await MeAsync(a);
        Assert.Equal(first, await LastSeenAsync());

        _factory.Time.Advance(TimeSpan.FromMinutes(2));
        await MeAsync(a);
        Assert.Equal(_factory.Time.GetUtcNow(), await LastSeenAsync());
    }

    // ---- Sessions list and sign-out (US4) ----

    [Fact]
    public async Task Lists_this_device_first_with_details()
    {
        var (email, a) = await AccountAsync();
        _factory.Time.Advance(TimeSpan.FromMinutes(1));
        await SignInAsync(email);

        var sessions = await SessionsAsync(a);

        Assert.Equal(2, sessions.Count);
        Assert.True(sessions[0].GetProperty("current").GetBoolean());
        Assert.Equal("Chrome on Windows", sessions[0].GetProperty("device").GetString());
        Assert.False(sessions[1].GetProperty("current").GetBoolean());
        Assert.Equal("Firefox on Linux", sessions[1].GetProperty("device").GetString());
        Assert.EndsWith(".x", sessions[1].GetProperty("ipMasked").GetString());
    }

    [Fact]
    public async Task Signing_out_another_device_ends_it()
    {
        var (email, a) = await AccountAsync();
        var b = await SignInAsync(email);
        var bId = (await SessionsAsync(a)).Single(s => !s.GetProperty("current").GetBoolean()).GetProperty("id").GetString();

        Assert.Equal(HttpStatusCode.NoContent, (await a.PostJsonAsync($"/api/account/sessions/{bId}/revoke", new { })).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(b));
        Assert.Single(await SessionsAsync(a));
    }

    [Fact]
    public async Task Cannot_revoke_the_current_session_or_someone_elses()
    {
        var (_, a) = await AccountAsync();
        var aId = (await SessionsAsync(a)).Single().GetProperty("id").GetString();
        var (_, other) = await AccountAsync();
        var otherId = (await SessionsAsync(other)).Single().GetProperty("id").GetString();

        var self = await a.PostJsonAsync($"/api/account/sessions/{aId}/revoke", new { });
        Assert.Equal("use_logout", (await self.JsonAsync()).GetProperty("errors").GetProperty("id").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await a.PostJsonAsync($"/api/account/sessions/{Guid.NewGuid()}/revoke", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await a.PostJsonAsync($"/api/account/sessions/{otherId}/revoke", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, await MeAsync(other));
    }

    [Fact]
    public async Task Sign_out_all_other_devices_keeps_this_one()
    {
        var (email, a) = await AccountAsync();
        var b = await SignInAsync(email);
        var c = await SignInAsync(email);
        var user = await db.LoadUserAsync(email);
        var legacy = Client();
        await legacy.SignInAsAsync(user.Id, legacy: true);

        Assert.Equal(HttpStatusCode.NoContent, (await a.PostJsonAsync("/api/account/sessions/revoke-others", new { })).StatusCode);

        Assert.Equal(HttpStatusCode.OK, await MeAsync(a));
        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(b));
        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(c));
        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(legacy));
        Assert.Single(await SessionsAsync(a));
    }

    [Fact]
    public async Task Idle_sessions_are_not_listed()
    {
        var (email, a) = await AccountAsync();
        await SignInAsync(email);
        _factory.Time.Advance(TimeSpan.FromMinutes(40));
        await MeAsync(a); // A stays active
        _factory.Time.Advance(TimeSpan.FromMinutes(25));

        Assert.Single(await SessionsAsync(a));
    }

    [Fact]
    public async Task Sessions_endpoints_require_the_csrf_header_and_a_session()
    {
        var (_, a) = await AccountAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await a.PostAsync("/api/account/sessions/revoke-others", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client().GetAsync("/api/account/sessions")).StatusCode);
    }
}
