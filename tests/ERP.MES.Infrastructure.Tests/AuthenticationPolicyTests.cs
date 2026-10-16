using ERP.MES.Application.Security;
using ERP.MES.Infrastructure.Security;
using ERP.MES.Tests.Shared;

namespace ERP.MES.Infrastructure.Tests;

// The approved defaults (Decision Log #39-#41), their safety floors, and the settings file (Milestone 1 plan 6.1).
public class AuthenticationPolicyTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "erpmes-policy-" + Guid.NewGuid().ToString("N"));

    public AuthenticationPolicyTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string Write(string name, string json)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void The_defaults_are_the_approved_values_and_are_valid()
    {
        var policy = AuthenticationPolicy.Default;

        Assert.Equal(12, policy.MinimumPasswordLength);
        Assert.Equal(5, policy.MaxFailedAttempts);
        Assert.Equal(5, policy.LockoutMinutes);
        Assert.Equal(600_000, policy.Pbkdf2Iterations);
        Assert.Equal(24, policy.BootstrapTokenLifetimeHours);
        Assert.Equal(5, policy.BootstrapMaxAttempts);
        Assert.Equal(5, policy.RecoveryMaxAttempts);
        Assert.Empty(policy.Validate());
    }

    [Theory]
    [InlineData("MinimumPasswordLength", 7)]
    [InlineData("MaxFailedAttempts", 2)]
    [InlineData("MaxFailedAttempts", 11)]
    [InlineData("LockoutMinutes", 0)]
    [InlineData("Pbkdf2Iterations", 309_999)]
    [InlineData("BootstrapTokenLifetimeHours", 0)]
    [InlineData("BootstrapMaxAttempts", 1)]
    [InlineData("RecoveryMaxAttempts", 50)]
    [InlineData("RecoveryTokenLifetimeHours", 100_000)]
    public void A_value_below_its_safety_floor_is_reported(string setting, int value)
    {
        var policy = setting switch
        {
            "MinimumPasswordLength" => new AuthenticationPolicy { MinimumPasswordLength = value },
            "MaxFailedAttempts" => new AuthenticationPolicy { MaxFailedAttempts = value },
            "LockoutMinutes" => new AuthenticationPolicy { LockoutMinutes = value },
            "Pbkdf2Iterations" => new AuthenticationPolicy { Pbkdf2Iterations = value },
            "BootstrapTokenLifetimeHours" => new AuthenticationPolicy { BootstrapTokenLifetimeHours = value },
            "BootstrapMaxAttempts" => new AuthenticationPolicy { BootstrapMaxAttempts = value },
            "RecoveryMaxAttempts" => new AuthenticationPolicy { RecoveryMaxAttempts = value },
            _ => new AuthenticationPolicy { RecoveryTokenLifetimeHours = value },
        };

        var problems = policy.Validate();

        Assert.Single(problems);
        Assert.Contains(setting, problems[0], StringComparison.Ordinal);
    }

    [Fact]
    public void The_values_at_the_floors_are_accepted()
    {
        var policy = new AuthenticationPolicy
        {
            MinimumPasswordLength = 8,
            MaxFailedAttempts = 3,
            LockoutMinutes = 1,
            Pbkdf2Iterations = 310_000,
        };

        Assert.Empty(policy.Validate());
    }

    [Theory]
    [InlineData("12345678901", PasswordPolicyError.TooShort)]
    [InlineData("123456789012", PasswordPolicyError.None)]
    [InlineData("", PasswordPolicyError.Empty)]
    [InlineData(null, PasswordPolicyError.Empty)]
    public void The_password_length_is_counted_in_characters_with_no_composition_rules(string? password, PasswordPolicyError expected)
    {
        Assert.Equal(expected, PasswordRules.Validate(password, AuthenticationPolicy.Default));
    }

    [Fact]
    public void Persian_text_counts_by_characters_not_by_bytes()
    {
        // Seven Persian letters are fourteen bytes in UTF-8 but only seven characters.
        Assert.Equal(PasswordPolicyError.TooShort, PasswordRules.Validate("گذرواژه", AuthenticationPolicy.Default));
        Assert.Equal(PasswordPolicyError.None, PasswordRules.Validate("گذرواژه-امن-۱۲۳", AuthenticationPolicy.Default));
    }

    [Fact]
    public void The_committed_desktop_settings_hold_exactly_the_approved_defaults()
    {
        var path = Path.Combine(SolutionLayout.Root, "src", "ERP.MES.Desktop", "appsettings.json");

        Assert.Equal(AuthenticationPolicy.Default, AuthenticationPolicyFile.Load(path));
    }

    [Fact]
    public void Missing_files_give_the_defaults_and_a_local_file_overrides_single_settings()
    {
        Assert.Equal(AuthenticationPolicy.Default, AuthenticationPolicyFile.Load(Path.Combine(_folder, "missing.json")));

        var shared = Write("appsettings.json", """{ "Authentication": { "LockoutMinutes": 10, "MinimumPasswordLength": 14 } }""");
        var local = Write("appsettings.Local.json", """{ "Authentication": { "LockoutMinutes": 15 } }""");

        var policy = AuthenticationPolicyFile.Load(shared, local);

        Assert.Equal(15, policy.LockoutMinutes);
        Assert.Equal(14, policy.MinimumPasswordLength);
        Assert.Equal(600_000, policy.Pbkdf2Iterations);
    }

    [Fact]
    public void A_setting_below_its_safety_floor_stops_the_load()
    {
        var path = Write("weak.json", """{ "Authentication": { "Pbkdf2Iterations": 1000, "MaxFailedAttempts": 99 } }""");

        var exception = Assert.Throws<InvalidAuthenticationPolicyException>(() => AuthenticationPolicyFile.Load(path));

        Assert.Equal(2, exception.Problems.Count);
    }

    [Fact]
    public void A_misspelt_setting_is_an_error_and_not_silently_ignored()
    {
        var path = Write("typo.json", """{ "Authentication": { "LockoutMinuts": 1 } }""");

        Assert.ThrowsAny<Exception>(() => AuthenticationPolicyFile.Load(path));
    }

    [Fact]
    public void Comments_and_trailing_commas_in_the_settings_file_are_tolerated()
    {
        var path = Write("comments.json", """
            {
              // a comment
              "Authentication": { "LockoutMinutes": 7, },
            }
            """);

        Assert.Equal(7, AuthenticationPolicyFile.Load(path).LockoutMinutes);
    }
}
