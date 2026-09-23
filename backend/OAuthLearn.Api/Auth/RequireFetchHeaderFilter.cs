namespace OAuthLearn.Api.Auth;

/// <summary>
/// CSRF guard for state-changing endpoints (FR-017, research R10): cross-site forms cannot set custom headers,
/// and CORS only lets the app's own origin send this one. Also blocks login CSRF.
/// </summary>
public class RequireFetchHeaderFilter : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next) =>
        context.HttpContext.Request.Headers["X-Requested-With"] == "fetch"
            ? next(context)
            : ValueTask.FromResult<object?>(Results.BadRequest());
}
