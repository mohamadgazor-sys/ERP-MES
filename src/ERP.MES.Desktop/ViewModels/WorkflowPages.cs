namespace ERP.MES.Desktop.ViewModels;

/// <summary>The view models of the functional pages of one signed-in session. They are created when the user signs in and dropped with the session, so nothing of one user's screens is shown to the next.</summary>
public sealed record WorkflowPages(CustomersViewModel Customers, RequestsViewModel Requests, ProductionQueueViewModel Queue, StageWorkViewModel Stages, QcMasterViewModel QcMaster, PlanningMasterViewModel PlanningMaster, OperatorsViewModel Operators, MaterialsViewModel Materials, ContractsViewModel Contracts, InquiriesViewModel Inquiries, ClarificationsViewModel Clarifications, ItemDefinitionViewModel ItemDefinition, ItemImportViewModel ItemImport, UnitQueueViewModel UnitQueue, CalendarViewModel Calendar);
