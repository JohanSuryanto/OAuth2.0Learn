using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OAuthLearn.Api.Auth;
using OAuthLearn.Api.Data;

namespace OAuthLearn.Api.Tests;

/// <summary>Google + password on the same email (spec 002 US4, FR-014, research R8).</summary>
[Collection(PostgresCollection.Name)]
public sealed class AccountLinkingTests(PostgresFixture db) : IDisposable
{
    private const string Password = "correct horse battery";
    private readonly TestAppFactory _factory = new(db);

    public void Dispose() => _factory.Dispose();

    private HttpClient Client() => _factory.CreateHttpsClient();

    private async Task<User> GoogleSignInAsync(string sub, string email)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<UserService>().UpsertFromGoogleAsync(sub, email, "G User", default);
    }

    [Fact]
    public async Task Password_added_to_a_google_account_after_verification_reaches_the_same_account()
    {
        var email = TestHelpers.UniqueEmail("gfirst");
        var sub = $"sub-{Guid.NewGuid():N}";
        var google = await GoogleSignInAsync(sub, email);

        await Client().PostJsonAsync("/api/auth/register", new { email, password = Password });
        var pending = await db.LoadUserAsync(email);
        Assert.Equal(google.Id, pending.Id);
        Assert.NotNull(pending.PasswordCredential!.PendingHash);

        var token = await db.LatestTokenAsync(email, "/verify-email");
        Assert.Equal(HttpStatusCode.NoContent, (await Client().PostJsonAsync("/api/auth/verify-email", new { token, password = Password })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Client().PostJsonAsync("/api/auth/password/sign-in", new { email, password = Password })).StatusCode);

        var again = await GoogleSignInAsync(sub, email);
        Assert.Equal(google.Id, again.Id);
        await using var context = db.CreateDbContext();
        Assert.Equal(1, await context.Users.CountAsync(u => u.EmailNormalized == email));
    }

    [Fact]
    public async Task Google_sign_in_links_to_a_verified_password_account()
    {
        var email = TestHelpers.UniqueEmail("pfirst");
        await Client().RegisterAndVerifyAsync(db, email, Password);
        var before = await db.LoadUserAsync(email);

        var linked = await GoogleSignInAsync($"sub-{Guid.NewGuid():N}", email.ToUpperInvariant());

        var after = await db.LoadUserAsync(email);
        Assert.Equal(before.Id, linked.Id);
        Assert.NotNull(after.GoogleSubject);
        Assert.Equal(before.PasswordCredential!.ActiveHash, after.PasswordCredential!.ActiveHash);
        Assert.Equal(before.SessionVersion, after.SessionVersion);
        Assert.Equal(HttpStatusCode.NoContent, (await Client().PostJsonAsync("/api/auth/password/sign-in", new { email, password = Password })).StatusCode);
    }

    [Fact]
    public async Task Google_owner_discards_a_password_nobody_verified()
    {
        var email = TestHelpers.UniqueEmail("hijack");
        await Client().PostJsonAsync("/api/auth/register", new { email, password = Password }); // someone else, never verified
        var link = await db.LatestTokenAsync(email, "/verify-email");
        var before = await db.LoadUserAsync(email);

        await GoogleSignInAsync($"sub-{Guid.NewGuid():N}", email);

        var after = await db.LoadUserAsync(email);
        Assert.Null(after.PasswordCredential);
        Assert.NotNull(after.EmailVerifiedAt);
        Assert.Equal(before.SessionVersion + 1, after.SessionVersion);

        var signIn = await Client().PostJsonAsync("/api/auth/password/sign-in", new { email, password = Password });
        Assert.Equal("invalid_credentials", (await signIn.JsonAsync()).GetProperty("code").GetString());
        var verify = await Client().PostJsonAsync("/api/auth/verify-email", new { token = link, password = Password });
        Assert.Equal("token_invalid", (await verify.JsonAsync()).GetProperty("errors").GetProperty("token").GetString());
    }

    [Fact]
    public async Task Google_email_changing_to_another_accounts_email_is_refused()
    {
        var taken = TestHelpers.UniqueEmail("taken");
        await Client().RegisterAndVerifyAsync(db, taken, Password);
        var sub = $"sub-{Guid.NewGuid():N}";
        var original = TestHelpers.UniqueEmail("orig");
        await GoogleSignInAsync(sub, original);

        var ex = await Assert.ThrowsAsync<SignInRejectedException>(() => GoogleSignInAsync(sub, taken));

        Assert.Equal("email_conflict", ex.ReasonCode);
        Assert.Equal(original, (await db.LoadUserAsync(original)).Email);
        Assert.Null((await db.LoadUserAsync(taken)).GoogleSubject);
    }
}
