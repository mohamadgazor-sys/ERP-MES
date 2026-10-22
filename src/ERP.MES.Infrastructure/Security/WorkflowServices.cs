using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Calendar;
using ERP.MES.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.MES.Infrastructure.Security;

/// <summary>The customer and request use cases wired to the real repositories, the current-authorization check and the clock. Composition roots (Desktop) and tests create it once per database.</summary>
public sealed record WorkflowServices(CustomerService Customers, RequestService Requests, ProductionQueueService Queue)
{
    public static WorkflowServices Create(
        IDbContextFactory<ErpMesDbContext> contexts, CurrentAuthorization authorization, IJalaliDateService dates, IClock? clock = null)
    {
        ArgumentNullException.ThrowIfNull(contexts);
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(dates);

        var effectiveClock = clock ?? new SystemClock();
        var customers = new EfCustomerRepository(contexts);
        var requests = new EfRequestRepository(contexts);
        return new WorkflowServices(
            new CustomerService(customers, authorization, effectiveClock),
            new RequestService(requests, customers, authorization, effectiveClock, dates),
            new ProductionQueueService(requests, authorization));
    }
}
