namespace ERP.MES.Application.Shell;

/// <summary>One entry of the shell's navigation list. <see cref="IsImplemented"/> is false for pages that are only labelled placeholders.</summary>
public sealed record NavigationItem(string Key, string Title, bool IsImplemented);
