using System.Security.Cryptography;
using ERP.MES.Application.Security;

namespace ERP.MES.Infrastructure.Security;

/// <summary>
/// PBKDF2-HMAC-SHA256 with the built-in .NET implementation (Decision Log #27, #41; Milestone 1 plan 6.2): a fresh random 16-byte salt per password, a
/// 32-byte derived key, the work factor stored with each hash, and constant-time comparison. Verification uses the stored parameters, never a global setting.
/// The password is hashed exactly as typed (UTF-8, no trimming or normalisation).
/// </summary>
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    public const string AlgorithmName = "PBKDF2-HMAC-SHA256";

    /// <summary>The baseline version (algorithm parameters other than the work factor). Changing the baseline is a reviewed change recorded in the Decision Log.</summary>
    public const int CurrentVersion = 1;

    public const int SaltBytes = 16;

    public const int HashBytes = 32;

    public PasswordHashRecord Hash(string password, int workFactor)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentOutOfRangeException.ThrowIfLessThan(workFactor, 1);

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, workFactor, HashAlgorithmName.SHA256, HashBytes);
        return new PasswordHashRecord(hash, salt, AlgorithmName, CurrentVersion, workFactor);
    }

    public bool Verify(string password, PasswordHashRecord stored)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(stored);

        if (stored.Algorithm != AlgorithmName || stored.Version != CurrentVersion || stored.WorkFactor < 1 || stored.Salt.Length == 0 || stored.Hash.Length == 0)
        {
            return false;
        }

        var derived = Rfc2898DeriveBytes.Pbkdf2(password, stored.Salt, stored.WorkFactor, HashAlgorithmName.SHA256, stored.Hash.Length);
        return CryptographicOperations.FixedTimeEquals(derived, stored.Hash);
    }

    public bool NeedsUpgrade(PasswordHashRecord stored, int configuredWorkFactor)
    {
        ArgumentNullException.ThrowIfNull(stored);
        return stored.Algorithm != AlgorithmName || stored.Version < CurrentVersion || stored.WorkFactor < configuredWorkFactor;
    }
}
