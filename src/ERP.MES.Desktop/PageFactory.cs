using System.Windows;
using ERP.MES.Desktop.ViewModels;
using ERP.MES.Desktop.Views;

namespace ERP.MES.Desktop;

/// <summary>Chooses the view for the selected navigation item. Pages that are not built yet get a clearly labelled placeholder.</summary>
internal static class PageFactory
{
    public static FrameworkElement Create(ShellViewModel shell, WorkflowPages pages)
    {
        var item = shell.SelectedItem;
        return item.Key switch
        {
            ShellViewModel.DashboardKey => new DashboardView { DataContext = shell },
            ShellViewModel.CustomersKey => new CustomersView(pages.Customers),
            ShellViewModel.RequestsKey => new RequestsView(pages.Requests),
            ShellViewModel.QueueKey => new ProductionQueueView(pages.Queue),
            ShellViewModel.StagesKey => new StageWorkView(pages.Stages),
            ShellViewModel.DefinitionsKey => new QcMasterView(pages.QcMaster),
            ShellViewModel.MachinesKey => new PlanningMasterView(pages.PlanningMaster),
            ShellViewModel.OperatorsKey => new OperatorsView(pages.Operators),
            ShellViewModel.MaterialsKey => new MaterialsView(pages.Materials),
            ShellViewModel.ContractsKey => new ContractsView(pages.Contracts),
            ShellViewModel.LimitationsKey => new LimitationsView(),
            _ => new PlaceholderView(item.Title, shell.NotImplementedText),
        };
    }
}
