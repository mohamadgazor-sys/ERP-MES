namespace ERP.MES.Application.Security;

/// <summary>
/// The development seed of departments and roles (Decision Log #30). The Persian display names are development placeholders; final company names may
/// replace them without a schema change, because code refers to the stable codes.
/// </summary>
public static class ReferenceData
{
    public static IReadOnlyList<Department> Departments { get; } =
    [
        new(1, DepartmentCodes.Central, "مرکزی"),
        new(2, DepartmentCodes.Management, "مدیریت"),
        new(3, DepartmentCodes.Planning, "برنامه‌ریزی"),
        new(4, DepartmentCodes.Drawing, "نقشه‌کشی"),
        new(5, DepartmentCodes.Warehouse, "انبار"),
        new(6, DepartmentCodes.Production, "تولید"),
        new(7, DepartmentCodes.Qc, "کنترل کیفیت"),
        new(8, DepartmentCodes.MaintenancePm, "نگهداری و تعمیرات"),
        new(9, DepartmentCodes.System, "سیستم"),
    ];

    public static IReadOnlyList<Role> Roles { get; } =
    [
        new(1, RoleCodes.Admin, "مدیر سیستم"),
        new(2, RoleCodes.Manager, "مدیر"),
        new(3, RoleCodes.Staff, "کارمند"),
    ];
}
