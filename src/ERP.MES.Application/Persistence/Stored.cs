namespace ERP.MES.Application.Persistence;

/// <summary>
/// A loaded record together with the row version it was loaded at (Decision Log #23). Pass the version back as the expected version
/// of an update to detect that someone else changed the record in the meantime. The Domain types do not carry the version.
/// </summary>
public sealed record Stored<T>(T Value, long Version)
    where T : class;
