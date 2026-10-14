using System.Windows;
using ERP.MES.Desktop.ViewModels;
using ERP.MES.Desktop.Views;

namespace ERP.MES.Desktop;

/// <summary>Chooses the view for the selected navigation item. Pages that are not built yet get a clearly labelled placeholder.</summary>
internal static class PageFactory
{
    public static FrameworkElement Create(ShellViewModel shell)
    {
        var item = shell.SelectedItem;
        return item.Key switch
        {
            ShellViewModel.DashboardKey => new DashboardView { DataContext = shell },
            ShellViewModel.LimitationsKey => new LimitationsView(),
            _ => new PlaceholderView(item.Title, shell.NotImplementedText),
        };
    }
}
