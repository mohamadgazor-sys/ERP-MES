using ERP.MES.Application.Security;
using ERP.MES.Domain.Imports;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Application.Persistence;

/// <summary>
/// Commits an item import (Decision Log #16, #97): every created and revised item, the import log with its per-row outcomes and the audit entries are saved in ONE transaction under the
/// request\'s expected row version. Any failure (a stale version, a rule of the Domain, the database) saves nothing.
/// </summary>
public interface IItemImportRepository
{
    Task<Stored<Request>> CommitAsync(
        long requestId,
        long expectedRequestVersion,
        Action<Request> apply,
        Func<Request, ItemImport> buildLog,
        Func<Request, IReadOnlyList<AuditLogEntry>> audit,
        CancellationToken cancellationToken = default);
}
