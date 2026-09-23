namespace OAuthLearn.Api.Email;

/// <summary>
/// Email texts. Links put the token in the URL fragment (#token=...), which browsers never send to servers
/// or in Referer headers (research R5).
/// </summary>
public static class EmailTemplates
{
    public static (string Subject, string Body) VerifyEmail(string origin, string token) => (
        "Verify your email for OAuth2.0 Learn",
        $"""
        Welcome to OAuth2.0 Learn!

        Open this link and enter the password you chose to finish creating your account:

        {origin}/verify-email#token={token}

        The link works once and expires in 24 hours. If you didn't create an account, ignore this email.
        """);

    public static (string Subject, string Body) ResetPassword(string origin, string token) => (
        "Reset your OAuth2.0 Learn password",
        $"""
        Someone asked to reset the password for this email address.

        Open this link to choose a new password:

        {origin}/reset-password#token={token}

        The link works once and expires in 30 minutes. If you didn't ask for this, ignore this email;
        your password stays the same.
        """);

    public static (string Subject, string Body) RegistrationAttempt(string origin) => (
        "Someone tried to create an account with your email",
        $"""
        Someone tried to create an OAuth2.0 Learn account with this email address, but you already have one.

        If it was you, sign in at {origin} or use "Forgot password?" there.
        If it wasn't you, you can ignore this email; nothing was changed.
        """);
}
