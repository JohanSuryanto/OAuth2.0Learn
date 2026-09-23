using OAuthLearn.Api.Data;
using OAuthLearn.Api.Passwords;
using OAuthLearn.Api.Throttling;

namespace OAuthLearn.Api.Auth;

public record RegisterRequest(string? Email, string? Password, string? DisplayName);

public record VerifyEmailRequest(string? Token, string? Password);

public record SignInRequest(string? Email, string? Password);

public record ForgotPasswordRequest(string? Email);

public record ResetPasswordRequest(string? Token, string? NewPassword);

/// <summary>Email/password endpoints (specs/002-email-password-auth/contracts/password-auth-api.md).</summary>
public static class PasswordEndpoints
{
    public static IEndpointRouteBuilder MapPasswordEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth")
            .AddEndpointFilter<RequireFetchHeaderFilter>()
            .AllowAnonymous();

        group.MapPost("/register", async (RegisterRequest request, AccountService accounts, HttpContext ctx) =>
        {
            var result = await accounts.RegisterAsync(
                request.Email, request.Password, request.DisplayName, AttemptLimiter.ClientIp(ctx), ctx.RequestAborted);
            return result.Status switch
            {
                AccountStatus.Ok => Accepted(),
                AccountStatus.Invalid => ValidationFailed(result.Errors!),
                _ => TooManyAttempts(),
            };
        });

        group.MapPost("/verify-email", async (VerifyEmailRequest request, AccountService accounts, HttpContext ctx) =>
        {
            var result = await accounts.VerifyEmailAsync(
                request.Token, request.Password, AttemptLimiter.ClientIp(ctx), ctx.RequestAborted);
            switch (result.Status)
            {
                case AccountStatus.Ok:
                    await SessionIssuer.SignInAsync(ctx, result.User!);
                    return Results.NoContent();
                case AccountStatus.TokenInvalid:
                    return ValidationFailed("token", PasswordPolicy.Codes.TokenInvalid);
                case AccountStatus.PasswordMismatch:
                    return ValidationFailed("password", PasswordPolicy.Codes.PasswordMismatch);
                default:
                    return TooManyAttempts();
            }
        });

        group.MapPost("/password/sign-in", async (SignInRequest request, AccountService accounts, HttpContext ctx) =>
        {
            var result = await accounts.SignInAsync(
                request.Email, request.Password, AttemptLimiter.ClientIp(ctx), ctx.RequestAborted);
            switch (result.Status)
            {
                case AccountStatus.Ok:
                    await SessionIssuer.SignInAsync(ctx, result.User!);
                    return Results.NoContent();
                case AccountStatus.EmailNotVerified:
                    return Code(StatusCodes.Status403Forbidden, "email_not_verified");
                case AccountStatus.TooManyAttempts:
                    return TooManyAttempts();
                default:
                    return Code(StatusCodes.Status401Unauthorized, "invalid_credentials");
            }
        });

        group.MapPost("/forgot-password", async (ForgotPasswordRequest request, AccountService accounts, HttpContext ctx) =>
        {
            var result = await accounts.ForgotPasswordAsync(request.Email, AttemptLimiter.ClientIp(ctx), ctx.RequestAborted);
            return result.Status == AccountStatus.Invalid ? ValidationFailed(result.Errors!) : Accepted();
        });

        group.MapPost("/reset-password", async (ResetPasswordRequest request, AccountService accounts, HttpContext ctx) =>
        {
            var result = await accounts.ResetPasswordAsync(request.Token, request.NewPassword, ctx.RequestAborted);
            return result.Status switch
            {
                AccountStatus.Ok => Results.NoContent(),
                AccountStatus.TokenInvalid => ValidationFailed("token", PasswordPolicy.Codes.TokenInvalid),
                _ => ValidationFailed(result.Errors!),
            };
        });

        return app;
    }

    private static IResult Accepted() => Results.Json(new { }, statusCode: StatusCodes.Status202Accepted);

    private static IResult TooManyAttempts() => Code(StatusCodes.Status429TooManyRequests, "too_many_attempts");

    private static IResult Code(int status, string code) => Results.Json(new { code }, statusCode: status);

    private static IResult ValidationFailed(IReadOnlyDictionary<string, string> errors) =>
        Results.Json(new { errors }, statusCode: StatusCodes.Status400BadRequest);

    private static IResult ValidationFailed(string field, string code) =>
        ValidationFailed(new Dictionary<string, string> { [field] = code });
}
