using ERP.MES.Domain.Identity;

namespace ERP.MES.Domain.Tests.Identity;

// The identity and organisation concepts of the Domain (Architecture Specification 3.3; Domain and Database Specification 15.1, 15.3; Decision Log #61, #62, #67).
public class IdentityTests
{
    private static readonly DateTime Now = new(2026, 10, 15, 9, 0, 0, DateTimeKind.Utc);

    private static readonly Department Planning = new(3, DepartmentCodes.Planning, "برنامه‌ریزی");

    private static readonly Role Staff = new(3, RoleCodes.Staff, "کارمند");

    private static readonly Role Admin = new(1, RoleCodes.Admin, "مدیر سیستم");

    private static User NewUser(string name = "Ali.Rezaei") => new(0, Username.Create(name), "Ali Rezaei", Planning, Staff, Now);

    // ---- user names (Decision Log #62) ----

    [Fact]
    public void A_user_name_keeps_its_spelling_but_its_key_ignores_case()
    {
        var name = Username.Create("Ali.Rezaei");

        Assert.Equal("Ali.Rezaei", name.Value);
        Assert.Equal("ALI.REZAEI", name.Key);
        Assert.Equal(Username.Create("ali.rezaei"), name);
        Assert.Equal(Username.Create("ALI.REZAEI").GetHashCode(), name.GetHashCode());
        Assert.NotEqual(Username.Create("ali.rezaei2"), name);
    }

    [Theory]
    [InlineData("ali", "ALI")]
    [InlineData("Ali", "ALI")]
    [InlineData("ALI", "ALI")]
    [InlineData("ali reza", "ALI REZA")]
    [InlineData("کاربر.۱", "کاربر.۱")]
    [InlineData("i", "I")]
    public void The_key_is_the_name_in_upper_case_of_the_invariant_culture(string text, string expected)
    {
        Assert.Equal(expected, Username.Create(text).Key);
        Assert.Equal(expected, Username.KeyOfValid(text));
    }

    [Theory]
    [InlineData(null, UsernameError.Empty)]
    [InlineData("", UsernameError.Empty)]
    [InlineData(" ali", UsernameError.LeadingOrTrailingWhitespace)]
    [InlineData("ali ", UsernameError.LeadingOrTrailingWhitespace)]
    [InlineData("\tali", UsernameError.LeadingOrTrailingWhitespace)]
    [InlineData(" ", UsernameError.LeadingOrTrailingWhitespace)]
    [InlineData("al\u0007i", UsernameError.ControlCharacter)]
    [InlineData("al\ni", UsernameError.ControlCharacter)]
    public void An_invalid_name_is_rejected_and_never_trimmed(string? text, UsernameError expected)
    {
        Assert.Equal(expected, Username.Validate(text));
        Assert.False(Username.TryCreate(text, out var username, out var error));
        Assert.Null(username);
        Assert.Equal(expected, error);
        Assert.Null(Username.KeyOfValid(text));
        Assert.Throws<ArgumentException>(() => Username.Create(text));
    }

    [Fact]
    public void The_length_limit_is_64_characters()
    {
        Assert.Equal(UsernameError.None, Username.Validate(new string('a', 64)));
        Assert.Equal(UsernameError.TooLong, Username.Validate(new string('a', 65)));
    }

    // ---- departments and roles (Decision Log #30, #67) ----

    [Fact]
    public void The_reference_codes_are_the_approved_development_set()
    {
        Assert.Equal(
            new[] { "CENTRAL", "MANAGEMENT", "PLANNING", "DRAWING", "WAREHOUSE", "PRODUCTION", "QC", "MAINTENANCE_PM", "SYSTEM" },
            DepartmentCodes.All);
        Assert.Equal(new[] { "ADMIN", "MANAGER", "STAFF" }, RoleCodes.All);
    }

    [Fact]
    public void A_display_name_can_be_replaced_without_changing_the_code()
    {
        var department = new Department(3, DepartmentCodes.Planning, "Planning");

        department.Rename("برنامه‌ریزی و کنترل پروژه");

        Assert.Equal(DepartmentCodes.Planning, department.Code);
        Assert.Equal("برنامه‌ریزی و کنترل پروژه", department.Name);
        Assert.Throws<ArgumentException>(() => department.Rename(" "));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("has space")]
    [InlineData("lower")]
    public void A_department_or_role_needs_a_proper_stable_code_and_a_name(string code)
    {
        Assert.Throws<ArgumentException>(() => new Department(1, code, "Name"));
        Assert.Throws<ArgumentException>(() => new Role(1, code, "Name"));
        Assert.Throws<ArgumentException>(() => new Department(1, "OK", " "));
        Assert.Throws<ArgumentException>(() => new Role(1, "OK", ""));
    }

    // ---- users (Domain and Database Specification 15.1) ----

    [Fact]
    public void A_new_user_has_exactly_one_department_and_one_role_and_is_active()
    {
        var user = NewUser();

        Assert.Equal("Ali.Rezaei", user.Username.Value);
        Assert.Equal("ALI.REZAEI", user.UsernameKey);
        Assert.Equal(DepartmentCodes.Planning, user.Department.Code);
        Assert.Equal(3, user.DepartmentId);
        Assert.Equal(RoleCodes.Staff, user.Role.Code);
        Assert.True(user.IsActive);
        Assert.False(user.IsActiveAdministrator);
        Assert.Equal(Now, user.CreatedAtUtc);
        Assert.Null(user.LastLoginAtUtc);
    }

    [Fact]
    public void A_user_cannot_be_created_without_a_name_department_or_role_or_with_a_local_time()
    {
        Assert.Throws<ArgumentNullException>(() => new User(0, null!, "A", Planning, Staff, Now));
        Assert.Throws<ArgumentNullException>(() => new User(0, Username.Create("a"), "A", null!, Staff, Now));
        Assert.Throws<ArgumentNullException>(() => new User(0, Username.Create("a"), "A", Planning, null!, Now));
        Assert.Throws<ArgumentException>(() => new User(0, Username.Create("a"), " ", Planning, Staff, Now));
        Assert.Throws<ArgumentException>(() => new User(0, Username.Create("a"), "A", Planning, Staff, DateTime.SpecifyKind(Now, DateTimeKind.Local)));
    }

    [Fact]
    public void The_display_name_is_trimmed_but_the_login_name_never_is()
    {
        var user = new User(0, Username.Create("ali"), "  Ali Rezaei  ", Planning, Staff, Now);

        Assert.Equal("Ali Rezaei", user.DisplayName);
        Assert.Equal("ali", user.Username.Value);
    }

    [Fact]
    public void A_user_is_deactivated_not_deleted_and_changes_role_and_department()
    {
        var user = NewUser();
        var later = Now.AddHours(1);

        user.SetActive(false, later);
        user.AssignRole(Admin, later.AddMinutes(1));
        user.AssignDepartment(new Department(9, DepartmentCodes.System, "سیستم"), later.AddMinutes(2));

        Assert.False(user.IsActive);
        Assert.Equal(RoleCodes.Admin, user.Role.Code);
        Assert.Equal(1, user.RoleId);
        Assert.Equal(DepartmentCodes.System, user.Department.Code);
        Assert.Equal(9, user.DepartmentId);
        Assert.Equal(later.AddMinutes(2), user.UpdatedAtUtc);
        Assert.False(user.IsActiveAdministrator);

        user.SetActive(true, later.AddMinutes(3));
        Assert.True(user.IsActiveAdministrator);
        Assert.Throws<ArgumentNullException>(() => user.AssignRole(null!, later));
        Assert.Throws<ArgumentNullException>(() => user.AssignDepartment(null!, later));
    }

    [Fact]
    public void Times_must_be_utc_and_a_login_is_recorded()
    {
        var user = NewUser();

        user.RecordLogin(Now.AddMinutes(5));

        Assert.Equal(Now.AddMinutes(5), user.LastLoginAtUtc);
        Assert.Throws<ArgumentException>(() => user.RecordLogin(DateTime.SpecifyKind(Now, DateTimeKind.Unspecified)));
        Assert.Throws<ArgumentException>(() => user.SetActive(false, DateTime.SpecifyKind(Now, DateTimeKind.Local)));
        Assert.Throws<ArgumentException>(() => user.Rename("B", DateTime.SpecifyKind(Now, DateTimeKind.Local)));
        user.Rename("  B C ", Now.AddMinutes(6));
        Assert.Equal("B C", user.DisplayName);
    }

    [Fact]
    public void The_login_name_cannot_be_changed_after_creation()
    {
        var setter = typeof(User).GetProperty(nameof(User.Username))!.SetMethod!;
        var keySetter = typeof(User).GetProperty(nameof(User.UsernameKey))!.SetMethod!;

        Assert.False(setter.IsPublic);
        Assert.False(keySetter.IsPublic);
        Assert.DoesNotContain(typeof(User).GetMethods().Select(m => m.Name), name => name.Contains("Username", StringComparison.Ordinal) && !name.StartsWith("get_", StringComparison.Ordinal));
    }

    // ---- the boundary: identity belongs in the Domain, credentials and technology do not ----

    [Fact]
    public void The_identity_types_carry_no_credential_or_technology_member()
    {
        var identityTypes = typeof(User).Assembly.GetTypes().Where(t => t.Namespace == "ERP.MES.Domain.Identity").ToArray();
        Assert.Contains(identityTypes, t => t == typeof(User));

        var members = identityTypes.SelectMany(t => t.GetMembers(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly))
            .Select(m => m.Name)
            .Where(name => name != nameof(object.GetHashCode))
            .ToArray();

        foreach (var forbidden in new[] { "Password", "Hash", "Salt", "Token", "Credential", "Lockout", "Pbkdf", "Secret" })
        {
            Assert.DoesNotContain(members, name => name.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
        }
    }
}
