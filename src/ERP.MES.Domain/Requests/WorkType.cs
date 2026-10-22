namespace ERP.MES.Domain.Requests;

/// <summary>The work type of a request, MAKE or REPAIR (Decision Log #84). A request has exactly one; its items inherit it. (Superseded: Domain and Database Specification 2.3 and Decision Log #2 had the type per item.)</summary>
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
