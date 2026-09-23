using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OAuthLearn.Api.Data;

namespace OAuthLearn.Api.Tests;

public static class TestHelpers
{
    /// <summary>POST JSON the way the frontend does: with the X-Requested-With CSRF header.</summary>
    public static Task<HttpResponseMessage> PostJsonAsync(this HttpClient client, string url, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-Requested-With", "fetch");
        return client.SendAsync(request);
    }

    public static async Task<JsonElement> JsonAsync(this HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    public static string UniqueEmail(string prefix = "user") => $"{prefix}-{Guid.NewGuid():N}@example.com";

    public static async Task<List<MailboxMessage>> MailboxAsync(this PostgresFixture db, string to)
    {
        await using var context = db.CreateDbContext();
        return await context.MailboxMessages.AsNoTracking()
            .Where(m => m.ToAddress == to)
            .OrderBy(m => m.CreatedAt).ThenBy(m => m.Id)
            .ToListAsync();
    }

    /// <summary>Token from the newest mailbox message to <paramref name="to"/> containing "{path}#token=".</summary>
    public static async Task<string?> LatestTokenAsync(this PostgresFixture db, string to, string path)
    {
        var marker = path + "#token=";
        var message = (await db.MailboxAsync(to)).LastOrDefault(m => m.BodyText.Contains(marker));
        if (message is null)
        {
            return null;
        }

        var start = message.BodyText.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        var end = start;
        while (end < message.BodyText.Length && !char.IsWhiteSpace(message.BodyText[end]))
        {
            end++;
        }

        return message.BodyText[start..end];
    }

    public static async Task<User> LoadUserAsync(this PostgresFixture db, string email)
    {
        await using var context = db.CreateDbContext();
        var normalized = UserService.NormalizeEmail(email);
        return await context.Users.AsNoTracking()
            .Include(u => u.PasswordCredential)
            .SingleAsync(u => u.EmailNormalized == normalized);
    }

    /// <summary>Signs the client in as any user through the test-only endpoint (see TestSignInStartupFilter).</summary>
    public static async Task SignInAsAsync(this HttpClient client, Guid userId, bool legacy = false)
    {
        var response = await client.PostAsync($"/test/sign-in-as/{userId}{(legacy ? "?legacy=true" : "")}", null);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>A client whose requests carry the given User-Agent (for device descriptions).</summary>
    public static HttpClient WithUserAgent(this HttpClient client, string userAgent)
    {
        client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        return client;
    }

    public const string ChromeOnWindows = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36";
    public const string FirefoxOnLinux = "Mozilla/5.0 (X11; Linux x86_64; rv:142.0) Gecko/20100101 Firefox/142.0";

    /// <summary>Registers and verifies an account through the API; the client ends up signed in.</summary>
    public static async Task RegisterAndVerifyAsync(this HttpClient client, PostgresFixture db, string email, string password)
    {
        var register = await client.PostJsonAsync("/api/auth/register", new { email, password });
        register.EnsureSuccessStatusCode();
        var token = await db.LatestTokenAsync(email, "/verify-email");
        var verify = await client.PostJsonAsync("/api/auth/verify-email", new { token, password });
        verify.EnsureSuccessStatusCode();
    }
}
