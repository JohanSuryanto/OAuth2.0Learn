using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using OAuthLearn.Api.Data;

namespace OAuthLearn.Api.Passwords;

public enum PasswordCheck
{
    Failed,
    Success,
    SuccessRehashNeeded,
}

/// <summary>
/// ASP.NET Core's PasswordHasher on its own (research R1): PBKDF2-HMAC-SHA512, random salt, versioned format,
/// iterations from <see cref="PasswordHasherOptions"/>. Every <see cref="Verify"/> call costs exactly one hash,
/// even without a stored hash, so response times don't reveal which accounts exist (research R2).
/// </summary>
public sealed class PasswordHashing
{
    public const int DefaultIterations = 210_000;

    private readonly PasswordHasher<User> _hasher;
    private readonly string _dummyHash;

    public PasswordHashing(IOptions<PasswordHasherOptions> options)
    {
        _hasher = new PasswordHasher<User>(options);
        _dummyHash = _hasher.HashPassword(null!, "timing-equalizer-" + Guid.NewGuid());
    }

    public string Hash(string password) => _hasher.HashPassword(null!, password);

    public PasswordCheck Verify(string? hash, string password)
    {
        var result = _hasher.VerifyHashedPassword(null!, hash ?? _dummyHash, password);
        if (hash is null)
        {
            return PasswordCheck.Failed;
        }

        return result switch
        {
            PasswordVerificationResult.Success => PasswordCheck.Success,
            PasswordVerificationResult.SuccessRehashNeeded => PasswordCheck.SuccessRehashNeeded,
            _ => PasswordCheck.Failed,
        };
    }
}
