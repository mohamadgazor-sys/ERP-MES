namespace ERP.MES.Domain.Identity;

public enum UsernameError
{
    None = 0,
    Empty,
    TooLong,
    LeadingOrTrailingWhitespace,
    ControlCharacter,
}

/// <summary>
/// A login name (Decision Log #62). It is stored as given, so the person's spelling is kept, but it is compared and made unique
/// by its <see cref="Key"/>: the same name in upper case (invariant culture), so "Ali" and "ali" are the same name. Leading or trailing white space is
/// rejected rather than silently trimmed, and so is any control character. The application and the database use this one key; nothing else
/// (no database collation) decides whether two names are the same.
/// </summary>
public sealed class Username : IEquatable<Username>
{
    public const int MaximumLength = 64;

    private Username(string value)
    {
        Value = value;
        Key = KeyOf(value);
    }

    /// <summary>The name as it was given.</summary>
    public string Value { get; }

    /// <summary>The canonical form used for lookup and uniqueness.</summary>
    public string Key { get; }

    /// <summary>Checks a text without changing it. <see cref="UsernameError.None"/> means it can be used as given.</summary>
    public static UsernameError Validate(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return UsernameError.Empty;
        }

        if (text.Length > MaximumLength)
        {
            return UsernameError.TooLong;
        }

        if (char.IsWhiteSpace(text[0]) || char.IsWhiteSpace(text[^1]))
        {
            return UsernameError.LeadingOrTrailingWhitespace;
        }

        return text.Any(char.IsControl) ? UsernameError.ControlCharacter : UsernameError.None;
    }

    public static bool TryCreate(string? text, out Username? username, out UsernameError error)
    {
        error = Validate(text);
        username = error == UsernameError.None ? new Username(text!) : null;
        return error == UsernameError.None;
    }

    public static Username Create(string? text) =>
        TryCreate(text, out var username, out var error)
            ? username!
            : throw new ArgumentException("Invalid user name: " + error + ".", nameof(text));

    /// <summary>The canonical key of a typed text, or null when the text is not a valid name (so no user can have it).</summary>
    public static string? KeyOfValid(string? text) => Validate(text) == UsernameError.None ? KeyOf(text!) : null;

    private static string KeyOf(string value) => value.ToUpperInvariant();

    public bool Equals(Username? other) => other is not null && string.Equals(Key, other.Key, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as Username);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Key);

    public override string ToString() => Value;
}
