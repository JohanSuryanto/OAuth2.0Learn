using System.Net;
using Microsoft.Extensions.DependencyInjection;
using OAuthLearn.Api.Auth;
using OAuthLearn.Api.Data;

namespace OAuthLearn.Api.Tests;

/// <summary>Profile, change/set password and re-authentication (spec 004, US2, US3).</summary>
[Collection(PostgresCollection.Name)]
public sealed class AccountSecurityTests(PostgresFixture db) : IDisposable
{
    private const string Password = "correct horse battery";
    private const string NewPassword = "brand new passphrase";
    private readonly TestAppFactory _factory = new(db);

    public void Dispose() => _factory.Dispose();

    private HttpClient Client() => _factory.CreateHttpsClient();

    private async Task<(string Email, HttpClient Client)> PasswordAccountAsync()
    {
        var email = TestHelpers.UniqueEmail("security");
        var client = Client();
        await client.RegisterAndVerifyAsync(db, email, Password);
        return (email, client);
    }

    private async Task<(User User, HttpClient Client)> GoogleAccountAsync()
    {
        User user;
        using (var scope = _factory.Services.CreateScope())
        {
            user = await scope.ServiceProvider.GetRequiredService<UserService>()
                .UpsertFromGoogleAsync($"sub-{Guid.NewGuid():N}", TestHelpers.UniqueEmail("google"), "Gina", default);
        }

        var client = Client();
        await client.SignInAsAsync(user.Id);
        return (user, client);
    }

    private static async Task<string?> NameAsync(HttpClient client)
    {
        var me = await (await client.GetAsync("/api/auth/me")).JsonAsync();
        var name = me.GetProperty("name");
        return name.ValueKind == System.Text.Json.JsonValueKind.Null ? null : name.GetString();
    }

    private static async Task<string?> ErrorAsync(HttpResponseMessage response, string field) =>
        (await response.JsonAsync()).GetProperty("errors").GetProperty(field).GetString();

    // ---- Profile (US2) ----

    [Fact]
    public async Task Updating_the_name_shows_at_once_without_signing_in_again()
    {
        var (_, client) = await PasswordAccountAsync();

        var response = await client.PostJsonAsync("/api/account/profile", new { displayName = "  New Name  " });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("New Name", (await response.JsonAsync()).GetProperty("displayName").GetString());
        Assert.Equal("New Name", await NameAsync(client));
    }

    [Fact]
    public async Task Whitespace_removes_the_name()
    {
        var (_, client) = await PasswordAccountAsync();
        await client.PostJsonAsync("/api/account/profile", new { displayName = "Temp" });

        await client.PostJsonAsync("/api/account/profile", new { displayName = "   " });

        Assert.Null(await NameAsync(client));
    }

    [Fact]
    public async Task Name_longer_than_100_is_refused()
    {
        var (_, client) = await PasswordAccountAsync();

        var response = await client.PostJsonAsync("/api/account/profile", new { displayName = new string('a', 101) });

        Assert.Equal("display_name_too_long", await ErrorAsync(response, "displayName"));
    }

    [Fact]
    public async Task Profile_requires_csrf_header_and_session()
    {
        var (_, client) = await PasswordAccountAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/account/profile", System.Net.Http.Json.JsonContent.Create(new { displayName = "x" }))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client().PostJsonAsync("/api/account/profile", new { displayName = "x" })).StatusCode);
    }

    [Fact]
    public async Task Google_sign_in_keeps_a_name_the_user_chose()
    {
        var (user, client) = await GoogleAccountAsync();
        await client.PostJsonAsync("/api/account/profile", new { displayName = "Chosen" });

        using var scope = _factory.Services.CreateScope();
        var again = await scope.ServiceProvider.GetRequiredService<UserService>()
            .UpsertFromGoogleAsync(user.GoogleSubject!, user.Email, "Name From Google", default);

        Assert.Equal("Chosen", again.DisplayName);
    }

    // ---- Change password (US3) ----

    [Fact]
    public async Task Changing_the_password_keeps_this_device_and_ends_the_others()
    {
        var (email, a) = await PasswordAccountAsync();
        var b = Client();
        await b.PostJsonAsync("/api/auth/password/sign-in", new { email, password = Password });

        var response = await a.PostJsonAsync("/api/account/password/change", new { currentPassword = Password, newPassword = NewPassword });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await a.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await b.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client().PostJsonAsync("/api/auth/password/sign-in", new { email, password = Password })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Client().PostJsonAsync("/api/auth/password/sign-in", new { email, password = NewPassword })).StatusCode);
    }

    [Fact]
    public async Task Wrong_current_password_is_refused_and_throttled_with_sign_in()
    {
        var (email, client) = await PasswordAccountAsync();

        var wrong = await client.PostJsonAsync("/api/account/password/change", new { currentPassword = "wrong password!", newPassword = NewPassword });
        Assert.Equal("current_password_incorrect", await ErrorAsync(wrong, "currentPassword"));

        for (var i = 0; i < 4; i++)
        {
            await client.PostJsonAsync("/api/account/password/change", new { currentPassword = "wrong password!", newPassword = NewPassword });
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostJsonAsync("/api/account/password/change", new { currentPassword = Password, newPassword = NewPassword })).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Client().PostJsonAsync("/api/auth/password/sign-in", new { email, password = Password })).StatusCode);
    }

    [Fact]
    public async Task New_password_must_follow_the_rules()
    {
        var (_, client) = await PasswordAccountAsync();

        var response = await client.PostJsonAsync("/api/account/password/change", new { currentPassword = Password, newPassword = "basketball" });

        Assert.Equal("password_common", await ErrorAsync(response, "newPassword"));
    }

    [Fact]
    public async Task Google_only_account_cannot_change_a_password_it_does_not_have()
    {
        var (_, client) = await GoogleAccountAsync();

        var response = await client.PostJsonAsync("/api/account/password/change", new { currentPassword = "x", newPassword = NewPassword });

        Assert.Equal("no_password", await ErrorAsync(response, "currentPassword"));
    }

    // ---- Set password + recent authentication (US3) ----

    [Fact]
    public async Task Google_only_account_can_set_a_password_within_10_minutes()
    {
        var (user, client) = await GoogleAccountAsync();

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostJsonAsync("/api/account/password/set", new { newPassword = NewPassword })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await Client().PostJsonAsync("/api/auth/password/sign-in", new { email = user.Email, password = NewPassword })).StatusCode);
    }

    [Fact]
    public async Task Setting_a_password_after_10_minutes_needs_google_reauth()
    {
        var (user, client) = await GoogleAccountAsync();
        _factory.Time.Advance(TimeSpan.FromMinutes(11));

        var response = await client.PostJsonAsync("/api/account/password/set", new { newPassword = NewPassword });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var json = await response.JsonAsync();
        Assert.Equal("reauth_required", json.GetProperty("code").GetString());
        Assert.Equal(["google"], json.GetProperty("methods").EnumerateArray().Select(m => m.GetString()!).ToArray());

        // A Google re-authentication with the linked account unlocks it.
        using (var scope = _factory.Services.CreateScope())
        {
            var sid = await CurrentSidAsync(user.Id);
            await scope.ServiceProvider.GetRequiredService<AccountSecurityService>().CompleteGoogleReauthAsync(user.Id, sid, user.GoogleSubject!, default);
        }

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostJsonAsync("/api/account/password/set", new { newPassword = NewPassword })).StatusCode);
    }

    [Fact]
    public async Task Google_reauth_with_another_google_account_is_refused()
    {
        var (user, _) = await GoogleAccountAsync();
        var sid = await CurrentSidAsync(user.Id);

        using var scope = _factory.Services.CreateScope();
        var ex = await Assert.ThrowsAsync<SignInRejectedException>(() =>
            scope.ServiceProvider.GetRequiredService<AccountSecurityService>().CompleteGoogleReauthAsync(user.Id, sid, "someone-else", default));
        Assert.Equal("reauth_mismatch", ex.ReasonCode);
    }

    [Fact]
    public async Task Account_with_a_password_uses_change_not_set()
    {
        var (_, client) = await PasswordAccountAsync();

        var response = await client.PostJsonAsync("/api/account/password/set", new { newPassword = NewPassword });

        Assert.Equal("password_already_set", await ErrorAsync(response, "newPassword"));
    }

    [Fact]
    public async Task Password_reauth_checks_the_current_password()
    {
        var (_, client) = await PasswordAccountAsync();
        _factory.Time.Advance(TimeSpan.FromMinutes(11));

        var wrong = await client.PostJsonAsync("/api/account/reauth/password", new { password = "nope nope nope" });
        Assert.Equal("current_password_incorrect", await ErrorAsync(wrong, "password"));

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostJsonAsync("/api/account/reauth/password", new { password = Password })).StatusCode);
    }

    // ---- Google re-auth plumbing ----

    [Fact]
    public async Task Google_reauth_challenge_forces_a_fresh_login()
    {
        var (_, client) = await GoogleAccountAsync();

        var response = await client.GetAsync("/api/auth/reauth/google");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.ToString();
        Assert.StartsWith("https://accounts.google.com/", location);
        Assert.Contains("max_age=0", location);
        Assert.Contains("prompt=select_account", location);
    }

    [Theory]
    [InlineData("/api/auth/popup-complete?result=reauth_ok", "reauth_ok")]
    [InlineData("/api/auth/popup-complete?error=reauth_mismatch", "reauth_mismatch")]
    [InlineData("/api/auth/popup-complete?result=whatever", "success")]
    public async Task Popup_complete_passes_reauth_results(string url, string result)
    {
        var response = await Client().GetAsync(url);

        Assert.Equal($"{TestAppFactory.FrontendOrigin}/auth-complete.html?result={result}", response.Headers.Location?.OriginalString);
    }

    private async Task<Guid> CurrentSidAsync(Guid userId)
    {
        await using var context = db.CreateDbContext();
        return context.UserSessions.Where(s => s.UserId == userId).OrderByDescending(s => s.CreatedAt).Select(s => s.Id).First();
    }
}
