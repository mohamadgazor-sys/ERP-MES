using ERP.MES.Domain.Documents;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Application.Persistence;

/// <summary>
/// Saves and loads a request together with its items, stages and QC approvals (the request is the aggregate root). Every method is one
/// transaction; loaded objects are detached and changed through <see cref="UpdateAsync"/>. No database type appears in this contract.
/// </summary>
public interface IRequestRepository
{
    /// <summary>
    /// Saves a new request (id 0) with whatever items, stages and approvals it already holds. Fails with <see cref="RecordNotFoundException"/> if the
    /// customer does not exist and with <see cref="DuplicateRequestException"/> if the business key is taken (exact, case-sensitive comparison).
    /// </summary>
    Task<Stored<Request>> AddAsync(Request request, CancellationToken cancellationToken = default);

    /// <summary>Loads a request with its items, stages and approvals, or null.</summary>
    Task<Stored<Request>?> FindAsync(long requestId, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(RequestKey key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Request numbers of the same customer and Jalali year that differ from <paramref name="requestNo"/> only by letter case. Decision Log #31 asks for a
    /// non-blocking warning in that case; such a request number is still a different, valid number.
    /// </summary>
    Task<IReadOnlyList<RequestNo>> FindCaseInsensitiveNearDuplicatesAsync(long customerId, int jalaliRequestYear, RequestNo requestNo, CancellationToken cancellationToken = default);

    /// <summary>Request list rows, newest request date first; optionally for one customer.</summary>
    Task<IReadOnlyList<RequestSummary>> ListAsync(long? customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the request with its items, stages and approvals, runs <paramref name="change"/> (Domain methods such as adding an item, releasing or
    /// approving a stage, changing a priority), and saves the result: new items, stages and approvals are inserted, changed rows updated. Concurrency
    /// is per row (Decision Log #23): <paramref name="expectedVersion"/> checks the request row, and any row changed by someone else during the
    /// operation raises <see cref="ConcurrencyConflictException"/>. An exception thrown by <paramref name="change"/> propagates and nothing is saved.
    /// </summary>
    Task<Stored<Request>> UpdateAsync(long requestId, long? expectedVersion, Action<Request> change, CancellationToken cancellationToken = default);

    /// <summary>The binary of the request's current PDF with its file name, or null when the request has none or does not exist. The content is loaded only here, never with the request.</summary>
    Task<StoredDocument?> FindCurrentRequestPdfAsync(long requestId, CancellationToken cancellationToken = default);

    /// <summary>Like <see cref="FindAsync"/>, and also returns the row version of the request, of every item and of every stage (Decision Log #77, M8).</summary>
    Task<VersionedRequest?> FindWithVersionsAsync(long requestId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Item- and stage-level expected versions (Decision Log #77, M8). Loads the request with its items, stages and approvals, compares EVERY row named in
    /// <paramref name="expected"/> with the stored row, and only if all of them match runs <paramref name="change"/> (the Domain methods, which keep enforcing their own
    /// rules) and saves the result in one transaction. A named row at another version raises <see cref="ConcurrencyConflictException"/>; a named row that does not
    /// exist raises <see cref="RecordNotFoundException"/>; in both cases <paramref name="change"/> has not run and nothing is saved. A row changed by someone else after the
    /// load raises <see cref="ConcurrencyConflictException"/> at save time and nothing at all is saved (all-or-nothing). QC approvals are appended, never changed or removed.
    ///
    /// The guarantee is per row, not per aggregate: it protects the rows the caller names (and every row the change touches, for the short time between load and
    /// save), not "the request as the caller last saw it" unless the caller names every row it relied on (see <see cref="RowVersionExpectations.FromLoaded"/>).
    /// </summary>
    Task<VersionedRequest> UpdateRowsAsync(long requestId, RowVersionExpectations expected, Action<Request> change, CancellationToken cancellationToken = default);

    /// <summary>
    /// Like <see cref="UpdateRowsAsync(long, RowVersionExpectations, Action{Request}, CancellationToken)"/>, and also appends the audit entries that <paramref name="audit"/> returns
    /// (called after <paramref name="change"/> succeeded, with the changed request) in the SAME transaction: either the change and its audit entries are saved, or neither is. If
    /// <paramref name="audit"/> throws, nothing is saved. Audit entries are append-only (Decision Log #26, #68).
    /// </summary>
    Task<VersionedRequest> UpdateRowsAsync(
        long requestId, RowVersionExpectations expected, Action<Request> change, Func<Request, IReadOnlyList<AuditLogEntry>> audit, CancellationToken cancellationToken = default);
}
