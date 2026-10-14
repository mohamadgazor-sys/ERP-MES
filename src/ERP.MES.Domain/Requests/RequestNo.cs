namespace ERP.MES.Domain.Requests;

public enum RequestNoError
{
    None = 0,
    Empty,
    LeadingOrTrailingWhitespace,
}

/// <summary>
/// The customer-supplied request number (Domain and Database Specification section 4.2, Decision Log #31): free-form
/// text stored and compared exactly as received. It is never trimmed, re-cased or normalised; leading or trailing
/// white space is rejected instead. Equality is case-sensitive (ordinal).
/// </summary>
public sealed class RequestNo : IEquatable<RequestNo>
{
    private RequestNo(string value) => Value = value;

    public string Value { get; }

    /// <summary>Checks a text without changing it. <see cref="RequestNoError.None"/> means it can be used as is.</summary>
    public static RequestNoError Validate(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return RequestNoError.Empty;
        }

        return char.IsWhiteSpace(text[0]) || char.IsWhiteSpace(text[^1])
            ? RequestNoError.LeadingOrTrailingWhitespace
            : RequestNoError.None;
    }

    public static bool TryCreate(string? text, out RequestNo? requestNo, out RequestNoError error)
    {
        error = Validate(text);
        requestNo = error == RequestNoError.None ? new RequestNo(text!) : null;
        return error == RequestNoError.None;
    }

    public static RequestNo Create(string? text) =>
        TryCreate(text, out var requestNo, out var error)
            ? requestNo!
            : throw new ArgumentException("Invalid request number: " + error + ".", nameof(text));

    /// <summary>
    /// True when the two numbers differ only by letter case. Decision Log #31 asks for a non-blocking warning in
    /// that case for the same customer and Jalali year; it never blocks, because the numbers are different values.
    /// </summary>
    public static bool IsCaseInsensitiveNearDuplicate(RequestNo a, RequestNo b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        return !string.Equals(a.Value, b.Value, StringComparison.Ordinal)
               && string.Equals(a.Value, b.Value, StringComparison.OrdinalIgnoreCase);
    }

    public bool Equals(RequestNo? other) => other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as RequestNo);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString() => Value;
}

/// <summary>
/// The business key of a request: Customer + RequestNo + Jalali Request Year (section 4.2). Equality is exact, so
/// "wsj-898" and "WSJ-898" are different keys.
/// </summary>
public readonly record struct RequestKey(long CustomerId, string RequestNo, int JalaliRequestYear);
