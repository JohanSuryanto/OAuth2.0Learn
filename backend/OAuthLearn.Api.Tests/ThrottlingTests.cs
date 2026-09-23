using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OAuthLearn.Api.Throttling;

namespace OAuthLearn.Api.Tests;

/// <summary>Guessing protection (spec 002 US3, FR-011, research R7). Each test gets its own app + clock.</summary>
[Collection(PostgresCollection.Name)]
public sealed class ThrottlingTests(PostgresFixture db) : IDisposable
{
    private const string Password = "correct horse battery";
    private readonly TestAppFactory _factory = new(db);

    public void Dispose() => _factory.Dispose();

    private Task<HttpResponseMessage> SignInAsync(string email, string password) =>
        _factory.CreateHttpsClient().PostJsonAsync("/api/auth/password/sign-in", new { email, password });

    [Fact]
    public async Task Five_failures_lock_the_email_even_for_the_correct_password_until_15_minutes_pass()
    {
        var email = TestHelpers.UniqueEmail("locked");
        await _factory.CreateHttpsClient().RegisterAndVerifyAsync(db, email, Password);

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await SignInAsync(email, "wrong password " + i)).StatusCode);
            _factory.Time.Advance(TimeSpan.FromSeconds(10));
        }

        var locked = await SignInAsync(email, Password);
        Assert.Equal(HttpStatusCode.TooManyRequests, locked.StatusCode);
        Assert.Equal("""{"code":"too_many_attempts"}""", await locked.Content.ReadAsStringAsync());

        _factory.Time.Advance(TimeSpan.FromMinutes(15));
        Assert.Equal(HttpStatusCode.NoContent, (await SignInAsync(email, Password)).StatusCode);
    }

    [Fact]
    public async Task Unregistered_email_is_throttled_identically()
    {
        var registered = TestHelpers.UniqueEmail("real");
        await _factory.CreateHttpsClient().RegisterAndVerifyAsync(db, registered, Password);
        var unregistered = TestHelpers.UniqueEmail("fake");

        for (var i = 0; i < 6; i++)
        {
            var a = await SignInAsync(registered, "wrong password " + i);
            var b = await SignInAsync(unregistered, "wrong password " + i);
            Assert.Equal(a.StatusCode, b.StatusCode);
            Assert.Equal(await a.Content.ReadAsStringAsync(), await b.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task Twenty_failures_from_one_address_lock_every_email()
    {
        for (var i = 0; i < 20; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await SignInAsync(TestHelpers.UniqueEmail("spray"), "wrong password")).StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await SignInAsync(TestHelpers.UniqueEmail("next"), "wrong password")).StatusCode);

        _factory.Time.Advance(TimeSpan.FromMinutes(16));
        Assert.Equal(HttpStatusCode.Unauthorized, (await SignInAsync(TestHelpers.UniqueEmail("after"), "wrong password")).StatusCode);
    }

    [Fact]
    public async Task Successful_sign_in_clears_the_email_counter()
    {
        var email = TestHelpers.UniqueEmail("clears");
        await _factory.CreateHttpsClient().RegisterAndVerifyAsync(db, email, Password);

        for (var i = 0; i < 4; i++) await SignInAsync(email, "wrong password");
        Assert.Equal(HttpStatusCode.NoContent, (await SignInAsync(email, Password)).StatusCode);
        for (var i = 0; i < 4; i++) await SignInAsync(email, "wrong password");

        Assert.Equal(HttpStatusCode.NoContent, (await SignInAsync(email, Password)).StatusCode);
    }

    [Fact]
    public async Task Throttle_keys_never_contain_the_plain_email()
    {
        var email = TestHelpers.UniqueEmail("hashed");
        await SignInAsync(email, "wrong password");

        await using var context = db.CreateDbContext();
        var plain = Encoding.UTF8.GetBytes(email);
        var keys = await context.RateLimitEvents.AsNoTracking().Select(e => e.KeyHash).ToListAsync();
        Assert.NotEmpty(keys);
        Assert.DoesNotContain(keys, k => k.SequenceEqual(plain) || k.Length != 32);
    }

    [Fact]
    public async Task Lockout_needs_the_failures_close_together()
    {
        using var scope = _factory.Services.CreateScope();
        var limiter = scope.ServiceProvider.GetRequiredService<AttemptLimiter>();
        const string bucket = AttemptLimiter.Buckets.SignInFailEmail;
        var spread = "spread-" + Guid.NewGuid();
        var burst = "burst-" + Guid.NewGuid();

        for (var i = 0; i < 5; i++)
        {
            await limiter.RecordAsync(spread, spread, default);
            _factory.Time.Advance(TimeSpan.FromMinutes(5)); // 5 failures over 20 minutes
        }

        Assert.False(await limiter.IsLockedOutAsync(spread, spread, 5, TimeSpan.FromMinutes(15), default));

        for (var i = 0; i < 5; i++)
        {
            await limiter.RecordAsync(bucket, burst, default);
            _factory.Time.Advance(TimeSpan.FromMinutes(1));
        }

        Assert.True(await limiter.IsLockedOutAsync(bucket, burst, 5, TimeSpan.FromMinutes(15), default));
        _factory.Time.Advance(TimeSpan.FromMinutes(14));
        Assert.False(await limiter.IsLockedOutAsync(bucket, burst, 5, TimeSpan.FromMinutes(15), default));
    }
}
