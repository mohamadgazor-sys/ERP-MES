using ERP.MES.Application.Security;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Application.Persistence;

/// <summary>What a QC command appends beside the changed request: the inspection lines and/or a shortfall decision (both append-only).</summary>
public sealed record QcAppended(IReadOnlyList<QcInspectionLine> Lines, ShortfallDecision? Decision);

/// <summary>Persistence of QC quantity decisions (Decision Log #101): the stage change, the append-only lines or decision and the audit entries are one transaction under the named row versions.</summary>
public interface IQcInspectionRepository
{
    Task<VersionedRequest> CommitAsync(
        long requestId, RowVersionExpectations expected, Func<Request, QcAppended> change, Func<Request, IReadOnlyList<AuditLogEntry>> audit, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<QcInspectionLine>> ListLinesAsync(long requestItemId, int stageNo, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ShortfallDecision>> ListDecisionsAsync(long requestItemId, int stageNo, CancellationToken cancellationToken = default);
}
