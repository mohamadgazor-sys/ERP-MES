namespace ERP.MES.Application.Security;

/// <summary>
/// The authentication settings (Decision Log #40, #41, #39; Milestone 1 plan section 6.1). Defaults are the approved values; every value is configurable but
/// a value below its documented safety floor is refused by <see cref="Validate"/> (the application does not start with one).
/// </summary>
public sealed record AuthenticationPolicy
{
    /// <summary>The approved defaults.</summary>
    public static AuthenticationPolicy Default { get; } = new();

    public const int MinimumLengthFloor = 8;
    public const int MinimumAttemptsFloor = 3;
    public const int MaximumAttemptsCeiling = 10;
    public const int LockoutMinutesFloor = 1;
    public const int Pbkdf2IterationsFloor = 310_000;
    public const int MaximumTokenLifetimeHours = 24 * 30;

    /// <summary>Counted in characters; no composition rules.</summary>
    public int MinimumPasswordLength { get; init; } = 12;

    public int MaxFailedAttempts { get; init; } = 5;

    public int LockoutMinutes { get; init; } = 5;

    /// <summary>The work factor used for new hashes. Older hashes are upgraded after a successful verification; the baseline is never lowered automatically (Decision Log #41, #42).</summary>
    public int Pbkdf2Iterations { get; init; } = 600_000;

    /// <summary>Default lifetime of a bootstrap token; the operator may choose another at issue time.</summary>
    public int BootstrapTokenLifetimeHours { get; init; } = 24;

    public int BootstrapMaxAttempts { get; init; } = 5;

    public int RecoveryTokenLifetimeHours { get; init; } = 24;

    public int RecoveryMaxAttempts { get; init; } = 5;

    /// <summary>Problems with the values; empty when the policy may be used.</summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();
        if (MinimumPasswordLength < MinimumLengthFloor)
        {
            problems.Add($"MinimumPasswordLength must be at least {MinimumLengthFloor}.");
        }

        CheckAttempts(MaxFailedAttempts, nameof(MaxFailedAttempts));
        CheckAttempts(BootstrapMaxAttempts, nameof(BootstrapMaxAttempts));
        CheckAttempts(RecoveryMaxAttempts, nameof(RecoveryMaxAttempts));

        if (LockoutMinutes < LockoutMinutesFloor)
        {
            problems.Add($"LockoutMinutes must be at least {LockoutMinutesFloor}.");
        }

        if (Pbkdf2Iterations < Pbkdf2IterationsFloor)
        {
            problems.Add($"Pbkdf2Iterations must be at least {Pbkdf2IterationsFloor}.");
        }

        CheckLifetime(BootstrapTokenLifetimeHours, nameof(BootstrapTokenLifetimeHours));
        CheckLifetime(RecoveryTokenLifetimeHours, nameof(RecoveryTokenLifetimeHours));
        return problems;

        void CheckAttempts(int value, string name)
        {
            if (value < MinimumAttemptsFloor || value > MaximumAttemptsCeiling)
            {
                problems.Add($"{name} must be between {MinimumAttemptsFloor} and {MaximumAttemptsCeiling}.");
            }
        }

        void CheckLifetime(int value, string name)
        {
            if (value < 1 || value > MaximumTokenLifetimeHours)
            {
                problems.Add($"{name} must be between 1 and {MaximumTokenLifetimeHours} hours.");
            }
        }
    }
}

/// <summary>Thrown when the configured policy is below a safety floor.</summary>
public sealed class InvalidAuthenticationPolicyException(IReadOnlyList<string> problems)
    : InvalidOperationException("The authentication policy is invalid: " + string.Join(" ", problems))
{
    public IReadOnlyList<string> Problems { get; } = problems;
}
