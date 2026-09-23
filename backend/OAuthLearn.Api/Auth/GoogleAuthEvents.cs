using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OAuth;
using OAuthLearn.Api.Data;

namespace OAuthLearn.Api.Auth;

/// <summary>Sign-in was refused by our own rules; <see cref="ReasonCode"/> is safe to log.</summary>
public class SignInRejectedException(string reasonCode, string message) : Exception(message)
{
    public string ReasonCode { get; } = reasonCode;
}

public static class GoogleAuthEvents
{
    private const string LoggerCategory = "OAuthLearn.Auth.Google";

    /// <summary>
    /// True only when Google explicitly says the email is verified. Google's userinfo v3 uses
    /// <c>email_verified</c>, v2 uses <c>verified_email</c>. Missing or anything else fails closed (FR-018).
    /// </summary>
    public static bool IsEmailVerified(JsonElement userInfo)
    {
        if (userInfo.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var name in new[] { "email_verified", "verified_email" })
        {
            if (userInfo.TryGetProperty(name, out var value))
            {
                return value.ValueKind == JsonValueKind.True
                    || (value.ValueKind == JsonValueKind.String && value.GetString() == "true");
            }
        }

        return false;
    }

    /// <summary>Runs after Google returned the user's profile, before the cookie is issued.</summary>
    public static async Task OnCreatingTicket(OAuthCreatingTicketContext context)
    {
        var identity = context.Identity
            ?? throw new SignInRejectedException("missing_identity", "No identity was created.");

        var sub = identity.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var email = identity.FindFirst(ClaimTypes.Email)?.Value;
        var name = identity.FindFirst(ClaimTypes.Name)?.Value;

        if (string.IsNullOrEmpty(sub) || string.IsNullOrEmpty(email))
        {
            throw new SignInRejectedException("missing_email", "Google account has no email");
        }

        if (!IsEmailVerified(context.User))
        {
            throw new SignInRejectedException("email_not_verified", "Google email not verified");
        }

        var services = context.HttpContext.RequestServices;
        var ct = context.HttpContext.RequestAborted;

        // Re-authentication (spec 004, research R6): confirm it's the linked Google account, then stop —
        // OnTicketReceived skips the sign-in, so the current session is left untouched.
        if (IsReauth(context.Properties))
        {
            var uid = Guid.Parse(context.Properties.Items[ReauthUserKey]!);
            var sid = Guid.Parse(context.Properties.Items[ReauthSidKey]!);
            await services.GetRequiredService<AccountSecurityService>().CompleteGoogleReauthAsync(uid, sid, sub, ct);
            return;
        }

        var user = await services.GetRequiredService<UserService>().UpsertFromGoogleAsync(sub, email, name, ct);

        // The header shows the stored display name, not Google's (spec 004, research R8).
        foreach (var claim in identity.FindAll(ClaimTypes.Name).ToList())
        {
            identity.RemoveClaim(claim);
        }

        if (user.DisplayName is not null)
        {
            identity.AddClaim(new Claim(ClaimTypes.Name, user.DisplayName));
        }

        var now = services.GetRequiredService<TimeProvider>().GetUtcNow();
        var session = await services.GetRequiredService<SessionStore>().CreateAsync(user.Id, context.HttpContext, now, ct);
        identity.AddClaims(AuthClaims.SessionClaims(user, session.Id, now));

        GetLogger(context.HttpContext).LogInformation("User signed in. UserId={UserId}", user.Id);
    }

    public const string PurposeKey = "purpose";
    public const string ReauthPurpose = "reauth";
    public const string ReauthUserKey = "uid";
    public const string ReauthSidKey = "sid";

    public static bool IsReauth(AuthenticationProperties? properties) =>
        properties?.Items.TryGetValue(PurposeKey, out var purpose) == true && purpose == ReauthPurpose;

    /// <summary>Re-authentication must really ask for credentials again: max_age=0 (research R6).</summary>
    public static Task OnRedirectToAuthorizationEndpoint(RedirectContext<OAuthOptions> context)
    {
        var uri = IsReauth(context.Properties)
            ? context.RedirectUri + "&prompt=select_account&max_age=0"
            : context.RedirectUri;
        context.Response.Redirect(uri);
        return Task.CompletedTask;
    }

    /// <summary>Re-authentication finished: report back to the popup without signing in.</summary>
    public static Task OnTicketReceived(TicketReceivedContext context)
    {
        if (IsReauth(context.Properties))
        {
            context.Response.Redirect("/api/auth/popup-complete?result=reauth_ok");
            context.HandleResponse();
        }

        return Task.CompletedTask;
    }

    /// <summary>Google returned error=access_denied: the user cancelled or declined consent.</summary>
    public static Task OnAccessDenied(AccessDeniedContext context)
    {
        GetLogger(context.HttpContext).LogInformation("Sign-in failed. Reason={Reason}", "access_denied");
        context.Response.Redirect("/api/auth/popup-complete?error=access_denied");
        context.HandleResponse();
        return Task.CompletedTask;
    }

    /// <summary>Any other failure: DB error, missing/unverified email, invalid state, token exchange error.</summary>
    public static Task OnRemoteFailure(RemoteFailureContext context)
    {
        var logger = GetLogger(context.HttpContext);
        var error = "signin_failed";
        if (context.Failure is SignInRejectedException rejected)
        {
            logger.LogWarning("Sign-in failed. Reason={Reason}", rejected.ReasonCode);
            if (rejected.ReasonCode == "reauth_mismatch")
            {
                error = "reauth_mismatch";
            }
        }
        else
        {
            logger.LogWarning(context.Failure, "Sign-in failed. Reason={Reason}", "remote_failure");
        }

        context.Response.Redirect("/api/auth/popup-complete?error=" + error);
        context.HandleResponse();
        return Task.CompletedTask;
    }

    private static ILogger GetLogger(HttpContext httpContext) =>
        httpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(LoggerCategory);
}
