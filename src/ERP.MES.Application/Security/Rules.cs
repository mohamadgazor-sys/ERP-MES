using System.Globalization;
using System.Security.Cryptography;
using ERP.MES.Application.Persistence;

namespace ERP.MES.Application.Security;

public enum PasswordPolicyError
{
    None = 0,
    Empty,
    TooShort,
    ContainsSpace,
}

/// <summary>
/// The password policy (Decision Log #72): a minimum length counted in characters, and no space character (U+0020) anywhere. Nothing else is required or forbidden:
/// there are no composition rules. The password is used exactly as typed - it is never trimmed or normalised - so a password with a space is rejected, not repaired.
/// The same rule applies wherever a user chooses a new password; signing in with an existing password only verifies it and never applies this policy.
/// </summary>
public static class PasswordRules
{
    public static PasswordPolicyError Validate(string? password, AuthenticationPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (string.IsNullOrEmpty(password))
        {
            return PasswordPolicyError.Empty;
        }

        if (password.Contains(' ', StringComparison.Ordinal))
        {
            return PasswordPolicyError.ContainsSpace;
        }

        return new StringInfo(password).LengthInTextElements < policy.MinimumPasswordLength ? PasswordPolicyError.TooShort : PasswordPolicyError.None;
    }
}

/// <summary>
/// One-time secret tokens (bootstrap and recovery, Decision Log #39, #47): 32 random bytes from the operating-system generator, shown once as 64
/// hexadecimal digits in groups. Only the SHA-256 digest is stored; a high-entropy random token needs no slow hash. Comparison is constant-time.
/// </summary>
internal static class SecretTokens
{
    public const int ByteLength = 32;

    public static (byte[] Digest, string Text) Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(ByteLength);
        return (SHA256.HashData(bytes), Format(bytes));
    }

    public static string Format(byte[] bytes)
    {
        var hex = Convert.ToHexString(bytes).ToLowerInvariant();
        return string.Join('-', Enumerable.Range(0, hex.Length / 8).Select(i => hex.Substring(i * 8, 8)));
    }

    /// <summary>The digest of the typed token. An unreadable text yields the digest of random bytes, so it simply does not match and is counted like any wrong token.</summary>
    public static byte[] DigestOf(string? text)
    {
        var compact = new string((text ?? string.Empty).Where(c => c != '-' && !char.IsWhiteSpace(c)).ToArray());
        if (compact.Length == ByteLength * 2 && compact.All(Uri.IsHexDigit))
        {
            return SHA256.HashData(Convert.FromHexString(compact));
        }

        return SHA256.HashData(RandomNumberGenerator.GetBytes(ByteLength));
    }

    public static bool Matches(byte[]? stored, byte[] entered) => stored is not null && CryptographicOperations.FixedTimeEquals(stored, entered);
}

/// <summary>Re-runs a piece of work that lost an optimistic-concurrency race. The work re-reads its state each time, so it decides afresh.</summary>
internal static class ConflictRetry
{
    private const int MaximumAttempts = 8;

    public static async Task<T> RunAsync<T>(Func<Task<T>> work)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await work();
            }
            catch (ConcurrencyConflictException) when (attempt < MaximumAttempts)
            {
                // Another writer changed one of the rows; try again from a fresh read.
            }
        }
    }
}
