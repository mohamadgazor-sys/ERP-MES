namespace ERP.MES.Application.Persistence;

/// <summary>Base type of the failures the persistence contracts report. Domain rule violations are not persistence failures: they come from the Domain types themselves.</summary>
public class PersistenceException : Exception
{
    public PersistenceException(string message)
        : base(message)
    {
    }

    public PersistenceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>The row was changed by someone else after it was loaded (optimistic concurrency, Decision Log #23). Nothing was saved; reload and retry.</summary>
public sealed class ConcurrencyConflictException : PersistenceException
{
    public ConcurrencyConflictException(string message, Exception? innerException = null)
        : base(message, innerException!)
    {
    }
}

/// <summary>A referenced record (customer, request) does not exist.</summary>
public sealed class RecordNotFoundException : PersistenceException
{
    public RecordNotFoundException(string recordKind, long id)
        : base($"The {recordKind} with id {id} does not exist.")
    {
        RecordKind = recordKind;
        Id = id;
    }

    public string RecordKind { get; }

    public long Id { get; }
}

/// <summary>A request with the same Customer + RequestNo + Jalali Request Year already exists (Domain and Database Specification 4.2).</summary>
public sealed class DuplicateRequestException : PersistenceException
{
    public DuplicateRequestException(ERP.MES.Domain.Requests.RequestKey key, Exception? innerException = null)
        : base($"A request with number '{key.RequestNo}' already exists for this customer in Jalali year {key.JalaliRequestYear}.", innerException!)
    {
        Key = key;
    }

    public ERP.MES.Domain.Requests.RequestKey Key { get; }
}
