namespace ERP.MES.Desktop.ViewModels;

/// <summary>The one place where the official application name is written (owner decision of 2026-11-13). Internal namespaces, project names and database identifiers keep their technical names.</summary>
public static class AppInfo
{
    /// <summary>The official Persian name shown in window titles, the sign-in window, dialogs and about information.</summary>
    public const string Name = "برنامه جامع مدیریت و کنترل تولید ایران‌تول";

    /// <summary>The label that marks the demonstration build.</summary>
    public const string DemoSuffix = "نسخه نمایشی";

    public const string DemoTitle = Name + " — " + DemoSuffix;
}
