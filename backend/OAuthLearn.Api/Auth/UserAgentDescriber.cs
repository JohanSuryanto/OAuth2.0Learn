namespace OAuthLearn.Api.Auth;

/// <summary>Turns a User-Agent header into "{Browser} on {OS}" for the sessions list (spec 004, research R9).</summary>
public static class UserAgentDescriber
{
    public const int MaxLength = 200;

    public static string? Describe(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return null;
        }

        var browser = Browser(userAgent);
        var os = OperatingSystem(userAgent);
        var description = (browser, os) switch
        {
            (null, null) => null,
            (not null, not null) => $"{browser} on {os}",
            (not null, null) => browser,
            (null, not null) => $"Browser on {os}",
        };

        return description is { Length: > MaxLength } ? description[..MaxLength] : description;
    }

    // Order matters: Edge and Opera also say "Chrome", and Chrome also says "Safari".
    private static string? Browser(string ua) =>
        Has(ua, "Edg/") || Has(ua, "Edge/") ? "Edge"
        : Has(ua, "OPR/") || Has(ua, "Opera") ? "Opera"
        : Has(ua, "Firefox/") || Has(ua, "FxiOS/") ? "Firefox"
        : Has(ua, "Chrome/") || Has(ua, "CriOS/") || Has(ua, "Chromium/") ? "Chrome"
        : Has(ua, "Safari/") && Has(ua, "Version/") ? "Safari"
        : null;

    // Order matters: Android also says "Linux", iOS also says "Mac OS X".
    private static string? OperatingSystem(string ua) =>
        Has(ua, "Windows") ? "Windows"
        : Has(ua, "iPhone") || Has(ua, "iPad") || Has(ua, "iPod") ? "iOS"
        : Has(ua, "Android") ? "Android"
        : Has(ua, "CrOS") ? "ChromeOS"
        : Has(ua, "Mac OS X") || Has(ua, "Macintosh") ? "macOS"
        : Has(ua, "Linux") ? "Linux"
        : null;

    private static bool Has(string ua, string token) => ua.Contains(token, StringComparison.OrdinalIgnoreCase);
}
