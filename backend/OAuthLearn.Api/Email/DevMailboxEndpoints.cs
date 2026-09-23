using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using OAuthLearn.Api.Data;

namespace OAuthLearn.Api.Email;

/// <summary>GET /dev/mailbox: the local development mailbox (research R6). Mapped only in Development.</summary>
public static partial class DevMailboxEndpoints
{
    public static IEndpointRouteBuilder MapDevMailbox(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            return app;
        }

        app.MapGet("/dev/mailbox", async (HttpContext ctx, AppDbContext db) =>
        {
            var messages = await db.MailboxMessages.AsNoTracking()
                .OrderByDescending(m => m.CreatedAt)
                .Take(50)
                .ToListAsync(ctx.RequestAborted);

            // No scripts on this page; inline styles only (overrides the API's stricter default CSP).
            ctx.Response.Headers.ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'; frame-ancestors 'none'";
            return Results.Content(Render(messages), "text/html; charset=utf-8");
        });

        return app;
    }

    private static string Render(List<MailboxMessage> messages)
    {
        var html = new StringBuilder();
        html.Append("""
            <!doctype html>
            <html lang="en"><head><meta charset="utf-8"><title>Dev mailbox</title>
            <style>
              body { font-family: system-ui, sans-serif; max-width: 860px; margin: 24px auto; padding: 0 16px; color: #1c1f24; }
              .msg { border: 1px solid #e2e5ea; border-radius: 8px; padding: 12px 16px; margin: 12px 0; }
              .meta { color: #5d6470; font-size: 0.85rem; }
              pre { white-space: pre-wrap; font-family: inherit; }
            </style></head><body>
            <h1>Development mailbox</h1>
            <p class="meta">Emails the app would have sent. Newest first, last 50. Development only.</p>
            """);

        if (messages.Count == 0)
        {
            html.Append("<p>No messages yet.</p>");
        }

        foreach (var message in messages)
        {
            html.Append("<div class=\"msg\"><div class=\"meta\">To: ")
                .Append(WebUtility.HtmlEncode(message.ToAddress))
                .Append(" · ")
                .Append(message.CreatedAt.ToString("u"))
                .Append("</div><h3>")
                .Append(WebUtility.HtmlEncode(message.Subject))
                .Append("</h3><pre>")
                .Append(Linkify(WebUtility.HtmlEncode(message.BodyText)))
                .Append("</pre></div>");
        }

        html.Append("</body></html>");
        return html.ToString();
    }

    // Runs on already-encoded text, so URLs can't carry markup.
    private static string Linkify(string encoded) =>
        UrlPattern().Replace(encoded, m => $"<a href=\"{m.Value}\">{m.Value}</a>");

    [GeneratedRegex(@"https?://[^\s<""]+")]
    private static partial Regex UrlPattern();
}
