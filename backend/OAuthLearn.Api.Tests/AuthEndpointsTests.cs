using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OAuthLearn.Api.Auth;
using OAuthLearn.Api.Data;

namespace OAuthLearn.Api.Tests;

[Collection(PostgresCollection.Name)]
public sealed class AuthEndpointsTests(PostgresFixture db) : IDisposable
{
    private readonly TestAppFactory _factory = new(db);

    public void Dispose() => _factory.Dispose();

    // ---- /api/auth/me ----

    [Fact]
    public async Task Me_without_session_returns_401_without_redirect()
    {
        var response = await _factory.CreateHttpsClient().GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task Me_with_user_returns_email_and_name()
    {
        var client = _factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "a@b.com|Alice");

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<MeResponse>();
        Assert.Equal(new MeResponse("a@b.com", "Alice", null), body); // no cookie ticket with the Test scheme
    }

    // ---- /api/auth/popup-complete ----

    [Theory]
    [InlineData("/api/auth/popup-complete", "success")]
    [InlineData("/api/auth/popup-complete?error=access_denied", "access_denied")]
    [InlineData("/api/auth/popup-complete?error=signin_failed", "signin_failed")]
    [InlineData("/api/auth/popup-complete?error=%3Cscript%3E", "signin_failed")]
    public async Task Popup_complete_redirects_to_the_frontend_with_a_whitelisted_result(string url, string result)
    {
        var response = await _factory.CreateHttpsClient().GetAsync(url);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(
            $"{TestAppFactory.FrontendOrigin}/auth-complete.html?result={result}",
            response.Headers.Location?.OriginalString);
    }

    // ---- /api/auth/login ----

    [Fact]
    public async Task Login_challenges_google_with_the_signin_google_callback()
    {
        var response = await _factory.CreateHttpsClient().GetAsync("/api/auth/login");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!;
        Assert.Equal("accounts.google.com", location.Host);
        var query = QueryHelpers(location);
        Assert.EndsWith("/signin-google", query["redirect_uri"]);
        Assert.Equal("test-client-id", query["client_id"]);
        Assert.Contains("openid", query["scope"]);
        Assert.False(string.IsNullOrEmpty(query["state"]));
        Assert.Equal("S256", query["code_challenge_method"]);
    }

    // ---- security headers ----

    [Fact]
    public async Task Responses_carry_security_headers()
    {
        var response = await _factory.CreateHttpsClient().GetAsync("/api/auth/me");

        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }

    // ---- /api/auth/logout ----

    [Fact]
    public async Task Logout_without_csrf_header_returns_400()
    {
        var response = await _factory.CreateHttpsClient().PostAsync("/api/auth/logout", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Logout_revokes_sessions_keeps_the_user_row_and_expires_the_cookie()
    {
        var sub = $"sub-{Guid.NewGuid():N}";
        var email = TestHelpers.UniqueEmail("out");
        Guid userId;
        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserService>();
            userId = (await users.UpsertFromGoogleAsync(sub, email, null, default)).Id;
        }

        var client = _factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, email);
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, userId.ToString());
        client.DefaultRequestHeaders.Add("X-Requested-With", "fetch");

        var response = await client.PostAsync("/api/auth/logout", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var setCookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("oauthlearn.auth="));
        Assert.Contains("expires=Thu, 01 Jan 1970", setCookie, StringComparison.OrdinalIgnoreCase);

        await using var check = db.CreateDbContext();
        var stored = await check.Users.SingleAsync(u => u.GoogleSubject == sub);
        Assert.Equal(1, stored.SessionVersion);
    }

    [Fact]
    public async Task Logout_when_already_signed_out_returns_204()
    {
        var client = _factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Add("X-Requested-With", "fetch");

        var response = await client.PostAsync("/api/auth/logout", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    // ---- cookie configuration ----

    [Fact]
    public void Cookie_is_http_only_secure_lax_with_60_minute_sliding_expiry()
    {
        var options = _factory.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);

        Assert.Equal(TimeSpan.FromMinutes(60), options.ExpireTimeSpan);
        Assert.True(options.SlidingExpiration);
        Assert.True(options.Cookie.HttpOnly);
        Assert.Equal(CookieSecurePolicy.Always, options.Cookie.SecurePolicy);
        Assert.Equal(SameSiteMode.Lax, options.Cookie.SameSite);
        Assert.Equal("oauthlearn.auth", options.Cookie.Name);
    }

    private static Dictionary<string, string> QueryHelpers(Uri uri) =>
        Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query)
            .ToDictionary(kv => kv.Key, kv => kv.Value.ToString());
}
