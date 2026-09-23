using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Extensions.Options;
using OAuthLearn.Api.Auth;

namespace OAuthLearn.Api.Tests;

/// <summary>
/// Runs the real app against the fixture database. A "Test" scheme signs requests in when the
/// X-Test-User header is present; it is wired as the default *authenticate* scheme only, so the
/// challenge stays Cookie and unauthenticated requests still get the real 401 behavior.
/// </summary>
public class TestAppFactory(PostgresFixture db, string environment = "Testing") : WebApplicationFactory<Program>
{
    public const string FrontendOrigin = "http://localhost:5174";

    /// <summary>The app's clock; advance it to test expiry and throttling windows.</summary>
    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 9, 23, 8, 0, 0, TimeSpan.Zero));

    /// <summary>
    /// Client address seen by this app instance. Random per factory (xUnit makes one per test), so per-address
    /// throttling in one test never leaks into another through the shared database.
    /// </summary>
    public IPAddress ClientIp { get; } = new([10, (byte)Random.Shared.Next(256), (byte)Random.Shared.Next(256), (byte)Random.Shared.Next(1, 255)]);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // "Testing" (default): skips user secrets and startup migrations (the fixture already migrated).
        builder.UseEnvironment(environment);
        builder.UseSetting("ConnectionStrings:Default", db.ConnectionString);
        builder.UseSetting("Authentication:Google:ClientId", "test-client-id");
        builder.UseSetting("Authentication:Google:ClientSecret", "test-client-secret");
        builder.UseSetting("Frontend:Origin", FrontendOrigin);
        builder.UseSetting("Auth:LookupHashKey", "dGVzdC1sb29rdXAtaGFzaC1rZXktMzItYnl0ZXMtbG9uZyE=");
        // Real PBKDF2, fewer iterations: keeps the suite fast. Production default is 210,000.
        builder.UseSetting("Auth:PasswordHashIterations", "1000");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Time);
            services.AddSingleton<IStartupFilter>(new ClientIpStartupFilter(ClientIp));
            services.AddSingleton<IStartupFilter>(new TestSignInStartupFilter());

            services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.Scheme, _ => { })
                .AddPolicyScheme("TestOrCookie", null, options => options.ForwardDefaultSelector = ctx =>
                    ctx.Request.Headers.ContainsKey(TestAuthHandler.UserHeader)
                        ? TestAuthHandler.Scheme
                        : CookieAuthenticationDefaults.AuthenticationScheme);
            services.Configure<AuthenticationOptions>(options => options.DefaultAuthenticateScheme = "TestOrCookie");
        });
    }

    public HttpClient CreateHttpsClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("https://localhost"),
    });
}

/// <summary>TestServer leaves RemoteIpAddress empty; give requests this factory's address.</summary>
public class ClientIpStartupFilter(IPAddress ip) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((ctx, nextMiddleware) =>
        {
            ctx.Connection.RemoteIpAddress ??= ip;
            return nextMiddleware(ctx);
        });
        next(app);
    };
}

/// <summary>
/// Test-only: POST /test/sign-in-as/{userId}[?legacy=true] issues a real cookie for any user, e.g. a Google-only
/// account whose Google sign-in can't be automated. legacy=true issues a pre-spec-004 cookie without a sid.
/// </summary>
public class TestSignInStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (ctx, nextMiddleware) =>
        {
            const string prefix = "/test/sign-in-as/";
            if (ctx.Request.Method != "POST" || ctx.Request.Path.Value?.StartsWith(prefix) != true)
            {
                await nextMiddleware(ctx);
                return;
            }

            var userId = Guid.Parse(ctx.Request.Path.Value[prefix.Length..]);
            var db = ctx.RequestServices.GetRequiredService<Data.AppDbContext>();
            var user = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(db.Users, u => u.Id == userId);

            if (ctx.Request.Query["legacy"] == "true")
            {
                var now = ctx.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();
                var claims = new List<Claim>
                {
                    new(ClaimTypes.NameIdentifier, user.Id.ToString()),
                    new(ClaimTypes.Email, user.Email),
                    new(AuthClaims.AppUserId, user.Id.ToString()),
                    new(AuthClaims.SessionVersion, user.SessionVersion.ToString()),
                    new(AuthClaims.AuthTime, now.ToUnixTimeSeconds().ToString()),
                };
                await ctx.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));
            }
            else
            {
                await SessionIssuer.SignInAsync(ctx, user);
            }

            ctx.Response.StatusCode = StatusCodes.Status204NoContent;
        });
        next(app);
    };
}

public class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public new const string Scheme = "Test";

    /// <summary>Format: "email|name" (name optional).</summary>
    public const string UserHeader = "X-Test-User";

    public const string UserIdHeader = "X-Test-User-Id";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(UserHeader, out var value))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var parts = value.ToString().Split('|', 2);
        var claims = new List<Claim> { new(ClaimTypes.Email, parts[0]) };
        if (parts.Length > 1 && parts[1] != "")
        {
            claims.Add(new Claim(ClaimTypes.Name, parts[1]));
        }

        if (Request.Headers.TryGetValue(UserIdHeader, out var userId))
        {
            claims.Add(new Claim(AuthClaims.AppUserId, userId.ToString()));
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme)));
    }
}
