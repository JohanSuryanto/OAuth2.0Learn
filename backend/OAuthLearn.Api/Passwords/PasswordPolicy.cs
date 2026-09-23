using System.Net.Mail;
using OAuthLearn.Api.Data;

namespace OAuthLearn.Api.Passwords;

/// <summary>Server-side input rules (spec 002 FR-003–FR-005, research R12). Each returns an error code or null.</summary>
public static class PasswordPolicy
{
    public const int PasswordMinLength = 10;
    public const int PasswordMaxLength = 128;
    public const int EmailMaxLength = 254;
    public const int DisplayNameMaxLength = 100;

    public static class Codes
    {
        public const string EmailInvalid = "email_invalid";
        public const string PasswordTooShort = "password_too_short";
        public const string PasswordTooLong = "password_too_long";
        public const string PasswordIsEmail = "password_is_email";
        public const string PasswordCommon = "password_common";
        public const string DisplayNameTooLong = "display_name_too_long";
        public const string PasswordMismatch = "password_mismatch";
        public const string TokenInvalid = "token_invalid";
    }

    public static string? ValidateEmail(string? email)
    {
        var value = email?.Trim() ?? "";
        if (value.Length == 0 || value.Length > EmailMaxLength)
        {
            return Codes.EmailInvalid;
        }

        return MailAddress.TryCreate(value, out var address)
            && address.Address == value
            && address.Host.Contains('.')
                ? null
                : Codes.EmailInvalid;
    }

    public static string? ValidateNewPassword(string? password, string email, CommonPasswords common)
    {
        password ??= "";
        if (password.Length < PasswordMinLength)
        {
            return Codes.PasswordTooShort;
        }

        if (password.Length > PasswordMaxLength)
        {
            return Codes.PasswordTooLong;
        }

        if (email.Trim().Length > 0 && UserService.NormalizeEmail(password) == UserService.NormalizeEmail(email))
        {
            return Codes.PasswordIsEmail;
        }

        return common.Contains(password) ? Codes.PasswordCommon : null;
    }

    public static string? ValidateDisplayName(string? displayName) =>
        (displayName?.Trim().Length ?? 0) > DisplayNameMaxLength ? Codes.DisplayNameTooLong : null;
}
