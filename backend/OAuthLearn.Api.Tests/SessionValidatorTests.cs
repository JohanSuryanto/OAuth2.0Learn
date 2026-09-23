using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using OAuthLearn.Api.Auth;
using OAuthLearn.Api.Data;

namespace OAuthLearn.Api.Tests;

[Collection(PostgresCollection.Name)]
public class SessionValidatorTests(PostgresFixture db)
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 23, 8, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Keeps_a_session_with_the_current_version()
    {
        var user = await CreateUserAsync();

        var context = await ValidateAsync(Principal(user.Id, user.SessionVersion, _time.GetUtcNow()));

        Assert.NotNull(context.Principal);
    }

    [Fact]
    public async Task Rejects_a_session_after_logout_revoked_it()
    {
        var user = await CreateUserAsync();
        var principal = Principal(user.Id, user.SessionVersion, _time.GetUtcNow());
        await using (var dbContext = db.CreateDbContext())
        {
            await new UserService(dbContext, _time).RevokeSessionsAsync(user.Id, default);
        }

        var context = await ValidateAsync(principal);

        Assert.Null(context.Principal);
        Assert.Contains(
            context.HttpContext.Response.Headers.SetCookie,
            c => c!.StartsWith("oauthlearn.auth=") || c.StartsWith(".AspNetCore.Cookies="));
    }

    [Fact]
    public async Task Rejects_a_session_for_a_missing_user()
    {
        var context = await ValidateAsync(Principal(Guid.NewGuid(), 0, _time.GetUtcNow()));

        Assert.Null(context.Principal);
    }

    [Fact]
    public async Task Rejects_a_session_with_missing_claims()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Email, "x@example.com")], "Cookies"));

        var context = await ValidateAsync(principal);

        Assert.Null(context.Principal);
    }

    [Fact]
    public async Task Rejects_when_the_database_is_unreachable()
    {
        var user = await CreateUserAsync();

        var context = await ValidateAsync(
            Principal(user.Id, user.SessionVersion, _time.GetUtcNow()),
            connectionString: "Host=127.0.0.1;Port=1;Database=nowhere;Username=x;Password=x;Timeout=2");

        Assert.Null(context.Principal);
    }

    [Fact]
    public async Task Keeps_a_session_just_inside_the_absolute_lifetime()
    {
        var user = await CreateUserAsync();

        var context = await ValidateAsync(
            Principal(user.Id, user.SessionVersion, _time.GetUtcNow() - TimeSpan.FromHours(8) + TimeSpan.FromMinutes(1)));

        Assert.NotNull(context.Principal);
    }

    [Fact]
    public async Task Rejects_a_session_older_than_the_absolute_lifetime()
    {
        var user = await CreateUserAsync();

        var context = await ValidateAsync(
            Principal(user.Id, user.SessionVersion, _time.GetUtcNow() - TimeSpan.FromHours(8) - TimeSpan.FromMinutes(1)));

        Assert.Null(context.Principal);
    }

    [Fact]
    public async Task Uses_the_configured_absolute_lifetime()
    {
        var user = await CreateUserAsync();

        var context = await ValidateAsync(
            Principal(user.Id, user.SessionVersion, _time.GetUtcNow() - TimeSpan.FromMinutes(3)),
            absoluteLifetime: "00:02:00");

        Assert.Null(context.Principal);
    }

    private async Task<User> CreateUserAsync()
    {
        await using var dbContext = db.CreateDbContext();
        return await new UserService(dbContext, _time)
            .UpsertFromGoogleAsync($"sub-{Guid.NewGuid():N}", TestHelpers.UniqueEmail("session"), null, default);
    }

    private static ClaimsPrincipal Principal(Guid userId, int sessionVersion, DateTimeOffset authTime) =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Email, "s@example.com"),
            new Claim(AuthClaims.AppUserId, userId.ToString()),
            new Claim(AuthClaims.SessionVersion, sessionVersion.ToString()),
            new Claim(AuthClaims.AuthTime, authTime.ToUnixTimeSeconds().ToString()),
        ], CookieAuthenticationDefaults.AuthenticationScheme));

    private async Task<CookieValidatePrincipalContext> ValidateAsync(
        ClaimsPrincipal principal, string? connectionString = null, string absoluteLifetime = "08:00:00")
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:AbsoluteSessionLifetime"] = absoluteLifetime })
            .Build());
        services.AddSingleton<TimeProvider>(_time);
        services.AddDbContext<AppDbContext>(o => o
            .UseNpgsql(connectionString ?? db.ConnectionString)
            .UseSnakeCaseNamingConvention());
        services.AddScoped<UserService>();
        services.AddScoped<SessionStore>();
        services.AddAuthentication().AddCookie(o => o.Cookie.Name = "oauthlearn.auth");

        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();
        var httpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString("localhost");

        var scheme = new AuthenticationScheme(
            CookieAuthenticationDefaults.AuthenticationScheme, null, typeof(CookieAuthenticationHandler));
        var context = new CookieValidatePrincipalContext(
            httpContext, scheme, new CookieAuthenticationOptions(),
            new AuthenticationTicket(principal, CookieAuthenticationDefaults.AuthenticationScheme));

        await SessionValidator.ValidateAsync(context);
        return context;
    }
}
