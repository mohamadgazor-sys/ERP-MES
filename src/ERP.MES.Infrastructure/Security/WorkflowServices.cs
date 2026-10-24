using ERP.MES.Application.Imports;
using ERP.MES.Application.Costing;
using ERP.MES.Application.Persistence;
using ERP.MES.Application.Planning;
using ERP.MES.Application.Production;
using ERP.MES.Application.Reporting;
using ERP.MES.Application.Scheduling;
using ERP.MES.Application.Quality;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Calendar;
using ERP.MES.Infrastructure.Imports;
using ERP.MES.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.MES.Infrastructure.Security;

/// <summary>The customer and request use cases wired to the real repositories, the current-authorization check and the clock. Composition roots (Desktop) and tests create it once per database.</summary>
public sealed record WorkflowServices(
    CustomerService Customers, RequestService Requests, ProductionQueueService Queue, StageWorkService StageWork, RequestDocumentService Documents, RequestWorkflowService Lifecycle, QcMasterService QcMaster, PlanningMasterService PlanningMaster, OperatorService Operators, MaterialService Materials, ContractService Contracts, InquiryService Inquiries, ClarificationService Clarifications, RequestGateService Gates, ItemDefinitionService ItemDefinition, ItemImportService ItemImport, UnitQueueService UnitQueue, UnitDesignationService UnitDesignations, IUnitQueueRepository UnitQueueStore, CalendarService Calendar, WorkerScheduleService WorkerSchedule, QcInspectionService QcInspection, JobReportService JobReports, OvertimeService Overtime, CostingService Costing, WarehouseCompletionService Completion)
{
    public static WorkflowServices Create(
        IDbContextFactory<ErpMesDbContext> contexts, CurrentAuthorization authorization, IJalaliDateService dates, IClock? clock = null, IAuditContext? auditContext = null, DocumentPolicy? documentPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(contexts);
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(dates);

        var effectiveClock = clock ?? new SystemClock();
        var customers = new EfCustomerRepository(contexts);
        var requests = new EfRequestRepository(contexts);
        var gates = new RequestGateService(new EfContractRepository(contexts), new EfInquiryRepository(contexts), new EfClarificationRepository(contexts), authorization);
        var unitQueue = new EfUnitQueueRepository(contexts);
        var scheduling = new EfSchedulingRepository(contexts);
        return new WorkflowServices(
            new CustomerService(customers, authorization, effectiveClock),
            new RequestService(requests, customers, authorization, effectiveClock, dates, auditContext ?? new EnvironmentAuditContext()),
            new ProductionQueueService(requests, authorization),
            new StageWorkService(requests, authorization, effectiveClock, auditContext ?? new EnvironmentAuditContext(), gates, unitQueue),
            new RequestDocumentService(requests, authorization, effectiveClock, auditContext ?? new EnvironmentAuditContext(), documentPolicy ?? DocumentPolicy.Default),
            new RequestWorkflowService(requests, authorization, effectiveClock, auditContext ?? new EnvironmentAuditContext()),
            new QcMasterService(new EfQcMasterRepository(contexts), authorization, effectiveClock),
            new PlanningMasterService(new EfPlanningMasterRepository(contexts), authorization, effectiveClock, dates, auditContext ?? new EnvironmentAuditContext()),
            new OperatorService(new EfOperatorRepository(contexts), new EfPlanningMasterRepository(contexts), authorization, effectiveClock, dates, auditContext ?? new EnvironmentAuditContext()),
            new MaterialService(new EfMaterialRepository(contexts), authorization, effectiveClock, dates, auditContext ?? new EnvironmentAuditContext()),
            new ContractService(new EfContractRepository(contexts), authorization, effectiveClock, dates, auditContext ?? new EnvironmentAuditContext(), documentPolicy ?? DocumentPolicy.Default),
            new InquiryService(new EfInquiryRepository(contexts), new EfQcMasterRepository(contexts), authorization, effectiveClock, auditContext ?? new EnvironmentAuditContext()),
            new ClarificationService(new EfClarificationRepository(contexts), authorization, effectiveClock, auditContext ?? new EnvironmentAuditContext()),
            gates,
            new ItemDefinitionService(requests, new EfQcMasterRepository(contexts), new EfMaterialRepository(contexts), new EfPlanningMasterRepository(contexts), gates, authorization, effectiveClock, auditContext ?? new EnvironmentAuditContext()),
            new ItemImportService(requests, new EfItemImportRepository(contexts), new EfQcMasterRepository(contexts), new EfMaterialRepository(contexts), new EfPlanningMasterRepository(contexts), new XlsxWorkbookReader(), gates, authorization, effectiveClock, auditContext ?? new EnvironmentAuditContext()),
            new UnitQueueService(requests, unitQueue, new EfPlanningMasterRepository(contexts), gates, scheduling, dates, authorization, effectiveClock, auditContext ?? new EnvironmentAuditContext()),
            new UnitDesignationService(unitQueue, new EfPlanningMasterRepository(contexts), authorization, effectiveClock, auditContext ?? new EnvironmentAuditContext()),
            unitQueue,
            new CalendarService(scheduling, new EfPlanningMasterRepository(contexts), authorization, effectiveClock, auditContext ?? new EnvironmentAuditContext()),
            new WorkerScheduleService(scheduling, new EfOperatorRepository(contexts), authorization, effectiveClock, auditContext ?? new EnvironmentAuditContext()),
            new QcInspectionService(requests, new EfQcInspectionRepository(contexts), authorization, effectiveClock, auditContext ?? new EnvironmentAuditContext()),
            new JobReportService(new EfJobReportRepository(contexts), new EfOperatorRepository(contexts), new EfPlanningMasterRepository(contexts), scheduling, unitQueue, requests, dates, authorization, effectiveClock, auditContext ?? new EnvironmentAuditContext()),
            new OvertimeService(new EfOvertimeRepository(contexts), new EfOperatorRepository(contexts), new EfPlanningMasterRepository(contexts), unitQueue, authorization, effectiveClock, auditContext ?? new EnvironmentAuditContext()),
            new CostingService(new EfCostingRepository(contexts), dates, authorization, effectiveClock, auditContext ?? new EnvironmentAuditContext()),
            new WarehouseCompletionService(new EfWarehouseCompletionRepository(contexts), dates, authorization, effectiveClock, auditContext ?? new EnvironmentAuditContext()));
    }
}
