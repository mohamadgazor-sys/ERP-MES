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

        Assert.Equal(6, policy.MinimumPasswordLength);
        Assert.Equal(5, policy.MaxFailedAttempts);
        Assert.Equal(5, policy.LockoutMinutes);
        Assert.Equal(600_000, policy.Pbkdf2Iterations);
        Assert.Equal(24, policy.BootstrapTokenLifetimeHours);
        Assert.Equal(5, policy.BootstrapMaxAttempts);
        Assert.Equal(5, policy.RecoveryMaxAttempts);
        Assert.Empty(policy.Validate());
    }

    [Theory]
    [InlineData("MinimumPasswordLength", 5)]
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
            MinimumPasswordLength = 6,
            MaxFailedAttempts = 3,
            LockoutMinutes = 1,
            Pbkdf2Iterations = 310_000,
        };

        Assert.Empty(policy.Validate());
    }

    [Theory]
    [InlineData("abcdef", PasswordPolicyError.None)]
    [InlineData("abcde", PasswordPolicyError.TooShort)]
    [InlineData("a", PasswordPolicyError.TooShort)]
    [InlineData("", PasswordPolicyError.Empty)]
    [InlineData(null, PasswordPolicyError.Empty)]
    public void The_minimum_is_six_characters(string? password, PasswordPolicyError expected)
    {
        Assert.Equal(expected, PasswordRules.Validate(password, AuthenticationPolicy.Default));
    }

    [Theory]
    [InlineData("alllowercase")]
    [InlineData("ALLUPPERCASE")]
    [InlineData("123456789012")]
    [InlineData("!@#$%^&*()_+")]
    [InlineData("a-much-longer-password-with-no-required-mix")]
    [InlineData("گذرواژه‌ام")]
    public void There_are_no_composition_rules(string password)
    {
        Assert.Equal(PasswordPolicyError.None, PasswordRules.Validate(password, AuthenticationPolicy.Default));
    }

    [Theory]
    [InlineData(" abcdef")]
    [InlineData("abcdef ")]
    [InlineData("abc def")]
    [InlineData("  abcdef  ")]
    [InlineData("abcdefghijklmnop qrstuvwxyz")]
    [InlineData("      ")]
    [InlineData(" ab ")]
    public void A_space_anywhere_is_rejected_and_the_password_is_not_trimmed_to_fit(string password)
    {
        // Rejected - never repaired: the trimmed text of the first five would be a valid six-character password.
        Assert.Equal(PasswordPolicyError.ContainsWhitespace, PasswordRules.Validate(password, AuthenticationPolicy.Default));
    }

    [Theory]
    [InlineData("\u0020")]   // space
    [InlineData("\u00A0")]   // no-break space
    [InlineData("\u3000")]   // ideographic space
    [InlineData("\u2003")]   // em space
    [InlineData("\u202F")]   // narrow no-break space
    [InlineData("\t")]       // tab
    [InlineData("\n")]       // line feed
    [InlineData("\r")]       // carriage return
    [InlineData("\u000B")]   // vertical tab
    [InlineData("\u000C")]   // form feed
    [InlineData("\u0085")]   // next line
    [InlineData("\u2028")]   // line separator
    [InlineData("\u2029")]   // paragraph separator
    public void Every_kind_of_white_space_is_rejected_at_the_start_in_the_middle_and_at_the_end(string whiteSpace)
    {
        var policy = AuthenticationPolicy.Default;

        Assert.Equal(PasswordPolicyError.ContainsWhitespace, PasswordRules.Validate(whiteSpace + "sixsix", policy));
        Assert.Equal(PasswordPolicyError.ContainsWhitespace, PasswordRules.Validate("six" + whiteSpace + "six", policy));
        Assert.Equal(PasswordPolicyError.ContainsWhitespace, PasswordRules.Validate("sixsix" + whiteSpace, policy));
        Assert.Equal(PasswordPolicyError.ContainsWhitespace, PasswordRules.Validate(whiteSpace + "a-much-longer-password" + whiteSpace, policy));
    }

    [Fact]
    public void The_zero_width_non_joiner_and_non_white_space_characters_are_allowed_and_nothing_is_normalised()
    {
        var policy = AuthenticationPolicy.Default;

        // U+200C (Persian) is not white space. So are the other invisible format characters this decision does not name.
        Assert.Equal(PasswordPolicyError.None, PasswordRules.Validate("می\u200Cخواهم", policy));
        Assert.Equal(PasswordPolicyError.None, PasswordRules.Validate("ab\u200Ccd\u200Ce", policy));
        Assert.Equal(PasswordPolicyError.None, PasswordRules.Validate("\u200Cabcde", policy));
        Assert.False(char.IsWhiteSpace('\u200C'));

        // Nothing is normalised: the decomposed and the precomposed forms are both accepted exactly as typed (and counted as typed).
        Assert.Equal(PasswordPolicyError.None, PasswordRules.Validate("e\u0301e\u0301e\u0301e\u0301e\u0301e\u0301", policy));
        Assert.Equal(PasswordPolicyError.None, PasswordRules.Validate("\u00e9\u00e9\u00e9\u00e9\u00e9\u00e9", policy));
    }

    [Fact]
    public void White_space_is_reported_before_length_and_a_short_text_with_white_space_is_not_repaired_by_trimming()
    {
        // " abcde" would be a valid six characters only if the white space were kept, and "abcde" only if it were trimmed: it is rejected for the white space.
        Assert.Equal(PasswordPolicyError.ContainsWhitespace, PasswordRules.Validate(" abcde", AuthenticationPolicy.Default));
        Assert.Equal(PasswordPolicyError.ContainsWhitespace, PasswordRules.Validate("abcdef\n", AuthenticationPolicy.Default));
        Assert.Equal(PasswordPolicyError.ContainsWhitespace, PasswordRules.Validate("\t\t\t\t\t\t", AuthenticationPolicy.Default));
    }

    [Fact]
    public void Persian_text_counts_by_characters_not_by_bytes()
    {
        // Five Persian letters are ten bytes in UTF-8 but only five characters; six letters are enough.
        Assert.Equal(PasswordPolicyError.TooShort, PasswordRules.Validate("گذرها", AuthenticationPolicy.Default));
        Assert.Equal(PasswordPolicyError.None, PasswordRules.Validate("گذرواژ", AuthenticationPolicy.Default));
    }

    [Fact]
    public void A_configured_longer_minimum_still_applies_but_the_white_space_rule_cannot_be_configured_away()
    {
        var policy = new AuthenticationPolicy { MinimumPasswordLength = 10 };

        Assert.Equal(PasswordPolicyError.TooShort, PasswordRules.Validate("abcdefghi", policy));
        Assert.Equal(PasswordPolicyError.None, PasswordRules.Validate("abcdefghij", policy));
        Assert.Equal(PasswordPolicyError.ContainsWhitespace, PasswordRules.Validate("abcdefghi j", policy));
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
