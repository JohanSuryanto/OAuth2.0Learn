using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace OAuthLearn.Api.Tests;

/// <summary>FR-018 / SC-005: logs never contain passwords, raw tokens, or the email of a failed attempt.</summary>
[Collection(PostgresCollection.Name)]
public sealed class LoggingSafetyTests(PostgresFixture db) : IDisposable
{
    private readonly CapturingLoggerProvider _logs = new();
    private readonly TestAppFactory _factory = new(db);

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task No_secrets_or_failed_attempt_emails_in_logs()
    {
        var app = _factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<ILoggerProvider>(_logs)));
        var client = app.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

        const string password = "very secret passphrase";
        const string newPassword = "another secret passphrase";
        var email = TestHelpers.UniqueEmail("logged");
        var unknownEmail = TestHelpers.UniqueEmail("unknownattacker");

        await client.PostJsonAsync("/api/auth/register", new { email, password });
        var verifyToken = await db.LatestTokenAsync(email, "/verify-email");
        await client.PostJsonAsync("/api/auth/verify-email", new { token = verifyToken, password = "wrong guess 123" });
        await client.PostJsonAsync("/api/auth/verify-email", new { token = verifyToken, password });
        await client.PostJsonAsync("/api/auth/password/sign-in", new { email, password = "wrong guess 456" });
        await client.PostJsonAsync("/api/auth/password/sign-in", new { email = unknownEmail, password = "wrong guess 789" });
        await client.PostJsonAsync("/api/auth/password/sign-in", new { email, password });
        await client.PostJsonAsync("/api/auth/forgot-password", new { email });
        await client.PostJsonAsync("/api/auth/forgot-password", new { email = unknownEmail });
        var resetToken = await db.LatestTokenAsync(email, "/reset-password");
        await client.PostJsonAsync("/api/auth/reset-password", new { token = resetToken, newPassword });

        var all = string.Join("\n", _logs.Messages);
        Assert.Contains("Password sign-in failed", all); // the events are logged...
        foreach (var secret in new[] { password, newPassword, "wrong guess", verifyToken!, resetToken!, unknownEmail })
        {
            Assert.DoesNotContain(secret, all); // ...without the sensitive values
        }
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Messages);

        public void Dispose() { }

        private sealed class CapturingLogger(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                messages.Enqueue(formatter(state, exception) + " " + exception);
        }
    }
}
