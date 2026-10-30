using ERP.MES.Domain.Identity;

namespace ERP.MES.Infrastructure.Persistence;

/// <summary>
/// The development seed of departments and roles (Decision Log #30), written by the migration. The Persian display names are development placeholders that
/// may be replaced without a schema change; code and authorization use the stable codes (Decision Log #67).
/// </summary>
public static class ReferenceDataSeed
{
    public static IReadOnlyList<Department> Departments { get; } =
    [
        new(1, DepartmentCodes.Central, "منشی"),
        new(2, DepartmentCodes.Management, "مدیریت"),
        new(3, DepartmentCodes.Planning, "برنامه‌ریزی"),
        new(4, DepartmentCodes.Drawing, "نقشه‌کشی"),
        new(5, DepartmentCodes.Warehouse, "انبار"),
        new(6, DepartmentCodes.Production, "تولید"),
        new(7, DepartmentCodes.Qc, "کنترل کیفیت"),
        new(8, DepartmentCodes.MaintenancePm, "نگهداری و تعمیرات"),
        new(9, DepartmentCodes.System, "سیستم"),
        new(10, DepartmentCodes.ProductionManager, "مدیر تولید"),
    ];

    public static IReadOnlyList<Role> Roles { get; } =
    [
        new(1, RoleCodes.Admin, "مدیر سیستم"),
        new(2, RoleCodes.Manager, "مدیر"),
        new(3, RoleCodes.Staff, "کارمند"),
    ];
}
