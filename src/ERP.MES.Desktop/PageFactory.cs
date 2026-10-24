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
            ShellViewModel.InquiriesKey => new InquiriesView(pages.Inquiries),
            ShellViewModel.ClarificationsKey => new ClarificationsView(pages.Clarifications),
            ShellViewModel.ItemDefinitionKey => new ItemDefinitionView(pages.ItemDefinition),
            ShellViewModel.ItemImportKey => new ItemImportView(pages.ItemImport),
            ShellViewModel.UnitQueueKey => new UnitQueueView(pages.UnitQueue),
            ShellViewModel.CalendarKey => new CalendarView(pages.Calendar),
            ShellViewModel.QcInspectionKey => new QcInspectionView(pages.QcInspection),
            ShellViewModel.JobReportsKey => new JobReportsView(pages.JobReports),
            ShellViewModel.OvertimeKey => new OvertimeView(pages.Overtime),
            ShellViewModel.CostingKey => new CostingView(pages.Costing),
            ShellViewModel.CompletionKey => new CompletionView(pages.Completion),
            ShellViewModel.LimitationsKey => new LimitationsView(),
            _ => new PlaceholderView(item.Title, shell.NotImplementedText),
        };
    }
}
