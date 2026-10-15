namespace ERP.MES.Domain.Requests;

/// <summary>Work type of one request item (Domain and Database Specification section 2.3). One request may contain both.</summary>
public enum WorkType
{
    Make = 1,
    Repair = 2,
}

public static class WorkTypeInfo
{
    public static string Code(this WorkType workType) => workType switch
    {
        WorkType.Make => "MAKE",
        WorkType.Repair => "REPAIR",
        _ => throw new ArgumentOutOfRangeException(nameof(workType), workType, "Unknown work type."),
    };
}
