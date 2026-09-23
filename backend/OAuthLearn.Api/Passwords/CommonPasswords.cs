namespace OAuthLearn.Api.Passwords;

/// <summary>The bundled top-10k common password list (research R3), compared case-insensitively.</summary>
public sealed class CommonPasswords
{
    private const string ResourceName = "OAuthLearn.Api.Passwords.common-passwords.txt";

    private readonly HashSet<string> _passwords;

    public CommonPasswords()
    {
        using var stream = typeof(CommonPasswords).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource {ResourceName} not found.");
        using var reader = new StreamReader(stream);

        _passwords = new HashSet<string>(StringComparer.Ordinal);
        while (reader.ReadLine() is { } line)
        {
            var entry = line.Trim();
            if (entry.Length > 0 && !entry.StartsWith('#'))
            {
                _passwords.Add(entry.ToLowerInvariant());
            }
        }
    }

    public int Count => _passwords.Count;

    public bool Contains(string password) => _passwords.Contains(password.ToLowerInvariant());
}
