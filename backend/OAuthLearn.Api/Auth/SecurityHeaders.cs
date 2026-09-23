namespace OAuthLearn.Api.Auth;

public static class SecurityHeaders
{
    /// <summary>Adds defensive headers to every response (FR-021, research R5c S5).</summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (ctx, next) =>
        {
            ctx.Response.OnStarting(() =>
            {
                var headers = ctx.Response.Headers;
                headers.XContentTypeOptions = "nosniff";
                headers.XFrameOptions = "DENY";
                headers["Referrer-Policy"] = "no-referrer";
                // Endpoints may set a (still strict) CSP of their own, e.g. /dev/mailbox.
                if (!headers.ContainsKey("Content-Security-Policy"))
                {
                    headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
                }

                // Account and session data must never be stored by the browser or caches (spec 003, research R8).
                if (ctx.Request.Path.StartsWithSegments("/api"))
                {
                    headers.CacheControl = "no-store";
                }

                return Task.CompletedTask;
            });
            await next(ctx);
        });
}
