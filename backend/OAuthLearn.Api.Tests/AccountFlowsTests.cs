using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OAuthLearn.Api.Data;

namespace OAuthLearn.Api.Tests;

/// <summary>Register / verify / sign-in / reset through HTTP (spec 002, contracts/password-auth-api.md).</summary>
[Collection(PostgresCollection.Name)]
public sealed class AccountFlowsTests(PostgresFixture db) : IDisposable
{
    private const string GoodPassword = "correct horse battery";
    private readonly TestAppFactory _factory = new(db);

    public void Dispose() => _factory.Dispose();

    private HttpClient Client() => _factory.CreateHttpsClient();

    // ---- Registration (US1) ----

    [Fact]
    public async Task Register_new_email_creates_unverified_account_with_pending_password_and_sends_link()
    {
        var email = TestHelpers.UniqueEmail("new");

        var response = await Client().PostJsonAsync("/api/auth/register", new { email, password = GoodPassword, displayName = " Alice " });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("{}", await response.Content.ReadAsStringAsync());
        var user = await db.LoadUserAsync(email);
        Assert.Equal(UserCreatedVia.Password, user.CreatedVia);
        Assert.Null(user.EmailVerifiedAt);
        Assert.Null(user.GoogleSubject);
        Assert.Equal("Alice", user.DisplayName);
        Assert.Null(user.PasswordCredential!.ActiveHash);
        Assert.StartsWith("AQAAAAIAA", user.PasswordCredential.PendingHash); // PasswordHasher V3, HMAC-SHA512
        Assert.NotNull(await db.LatestTokenAsync(email, "/verify-email"));
    }

    [Fact]
    public async Task Register_existing_active_email_looks_identical_changes_nothing_and_sends_a_notice()
    {
        var email = TestHelpers.UniqueEmail("taken");
        await Client().RegisterAndVerifyAsync(db, email, GoodPassword);
        var before = await db.LoadUserAsync(email);

        var response = await Client().PostJsonAsync("/api/auth/register", new { email = email.ToUpperInvariant(), password = "another long password" });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("{}", await response.Content.ReadAsStringAsync());
        var after = await db.LoadUserAsync(email);
        Assert.Equal(before.PasswordCredential!.ActiveHash, after.PasswordCredential!.ActiveHash);
        Assert.Null(after.PasswordCredential.PendingHash);
        Assert.Contains("tried to create an account", (await db.MailboxAsync(email)).Last().Subject);
    }

    [Theory]
    [InlineData("not-an-email", GoodPassword, null, "email", "email_invalid")]
    [InlineData("VALID", "short", null, "password", "password_too_short")]
    [InlineData("VALID", "qwertyuiop", null, "password", "password_common")]
    [InlineData("VALID", "SAME_AS_EMAIL", null, "password", "password_is_email")]
    [InlineData("VALID", GoodPassword, "LONG_NAME", "displayName", "display_name_too_long")]
    public async Task Register_returns_field_error_codes(string email, string password, string? displayName, string field, string code)
    {
        var validEmail = TestHelpers.UniqueEmail("invalid");
        email = email == "VALID" ? validEmail : email;
        password = password == "SAME_AS_EMAIL" ? validEmail.ToUpperInvariant() : password;
        displayName = displayName == "LONG_NAME" ? new string('a', 101) : displayName;

        var response = await Client().PostJsonAsync("/api/auth/register", new { email, password, displayName });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(code, (await response.JsonAsync()).GetProperty("errors").GetProperty(field).GetString());
    }

    [Fact]
    public async Task Register_is_limited_to_5_per_ip_per_hour()
    {
        for (var i = 0; i < 5; i++)
        {
            var ok = await Client().PostJsonAsync("/api/auth/register", new { email = TestHelpers.UniqueEmail("burst"), password = GoodPassword });
            Assert.Equal(HttpStatusCode.Accepted, ok.StatusCode);
        }

        var sixth = await Client().PostJsonAsync("/api/auth/register", new { email = TestHelpers.UniqueEmail("burst"), password = GoodPassword });
        Assert.Equal(HttpStatusCode.TooManyRequests, sixth.StatusCode);
        Assert.Equal("too_many_attempts", (await sixth.JsonAsync()).GetProperty("code").GetString());

        _factory.Time.Advance(TimeSpan.FromMinutes(61));
        var later = await Client().PostJsonAsync("/api/auth/register", new { email = TestHelpers.UniqueEmail("burst"), password = GoodPassword });
        Assert.Equal(HttpStatusCode.Accepted, later.StatusCode);
    }

    [Fact]
    public async Task New_endpoints_require_the_csrf_header()
    {
        foreach (var url in new[] { "/api/auth/register", "/api/auth/verify-email", "/api/auth/password/sign-in", "/api/auth/forgot-password", "/api/auth/reset-password" })
        {
            var response = await Client().PostAsync(url, System.Net.Http.Json.JsonContent.Create(new { }));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    // ---- Verification (US1) ----

    [Fact]
    public async Task Verify_with_the_chosen_password_activates_and_signs_in()
    {
        var email = TestHelpers.UniqueEmail("verify");
        var client = Client();
        await client.PostJsonAsync("/api/auth/register", new { email, password = GoodPassword });
        var token = await db.LatestTokenAsync(email, "/verify-email");

        var response = await client.PostJsonAsync("/api/auth/verify-email", new { token, password = GoodPassword });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("oauthlearn.auth="));
        var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal(email, (await me.JsonAsync()).GetProperty("email").GetString());

        var user = await db.LoadUserAsync(email);
        Assert.NotNull(user.EmailVerifiedAt);
        Assert.NotNull(user.PasswordCredential!.ActiveHash);
        Assert.Null(user.PasswordCredential.PendingHash);
    }

    [Fact]
    public async Task Verify_with_a_different_password_fails_and_keeps_the_token_usable()
    {
        var email = TestHelpers.UniqueEmail("mismatch");
        await Client().PostJsonAsync("/api/auth/register", new { email, password = GoodPassword });
        var token = await db.LatestTokenAsync(email, "/verify-email");

        var wrong = await Client().PostJsonAsync("/api/auth/verify-email", new { token, password = "not the chosen one" });
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Equal("password_mismatch", (await wrong.JsonAsync()).GetProperty("errors").GetProperty("password").GetString());

        var right = await Client().PostJsonAsync("/api/auth/verify-email", new { token, password = GoodPassword });
        Assert.Equal(HttpStatusCode.NoContent, right.StatusCode);
    }

    [Fact]
    public async Task Verify_token_is_single_use_and_expires_after_24_hours()
    {
        var email = TestHelpers.UniqueEmail("once");
        await Client().PostJsonAsync("/api/auth/register", new { email, password = GoodPassword });
        var token = await db.LatestTokenAsync(email, "/verify-email");
        Assert.Equal(HttpStatusCode.NoContent, (await Client().PostJsonAsync("/api/auth/verify-email", new { token, password = GoodPassword })).StatusCode);

        var reused = await Client().PostJsonAsync("/api/auth/verify-email", new { token, password = GoodPassword });
        Assert.Equal("token_invalid", (await reused.JsonAsync()).GetProperty("errors").GetProperty("token").GetString());

        var other = TestHelpers.UniqueEmail("expires");
        await Client().PostJsonAsync("/api/auth/register", new { email = other, password = GoodPassword });
        var otherToken = await db.LatestTokenAsync(other, "/verify-email");
        _factory.Time.Advance(TimeSpan.FromHours(24) + TimeSpan.FromMinutes(1));
        var expired = await Client().PostJsonAsync("/api/auth/verify-email", new { token = otherToken, password = GoodPassword });
        Assert.Equal("token_invalid", (await expired.JsonAsync()).GetProperty("errors").GetProperty("token").GetString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("not base64 !!")]
    [InlineData("AAAA")]
    public async Task Verify_rejects_malformed_tokens(string token)
    {
        var response = await Client().PostJsonAsync("/api/auth/verify-email", new { token, password = GoodPassword });

        Assert.Equal("token_invalid", (await response.JsonAsync()).GetProperty("errors").GetProperty("token").GetString());
    }

    [Fact]
    public async Task Pre_registration_hijack_is_blocked()
    {
        // Research R4: an attacker registers the victim's email first; the victim registers too.
        var email = TestHelpers.UniqueEmail("victim");
        await Client().PostJsonAsync("/api/auth/register", new { email, password = "attacker-pass-1" });
        var attackerLink = await db.LatestTokenAsync(email, "/verify-email");
        await Client().PostJsonAsync("/api/auth/register", new { email, password = "victim-pass-12" });
        var victimLink = await db.LatestTokenAsync(email, "/verify-email");

        // The first link was replaced.
        var old = await Client().PostJsonAsync("/api/auth/verify-email", new { token = attackerLink, password = "attacker-pass-1" });
        Assert.Equal("token_invalid", (await old.JsonAsync()).GetProperty("errors").GetProperty("token").GetString());

        // The attacker's password can't activate the victim's link.
        var withAttackerPassword = await Client().PostJsonAsync("/api/auth/verify-email", new { token = victimLink, password = "attacker-pass-1" });
        Assert.Equal("password_mismatch", (await withAttackerPassword.JsonAsync()).GetProperty("errors").GetProperty("password").GetString());

        var victim = await Client().PostJsonAsync("/api/auth/verify-email", new { token = victimLink, password = "victim-pass-12" });
        Assert.Equal(HttpStatusCode.NoContent, victim.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client().PostJsonAsync("/api/auth/password/sign-in", new { email, password = "attacker-pass-1" })).StatusCode);
    }

    // ---- Sign-in (US2) ----

    [Fact]
    public async Task Sign_in_with_correct_password_issues_a_session()
    {
        var email = TestHelpers.UniqueEmail("signin");
        await Client().RegisterAndVerifyAsync(db, email, GoodPassword);
        var client = Client();

        var response = await client.PostJsonAsync("/api/auth/password/sign-in", new { email = $"  {email.ToUpperInvariant()}  ", password = GoodPassword });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(email, (await me.JsonAsync()).GetProperty("email").GetString());
    }

    [Fact]
    public async Task Wrong_password_unknown_email_and_google_only_account_look_identical()
    {
        var email = TestHelpers.UniqueEmail("known");
        await Client().RegisterAndVerifyAsync(db, email, GoodPassword);
        var googleEmail = TestHelpers.UniqueEmail("google");
        using (var scope = _factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<UserService>().UpsertFromGoogleAsync($"sub-{Guid.NewGuid():N}", googleEmail, null, default);
        }

        var responses = new[]
        {
            await Client().PostJsonAsync("/api/auth/password/sign-in", new { email, password = "wrong password!" }),
            await Client().PostJsonAsync("/api/auth/password/sign-in", new { email = TestHelpers.UniqueEmail("nobody"), password = GoodPassword }),
            await Client().PostJsonAsync("/api/auth/password/sign-in", new { email = googleEmail, password = GoodPassword }),
        };

        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal("""{"code":"invalid_credentials"}""", await response.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task Unverified_account_with_correct_password_gets_a_new_link()
    {
        var email = TestHelpers.UniqueEmail("unverified");
        await Client().PostJsonAsync("/api/auth/register", new { email, password = GoodPassword });
        var firstLink = await db.LatestTokenAsync(email, "/verify-email");

        var response = await Client().PostJsonAsync("/api/auth/password/sign-in", new { email, password = GoodPassword });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("email_not_verified", (await response.JsonAsync()).GetProperty("code").GetString());
        var secondLink = await db.LatestTokenAsync(email, "/verify-email");
        Assert.NotEqual(firstLink, secondLink);
        var old = await Client().PostJsonAsync("/api/auth/verify-email", new { token = firstLink, password = GoodPassword });
        Assert.Equal("token_invalid", (await old.JsonAsync()).GetProperty("errors").GetProperty("token").GetString());
    }

    [Fact]
    public async Task Logout_revokes_password_sessions_like_google_ones()
    {
        var email = TestHelpers.UniqueEmail("logout");
        var client = Client();
        await client.RegisterAndVerifyAsync(db, email, GoodPassword);
        var signIn = await client.PostJsonAsync("/api/auth/password/sign-in", new { email, password = GoodPassword });
        var cookie = signIn.Headers.GetValues("Set-Cookie").First(c => c.StartsWith("oauthlearn.auth=")).Split(';')[0];

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostJsonAsync("/api/auth/logout", new { })).StatusCode);

        var copied = _factory.CreateHttpsClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Add("Cookie", cookie);
        Assert.Equal(HttpStatusCode.Unauthorized, (await copied.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task Each_sign_in_issues_a_new_session_cookie()
    {
        var email = TestHelpers.UniqueEmail("fresh");
        await Client().RegisterAndVerifyAsync(db, email, GoodPassword);
        var client = Client();

        var first = await client.PostJsonAsync("/api/auth/password/sign-in", new { email, password = GoodPassword });
        _factory.Time.Advance(TimeSpan.FromSeconds(5));
        var second = await client.PostJsonAsync("/api/auth/password/sign-in", new { email, password = GoodPassword });

        string CookieOf(HttpResponseMessage r) => r.Headers.GetValues("Set-Cookie").First(c => c.StartsWith("oauthlearn.auth="));
        Assert.NotEqual(CookieOf(first), CookieOf(second));
    }

    // ---- Password reset (US5) ----

    [Fact]
    public async Task Forgot_password_answers_the_same_for_known_and_unknown_emails()
    {
        var known = TestHelpers.UniqueEmail("forgot");
        await Client().RegisterAndVerifyAsync(db, known, GoodPassword);
        var unknown = TestHelpers.UniqueEmail("ghost");

        var a = await Client().PostJsonAsync("/api/auth/forgot-password", new { email = known });
        var b = await Client().PostJsonAsync("/api/auth/forgot-password", new { email = unknown });

        Assert.Equal(HttpStatusCode.Accepted, a.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, b.StatusCode);
        Assert.Equal(await a.Content.ReadAsStringAsync(), await b.Content.ReadAsStringAsync());
        Assert.NotNull(await db.LatestTokenAsync(known, "/reset-password"));
        Assert.Empty(await db.MailboxAsync(unknown));
    }

    [Fact]
    public async Task Reset_changes_password_and_ends_existing_sessions()
    {
        var email = TestHelpers.UniqueEmail("reset");
        var signedIn = Client();
        await signedIn.RegisterAndVerifyAsync(db, email, GoodPassword);
        Assert.Equal(HttpStatusCode.OK, (await signedIn.GetAsync("/api/auth/me")).StatusCode);

        await Client().PostJsonAsync("/api/auth/forgot-password", new { email });
        var token = await db.LatestTokenAsync(email, "/reset-password");
        var reset = await Client().PostJsonAsync("/api/auth/reset-password", new { token, newPassword = "brand new passphrase" });

        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await signedIn.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client().PostJsonAsync("/api/auth/password/sign-in", new { email, password = GoodPassword })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Client().PostJsonAsync("/api/auth/password/sign-in", new { email, password = "brand new passphrase" })).StatusCode);

        var reused = await Client().PostJsonAsync("/api/auth/reset-password", new { token, newPassword = "yet another passphrase" });
        Assert.Equal("token_invalid", (await reused.JsonAsync()).GetProperty("errors").GetProperty("token").GetString());
    }

    [Fact]
    public async Task Reset_link_expires_after_30_minutes_and_a_newer_link_replaces_it()
    {
        var email = TestHelpers.UniqueEmail("expiry");
        await Client().RegisterAndVerifyAsync(db, email, GoodPassword);

        await Client().PostJsonAsync("/api/auth/forgot-password", new { email });
        var first = await db.LatestTokenAsync(email, "/reset-password");
        _factory.Time.Advance(TimeSpan.FromSeconds(1));
        await Client().PostJsonAsync("/api/auth/forgot-password", new { email });
        var second = await db.LatestTokenAsync(email, "/reset-password");

        var replaced = await Client().PostJsonAsync("/api/auth/reset-password", new { token = first, newPassword = "brand new passphrase" });
        Assert.Equal("token_invalid", (await replaced.JsonAsync()).GetProperty("errors").GetProperty("token").GetString());

        _factory.Time.Advance(TimeSpan.FromMinutes(31));
        var expired = await Client().PostJsonAsync("/api/auth/reset-password", new { token = second, newPassword = "brand new passphrase" });
        Assert.Equal("token_invalid", (await expired.JsonAsync()).GetProperty("errors").GetProperty("token").GetString());
    }

    [Fact]
    public async Task Reset_rejects_a_common_password_and_keeps_the_token_usable()
    {
        var email = TestHelpers.UniqueEmail("common");
        await Client().RegisterAndVerifyAsync(db, email, GoodPassword);
        await Client().PostJsonAsync("/api/auth/forgot-password", new { email });
        var token = await db.LatestTokenAsync(email, "/reset-password");

        var common = await Client().PostJsonAsync("/api/auth/reset-password", new { token, newPassword = "basketball" });
        Assert.Equal("password_common", (await common.JsonAsync()).GetProperty("errors").GetProperty("newPassword").GetString());

        var ok = await Client().PostJsonAsync("/api/auth/reset-password", new { token, newPassword = "brand new passphrase" });
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);
    }

    [Fact]
    public async Task Forgot_password_is_limited_to_3_per_email_per_hour_without_changing_the_answer()
    {
        var email = TestHelpers.UniqueEmail("limit");
        await Client().RegisterAndVerifyAsync(db, email, GoodPassword);

        for (var i = 0; i < 4; i++)
        {
            var response = await Client().PostJsonAsync("/api/auth/forgot-password", new { email });
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            _factory.Time.Advance(TimeSpan.FromSeconds(1));
        }

        Assert.Equal(3, (await db.MailboxAsync(email)).Count(m => m.BodyText.Contains("/reset-password#token=")));
    }

    [Fact]
    public async Task Google_only_account_can_set_a_first_password_via_reset()
    {
        var email = TestHelpers.UniqueEmail("googlereset");
        using (var scope = _factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<UserService>().UpsertFromGoogleAsync($"sub-{Guid.NewGuid():N}", email, null, default);
        }

        await Client().PostJsonAsync("/api/auth/forgot-password", new { email });
        var token = await db.LatestTokenAsync(email, "/reset-password");
        Assert.Equal(HttpStatusCode.NoContent, (await Client().PostJsonAsync("/api/auth/reset-password", new { token, newPassword = "first ever password" })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await Client().PostJsonAsync("/api/auth/password/sign-in", new { email, password = "first ever password" })).StatusCode);
    }

    // ---- Dev mailbox ----

    [Fact]
    public async Task Dev_mailbox_does_not_exist_outside_development()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Client().GetAsync("/dev/mailbox")).StatusCode);
    }

    [Fact]
    public async Task Dev_mailbox_in_development_encodes_content_and_has_its_own_csp()
    {
        await using (var context = db.CreateDbContext())
        {
            context.MailboxMessages.Add(new MailboxMessage
            {
                Id = Guid.NewGuid(),
                ToAddress = "x@example.com",
                Subject = "<script>alert(1)</script>",
                BodyText = "Click https://example.com/a?b=1 now",
                CreatedAt = DateTimeOffset.UtcNow.AddYears(1), // newest
            });
            await context.SaveChangesAsync();
        }

        using var devFactory = new TestAppFactory(db, "Development");
        var response = await devFactory.CreateHttpsClient().GetAsync("/dev/mailbox");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("style-src 'unsafe-inline'", response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("<a href=\"https://example.com/a?b=1\">", html);
    }

    // ---- No secrets in storage or responses ----

    [Fact]
    public async Task Passwords_are_never_stored_or_returned_in_plain_text()
    {
        var email = TestHelpers.UniqueEmail("plain");
        var client = Client();
        var register = await client.PostJsonAsync("/api/auth/register", new { email, password = GoodPassword });
        var token = await db.LatestTokenAsync(email, "/verify-email");
        var verify = await client.PostJsonAsync("/api/auth/verify-email", new { token, password = GoodPassword });
        var signIn = await client.PostJsonAsync("/api/auth/password/sign-in", new { email, password = GoodPassword });

        foreach (var response in new[] { register, verify, signIn })
        {
            Assert.DoesNotContain(GoodPassword, await response.Content.ReadAsStringAsync());
        }

        await using var context = db.CreateDbContext();
        var credential = await context.PasswordCredentials.AsNoTracking().SingleAsync(c => c.UserId == context.Users.Single(u => u.EmailNormalized == email).Id);
        Assert.DoesNotContain(GoodPassword, credential.ActiveHash);
        Assert.False(await context.MailboxMessages.AnyAsync(m => m.BodyText.Contains(GoodPassword)));
        Assert.False(await context.OneTimeTokens.AnyAsync(t => t.UserId == credential.UserId && t.TokenHash == System.Text.Encoding.UTF8.GetBytes(token!)));
    }
}
