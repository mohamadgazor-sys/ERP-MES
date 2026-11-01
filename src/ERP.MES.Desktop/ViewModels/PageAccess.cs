using ERP.MES.Application.Security;

namespace ERP.MES.Desktop.ViewModels;

/// <summary>
/// Which navigation pages a user sees (Decision Log #122 B2): a page is shown when the user holds at least one of the permissions that make the page meaningful; the dashboard and the demonstration
/// limitations page are always shown. This is a convenience only. Hiding a menu entry is not a security control: every Application command still checks the user's department and role at the moment it runs.
/// </summary>
public static class PageAccess
{
    private static readonly IReadOnlyDictionary<string, Permission[]> Requirements = new Dictionary<string, Permission[]>(StringComparer.Ordinal)
    {
        [ShellViewModel.CustomersKey] = [Permission.ViewCustomersAndRequests],
        [ShellViewModel.RequestsKey] = [Permission.ViewCustomersAndRequests],
        [ShellViewModel.QueueKey] = [Permission.ViewProductionQueue],
        [ShellViewModel.UnitQueueKey] = [Permission.ViewUnitQueue],
        [ShellViewModel.WorkBoardKey] = [Permission.ViewWorkSessions, Permission.OperateWorkSessions],
        [ShellViewModel.QcInspectionKey] = [Permission.ViewQcInspections, Permission.InspectOrCorrectQc],
        [ShellViewModel.JobReportsKey] = [Permission.ViewJobReports, Permission.SubmitJobReport],
        [ShellViewModel.OvertimeKey] = [Permission.ViewOvertime],
        [ShellViewModel.CostingKey] = [Permission.ViewItemCosts, Permission.CalculateItemCost, Permission.ApproveItemPrice],
        [ShellViewModel.CompletionKey] = [Permission.ViewWarehouseCompletion, Permission.RecordWarehouseReceipt],
        [ShellViewModel.KpiKey] = [Permission.ViewKpi],
        [ShellViewModel.CalendarKey] = [Permission.ViewCalendar, Permission.MaintainHolidayCalendar],
        [ShellViewModel.DefinitionsKey] = [Permission.MaintainUnitsOfMeasure, Permission.DefineMakeItems, Permission.DefineRepairItemsAndEditAnyItem],
        [ShellViewModel.MachinesKey] = [Permission.MaintainProductionResources, Permission.ChangeMachineHourlyRates, Permission.ViewMachineRates],
        [ShellViewModel.OperatorsKey] = [Permission.ViewOperators, Permission.MaintainProductionResources],
        [ShellViewModel.MaterialsKey] = [Permission.MaintainMaterials, Permission.ViewMaterialPrices, Permission.ChangeMaterialPricePerKg],
        [ShellViewModel.ContractsKey] = [Permission.ViewContracts, Permission.CreateOrSubmitContract, Permission.ApproveOrRejectContract],
        [ShellViewModel.InquiriesKey] = [Permission.ViewRawMaterialInquiries, Permission.ManageRawMaterialInquiries, Permission.AnswerRawMaterialInquiry],
        [ShellViewModel.ClarificationsKey] = [Permission.ViewDrawingClarifications, Permission.AskDrawingClarification, Permission.AnswerDrawingClarification],
        [ShellViewModel.ItemDefinitionKey] = [Permission.DefineMakeItems, Permission.DefineRepairItemsAndEditAnyItem, Permission.ViewProductionGates],
        [ShellViewModel.ItemImportKey] = [Permission.ImportItemsFromExcel, Permission.ImportTechnicalItems],
        [ShellViewModel.ItemDrawingsKey] = [Permission.ViewItemDrawings],
        [ShellViewModel.StageTitlesKey] = [Permission.MaintainStageTitles, Permission.DefineMakeItems, Permission.DefineRepairItemsAndEditAnyItem],
        [ShellViewModel.UsersKey] = [Permission.AdministerUsers],
    };

    /// <summary>The pages that need no permission: the dashboard and the limitations page.</summary>
    private static readonly string[] AlwaysVisible = [ShellViewModel.DashboardKey, ShellViewModel.LimitationsKey];

    /// <summary>The page keys that have a permission requirement (for tests and documentation).</summary>
    public static IReadOnlyCollection<string> GuardedKeys => Requirements.Keys.ToList();

    /// <summary>The permissions any of which makes the page visible; empty for an always-visible page.</summary>
    public static IReadOnlyList<Permission> PermissionsFor(string key) => Requirements.TryGetValue(key, out var permissions) ? permissions : [];

    /// <summary>The keys the user may see, from one fresh reload of the user (so a changed department or role takes effect at the next refresh).</summary>
    public static async Task<IReadOnlySet<string>> VisibleKeysAsync(CurrentAuthorization authorization, Principal? principal, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        var visible = new HashSet<string>(AlwaysVisible, StringComparer.Ordinal);
        var set = await authorization.EvaluateAsync(principal, Requirements.Values.SelectMany(p => p).Distinct(), cancellationToken);
        foreach (var (key, permissions) in Requirements)
        {
            if (permissions.Any(set.Has))
            {
                visible.Add(key);
            }
        }

        return visible;
    }
}
