namespace ERP.MES.Desktop.ViewModels;

/// <summary>A dashboard figure. <see cref="ValueText"/> is a dash until real data exists; no number is invented.</summary>
public sealed record DashboardTile(string Title, string ValueText, string Note);
