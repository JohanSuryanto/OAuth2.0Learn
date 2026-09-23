using System.Net;
using Microsoft.Extensions.DependencyInjection;
using OAuthLearn.Api.Data;

namespace OAuthLearn.Api.Tests;

/// <summary>GET /api/account (spec 003, contracts/api-and-routes.md).</summary>
[Collection(PostgresCollection.Name)]
public sealed class AccountEndpointTests(PostgresFixture db) : IDisposable
{
    private const string Password = "correct horse battery";
    private readonly TestAppFactory _factory = new(db);

    public void Dispose() => _factory.Dispose();

    private HttpClient Client() => _factory.CreateHttpsClient();

    private async Task<User> GoogleUserAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<UserService>()
            .UpsertFromGoogleAsync($"sub-{Guid.NewGuid():N}", email, "Gina Google", default);
    }

    private HttpClient ClientAs(User user)
    {
        var client = Client();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, user.Email);
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, user.Id.ToString());
        return client;
    }

    private static async Task<string[]> MethodsAsync(HttpResponseMessage response) =>
        (await response.JsonAsync()).GetProperty("methods").EnumerateArray().Select(m => m.GetString()!).ToArray();

    [Fact]
    public async Task Without_a_session_returns_401_without_redirect()
    {
        var response = await Client().GetAsync("/api/account");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task Password_only_account_lists_password_and_its_details()
    {
        var email = TestHelpers.UniqueEmail("pwonly");
        var client = Client();
        await client.PostJsonAsync("/api/auth/register", new { email, password = Password, displayName = "Pat" });
        var token = await db.LatestTokenAsync(email, "/verify-email");
        await client.PostJsonAsync("/api/auth/verify-email", new { token, password = Password });

        var response = await client.GetAsync("/api/account");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.JsonAsync();
        Assert.Equal(email, json.GetProperty("email").GetString());
        Assert.Equal("Pat", json.GetProperty("displayName").GetString());
        Assert.Equal(["password"], await MethodsAsync(response));
        Assert.Equal(_factory.Time.GetUtcNow(), json.GetProperty("createdAt").GetDateTimeOffset());
        Assert.Equal(_factory.Time.GetUtcNow(), json.GetProperty("lastSignInAt").GetDateTimeOffset());
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Google_only_account_lists_google()
    {
        var user = await GoogleUserAsync(TestHelpers.UniqueEmail("gonly"));

        var response = await ClientAs(user).GetAsync("/api/account");

        Assert.Equal(["google"], await MethodsAsync(response));
        Assert.Equal("Gina Google", (await response.JsonAsync()).GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task Linked_account_lists_google_and_password()
    {
        var email = TestHelpers.UniqueEmail("both");
        var user = await GoogleUserAsync(email);
        await Client().PostJsonAsync("/api/auth/register", new { email, password = Password });
        var token = await db.LatestTokenAsync(email, "/verify-email");
        await Client().PostJsonAsync("/api/auth/verify-email", new { token, password = Password });

        var response = await ClientAs(user).GetAsync("/api/account");

        Assert.Equal(["google", "password"], await MethodsAsync(response));
    }

    [Fact]
    public async Task Pending_password_is_not_listed()
    {
        var email = TestHelpers.UniqueEmail("pending");
        var user = await GoogleUserAsync(email);
        await Client().PostJsonAsync("/api/auth/register", new { email, password = Password }); // never verified

        var response = await ClientAs(user).GetAsync("/api/account");

        Assert.Equal(["google"], await MethodsAsync(response));
    }

    [Fact]
    public async Task Display_name_can_be_null()
    {
        var email = TestHelpers.UniqueEmail("noname");
        await Client().RegisterAndVerifyAsync(db, email, Password);
        var client = Client();
        await client.PostJsonAsync("/api/auth/password/sign-in", new { email, password = Password });

        var json = await (await client.GetAsync("/api/account")).JsonAsync();

        Assert.Equal(System.Text.Json.JsonValueKind.Null, json.GetProperty("displayName").ValueKind);
    }
}
