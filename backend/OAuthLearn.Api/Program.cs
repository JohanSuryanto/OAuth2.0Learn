using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OAuthLearn.Api.Auth;
using OAuthLearn.Api.Data;
using OAuthLearn.Api.Email;
using OAuthLearn.Api.Passwords;
using OAuthLearn.Api.Throttling;

var builder = WebApplication.CreateBuilder(args);

// Configuration is read lazily (from IConfiguration at resolve time) so test hosts can override it.
builder.Services.AddDbContext<AppDbContext>((sp, options) => options
    .UseNpgsql(sp.GetRequiredService<IConfiguration>().GetConnectionString("Default"))
    .UseSnakeCaseNamingConvention());
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<UserService>();

// Email/password (spec 002).
builder.Services.AddOptions<PasswordHasherOptions>()
    .Configure<IConfiguration>((options, config) =>
    {
        options.CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3;
        // OWASP guidance for PBKDF2-HMAC-SHA512; tests lower it via config for speed.
        options.IterationCount = config.GetValue("Auth:PasswordHashIterations", PasswordHashing.DefaultIterations);
    });
builder.Services.AddSingleton<PasswordHashing>();
builder.Services.AddSingleton<CommonPasswords>();
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<AttemptLimiter>();
builder.Services.AddScoped<AccountService>();
builder.Services.AddScoped<SessionStore>();
builder.Services.AddScoped<AccountSecurityService>();

// The development mailbox must never run anywhere else (research R6).
if (builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddScoped<IEmailSender, DevMailboxEmailSender>();
}
else
{
    throw new InvalidOperationException(
        $"No email sender is configured for the '{builder.Environment.EnvironmentName}' environment. " +
        "The development mailbox only runs in Development/Testing; register a real IEmailSender first.");
}

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        // Must be Cookie, not Google: unauthenticated API calls get 401 instead of a redirect to Google.
        // Only /api/auth/login challenges Google, explicitly.
        options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    })
    .AddCookie(options =>
    {
        options.Cookie.Name = "oauthlearn.auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromMinutes(60);
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
        options.Events.OnValidatePrincipal = SessionValidator.ValidateAsync;
    })
    .AddGoogle(options =>
    {
        options.CallbackPath = "/signin-google";
        options.SaveTokens = false;
        options.Scope.Add("openid");
        options.Events.OnCreatingTicket = GoogleAuthEvents.OnCreatingTicket;
        options.Events.OnAccessDenied = GoogleAuthEvents.OnAccessDenied;
        options.Events.OnRemoteFailure = GoogleAuthEvents.OnRemoteFailure;
        options.Events.OnRedirectToAuthorizationEndpoint = GoogleAuthEvents.OnRedirectToAuthorizationEndpoint;
        options.Events.OnTicketReceived = GoogleAuthEvents.OnTicketReceived;
    });

builder.Services.AddOptions<GoogleOptions>(GoogleDefaults.AuthenticationScheme)
    .Configure<IConfiguration>((options, config) =>
    {
        options.ClientId = config["Authentication:Google:ClientId"]!;
        options.ClientSecret = config["Authentication:Google:ClientSecret"]!;
    });

builder.Services.AddAuthorization();

builder.Services.AddCors();
builder.Services.AddOptions<CorsOptions>()
    .Configure<IConfiguration>((options, config) => options.AddPolicy("Frontend", policy => policy
        .WithOrigins(config["Frontend:Origin"]!)
        .AllowCredentials()
        .WithMethods("GET", "POST")
        .WithHeaders("X-Requested-With")));

var app = builder.Build();

EnsureRequiredConfiguration(app.Configuration);

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    try
    {
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Could not apply database migrations at startup. Is PostgreSQL running?");
    }
}

app.UseSecurityHeaders();
app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();

app.MapAuthEndpoints();
app.MapPasswordEndpoints();
app.MapAccountEndpoints();
app.MapDevMailbox();

app.Run();

static void EnsureRequiredConfiguration(IConfiguration config)
{
    string[] required =
    [
        "ConnectionStrings:Default",
        "Authentication:Google:ClientId",
        "Authentication:Google:ClientSecret",
        "Frontend:Origin",
        "Auth:LookupHashKey",
    ];
    var missing = required.Where(key => string.IsNullOrWhiteSpace(config[key])).ToArray();
    if (missing.Length > 0)
    {
        throw new InvalidOperationException(
            $"Missing configuration: {string.Join(", ", missing)}. " +
            "Set secrets with `dotnet user-secrets set ...` (see specs/001-google-oauth-login/quickstart.md " +
            "and specs/002-email-password-auth/quickstart.md).");
    }
}

public partial class Program { }
