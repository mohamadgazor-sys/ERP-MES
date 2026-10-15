using ERP.MES.Application.Security;
using ERP.MES.Infrastructure.Security;

namespace ERP.MES.Infrastructure.Tests;

// Password hashing and verification (Decision Log #27, #41, #42): PBKDF2-HMAC-SHA256, a fresh salt per password, parameters stored with each hash.
public class PasswordHasherTests
{
    private readonly Pbkdf2PasswordHasher _hasher = new();

    private static PasswordHashRecord Known(int iterations, string hex) =>
        new(Convert.FromHexString(hex), "salt"u8.ToArray(), Pbkdf2PasswordHasher.AlgorithmName, 1, iterations);

    [Fact]
    public void The_approved_baseline_is_pbkdf2_hmac_sha256_with_600000_iterations()
    {
        Assert.Equal("PBKDF2-HMAC-SHA256", Pbkdf2PasswordHasher.AlgorithmName);
        Assert.Equal(1, Pbkdf2PasswordHasher.CurrentVersion);
        Assert.Equal(600_000, AuthenticationPolicy.Default.Pbkdf2Iterations);
        Assert.Equal(16, Pbkdf2PasswordHasher.SaltBytes);
        Assert.Equal(32, Pbkdf2PasswordHasher.HashBytes);
    }

    // Standard PBKDF2-HMAC-SHA256 test vectors (password "password", salt "salt"), computed independently of this code.
    [Theory]
    [InlineData(1, "120fb6cffcf8b32c43e7225256c4f837a86548c92ccc35480805987cb70be17b")]
    [InlineData(2, "ae4d0c95af6b46d32d0adff928f06dd02a303f8ef3c251dfd6e2d85a95474c43")]
    [InlineData(4096, "c5e478d59288c841aa530db6845c4c8d962893a001ce4e11a4963873aa98134a")]
    public void Verification_matches_the_standard_pbkdf2_hmac_sha256_vectors(int iterations, string expectedHex)
    {
        Assert.True(_hasher.Verify("password", Known(iterations, expectedHex)));
        Assert.False(_hasher.Verify("Password", Known(iterations, expectedHex)));
    }

    [Fact]
    public void A_hash_records_algorithm_version_salt_work_factor_and_a_32_byte_hash()
    {
        var record = _hasher.Hash("a long enough password", 1000);

        Assert.Equal("PBKDF2-HMAC-SHA256", record.Algorithm);
        Assert.Equal(1, record.Version);
        Assert.Equal(1000, record.WorkFactor);
        Assert.Equal(16, record.Salt.Length);
        Assert.Equal(32, record.Hash.Length);
        Assert.True(_hasher.Verify("a long enough password", record));
        Assert.False(_hasher.Verify("a long enough passwore", record));
        Assert.False(_hasher.Verify(string.Empty, record));
    }

    [Fact]
    public void Every_password_gets_a_fresh_random_salt_so_equal_passwords_hash_differently()
    {
        var first = _hasher.Hash("same password here", 1000);
        var second = _hasher.Hash("same password here", 1000);

        Assert.NotEqual(first.Salt, second.Salt);
        Assert.NotEqual(first.Hash, second.Hash);
        Assert.True(_hasher.Verify("same password here", first));
        Assert.True(_hasher.Verify("same password here", second));
    }

    [Fact]
    public void Verification_uses_the_stored_work_factor_not_a_global_setting()
    {
        var old = _hasher.Hash("old work factor password", 1000);
        var current = _hasher.Hash("old work factor password", 2000);

        Assert.True(_hasher.Verify("old work factor password", old));
        Assert.True(_hasher.Verify("old work factor password", current));

        // Pretending the hash was made with another work factor must not verify: the stored value is what counts.
        Assert.False(_hasher.Verify("old work factor password", old with { WorkFactor = 2000 }));
    }

    [Fact]
    public void The_password_is_used_exactly_as_typed_without_trimming_or_normalisation()
    {
        var record = _hasher.Hash(" spaced password ", 1000);

        Assert.True(_hasher.Verify(" spaced password ", record));
        Assert.False(_hasher.Verify("spaced password", record));
        Assert.False(_hasher.Verify(" Spaced password ", record));
    }

    [Fact]
    public void An_unknown_algorithm_or_version_or_empty_parameters_never_verify()
    {
        var record = _hasher.Hash("a long enough password", 1000);

        Assert.False(_hasher.Verify("a long enough password", record with { Algorithm = "PBKDF2-HMAC-SHA1" }));
        Assert.False(_hasher.Verify("a long enough password", record with { Version = 2 }));
        Assert.False(_hasher.Verify("a long enough password", record with { Salt = [] }));
        Assert.False(_hasher.Verify("a long enough password", record with { WorkFactor = 0 }));
    }

    [Fact]
    public void A_hash_below_the_current_baseline_needs_an_upgrade_and_one_at_or_above_does_not()
    {
        var record = _hasher.Hash("a long enough password", 1000);

        Assert.True(_hasher.NeedsUpgrade(record, 2000));
        Assert.False(_hasher.NeedsUpgrade(record, 1000));
        Assert.False(_hasher.NeedsUpgrade(record, 500));
        Assert.True(_hasher.NeedsUpgrade(record with { Algorithm = "other" }, 500));
        Assert.True(_hasher.NeedsUpgrade(record with { Version = 0 }, 500));
    }

    [Fact]
    public void A_hash_at_the_approved_baseline_work_factor_verifies()
    {
        // One real 600,000-iteration hash, so the approved baseline itself is exercised.
        var record = _hasher.Hash("baseline password check", AuthenticationPolicy.Default.Pbkdf2Iterations);

        Assert.Equal(600_000, record.WorkFactor);
        Assert.True(_hasher.Verify("baseline password check", record));
        Assert.False(_hasher.NeedsUpgrade(record, AuthenticationPolicy.Default.Pbkdf2Iterations));
    }

    [Fact]
    public void Invalid_input_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => _hasher.Hash(null!, 1000));
        Assert.Throws<ArgumentOutOfRangeException>(() => _hasher.Hash("a long enough password", 0));
        Assert.Throws<ArgumentNullException>(() => _hasher.Verify(null!, _hasher.Hash("a long enough password", 1000)));
    }
}
