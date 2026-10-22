using ERP.MES.Application.Persistence;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Quality;

namespace ERP.MES.Application.Quality;

/// <summary>The QC master data as loaded at one moment, for the screen and for selection by other departments.</summary>
public sealed record QcMasterData(
    IReadOnlyList<Stored<UnitOfMeasure>> Units, IReadOnlyList<Stored<UnitConversion>> Conversions, IReadOnlyList<Stored<QuantityType>> QuantityTypes);

/// <summary>What a person typed for a new quantity type. <see cref="Members"/> is empty for a piece-like type (then <see cref="BaseUnits"/> is used) and lists the composition of a set.</summary>
public sealed record AddQuantityTypeCommand(string? Code, string? Name, long BaseUnitId, int BaseUnits, IReadOnlyList<SetMemberInput>? Members = null);

/// <summary>One member of a set as typed.</summary>
public sealed record SetMemberInput(string? Description, int BaseUnits);

/// <summary>
/// QC master data: units of measure, conversions between them and quantity types (Decision Log #9, #90). QC alone changes it (<see cref="Permission.MaintainUnitsOfMeasure"/>); every
/// active user reads it (<see cref="Permission.ViewQcMasterData"/>), because Planning selects from it when it defines items. Every command reloads the user from the database
/// (Decision Log #74) and updates pass the version the row was loaded at. Codes are stable and unique; definitions are retired, never deleted; the mapping of a quantity type to its base
/// counting unit is immutable.
/// </summary>
public sealed class QcMasterService(IQcMasterRepository repository, CurrentAuthorization authorization, IClock clock)
{
    public async Task<bool> CanMaintainAsync(Principal? principal, CancellationToken cancellationToken = default) =>
        (await authorization.CheckAsync(principal, Permission.MaintainUnitsOfMeasure, cancellationToken)).IsAllowed;

    public async Task<Outcome<QcMasterData>> LoadAsync(Principal? principal, CancellationToken cancellationToken = default)
    {
        if (!(await authorization.CheckAsync(principal, Permission.ViewQcMasterData, cancellationToken)).IsAllowed)
        {
            return Outcome<QcMasterData>.Fail(OperationStatus.NotAuthorized);
        }

        return await CustomerService.GuardAsync(async () => Outcome<QcMasterData>.Ok(new QcMasterData(
            await repository.ListUnitsAsync(cancellationToken), await repository.ListConversionsAsync(cancellationToken), await repository.ListQuantityTypesAsync(cancellationToken))));
    }

    // ---- units of measure ----

    public async Task<Outcome<Stored<UnitOfMeasure>>> AddUnitAsync(Principal? principal, string? code, string? name, UnitKind kind, CancellationToken cancellationToken = default)
    {
        var decision = await authorization.CheckAsync(principal, Permission.MaintainUnitsOfMeasure, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<Stored<UnitOfMeasure>>.Fail(OperationStatus.NotAuthorized);
        }

        if (!UnitOfMeasure.IsValidCode(code))
        {
            return Outcome<Stored<UnitOfMeasure>>.Invalid(ValidationIssue.CodeInvalid);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Outcome<Stored<UnitOfMeasure>>.Invalid(ValidationIssue.NameRequired);
        }

        if (!Enum.IsDefined(kind))
        {
            return Outcome<Stored<UnitOfMeasure>>.Invalid(ValidationIssue.UnitKindInvalid);
        }

        var now = clock.UtcNow;
        return await CustomerService.GuardAsync(async () =>
        {
            if ((await repository.ListUnitsAsync(cancellationToken)).Any(u => string.Equals(u.Value.Code, code, StringComparison.Ordinal)))
            {
                return Outcome<Stored<UnitOfMeasure>>.Invalid(ValidationIssue.CodeDuplicate);
            }

            return Outcome<Stored<UnitOfMeasure>>.Ok(await repository.AddUnitAsync(new UnitOfMeasure(0, code!, name, kind, decision.Current!.UserId, now), cancellationToken));
        });
    }

    public async Task<Outcome<Stored<UnitOfMeasure>>> RenameUnitAsync(Principal? principal, long unitId, long expectedVersion, string? name, CancellationToken cancellationToken = default)
    {
        if (!(await authorization.CheckAsync(principal, Permission.MaintainUnitsOfMeasure, cancellationToken)).IsAllowed)
        {
            return Outcome<Stored<UnitOfMeasure>>.Fail(OperationStatus.NotAuthorized);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Outcome<Stored<UnitOfMeasure>>.Invalid(ValidationIssue.NameRequired);
        }

        var now = clock.UtcNow;
        return await CustomerService.GuardAsync(async () => Outcome<Stored<UnitOfMeasure>>.Ok(
            await repository.UpdateUnitAsync(unitId, expectedVersion, u => u.Rename(name, now), cancellationToken)));
    }

    public async Task<Outcome<Stored<UnitOfMeasure>>> SetUnitActiveAsync(Principal? principal, long unitId, long expectedVersion, bool isActive, CancellationToken cancellationToken = default)
    {
        if (!(await authorization.CheckAsync(principal, Permission.MaintainUnitsOfMeasure, cancellationToken)).IsAllowed)
        {
            return Outcome<Stored<UnitOfMeasure>>.Fail(OperationStatus.NotAuthorized);
        }

        var now = clock.UtcNow;
        return await CustomerService.GuardAsync(async () => Outcome<Stored<UnitOfMeasure>>.Ok(
            await repository.UpdateUnitAsync(unitId, expectedVersion, u => u.SetActive(isActive, now), cancellationToken)));
    }

    // ---- conversions ----

    /// <summary>"One <paramref name="fromUnitId"/> equals <paramref name="factor"/> of <paramref name="toUnitId"/>". The reciprocal is derived, so a second definition in the opposite direction is refused.</summary>
    public async Task<Outcome<Stored<UnitConversion>>> AddConversionAsync(Principal? principal, long fromUnitId, long toUnitId, decimal factor, CancellationToken cancellationToken = default)
    {
        var decision = await authorization.CheckAsync(principal, Permission.MaintainUnitsOfMeasure, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<Stored<UnitConversion>>.Fail(OperationStatus.NotAuthorized);
        }

        if (fromUnitId == toUnitId)
        {
            return Outcome<Stored<UnitConversion>>.Invalid(ValidationIssue.ConversionSameUnit);
        }

        if (factor <= 0)
        {
            return Outcome<Stored<UnitConversion>>.Invalid(ValidationIssue.FactorInvalid);
        }

        var now = clock.UtcNow;
        return await CustomerService.GuardAsync(async () =>
        {
            var units = await repository.ListUnitsAsync(cancellationToken);
            if (units.All(u => u.Value.Id != fromUnitId) || units.All(u => u.Value.Id != toUnitId))
            {
                return Outcome<Stored<UnitConversion>>.Invalid(ValidationIssue.UnitNotFound);
            }

            var existing = await repository.ListConversionsAsync(cancellationToken);
            if (existing.Any(c => (c.Value.FromUnitId == fromUnitId && c.Value.ToUnitId == toUnitId) || (c.Value.FromUnitId == toUnitId && c.Value.ToUnitId == fromUnitId)))
            {
                return Outcome<Stored<UnitConversion>>.Invalid(ValidationIssue.ConversionDuplicate);
            }

            return Outcome<Stored<UnitConversion>>.Ok(await repository.AddConversionAsync(new UnitConversion(0, fromUnitId, toUnitId, factor, decision.Current!.UserId, now), cancellationToken));
        });
    }

    public async Task<Outcome<Stored<UnitConversion>>> SetConversionActiveAsync(Principal? principal, long conversionId, long expectedVersion, bool isActive, CancellationToken cancellationToken = default)
    {
        if (!(await authorization.CheckAsync(principal, Permission.MaintainUnitsOfMeasure, cancellationToken)).IsAllowed)
        {
            return Outcome<Stored<UnitConversion>>.Fail(OperationStatus.NotAuthorized);
        }

        var now = clock.UtcNow;
        return await CustomerService.GuardAsync(async () => Outcome<Stored<UnitConversion>>.Ok(
            await repository.UpdateConversionAsync(conversionId, expectedVersion, c => c.SetActive(isActive, now), cancellationToken)));
    }

    // ---- quantity types ----

    public async Task<Outcome<Stored<QuantityType>>> AddQuantityTypeAsync(Principal? principal, AddQuantityTypeCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var decision = await authorization.CheckAsync(principal, Permission.MaintainUnitsOfMeasure, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<Stored<QuantityType>>.Fail(OperationStatus.NotAuthorized);
        }

        if (!UnitOfMeasure.IsValidCode(command.Code))
        {
            return Outcome<Stored<QuantityType>>.Invalid(ValidationIssue.CodeInvalid);
        }

        if (string.IsNullOrWhiteSpace(command.Name))
        {
            return Outcome<Stored<QuantityType>>.Invalid(ValidationIssue.NameRequired);
        }

        var isSet = command.Members is { Count: > 0 };
        if (isSet && (command.Members!.Count < 2 || command.Members.Any(m => string.IsNullOrWhiteSpace(m.Description) || m.BaseUnits < 1)))
        {
            return Outcome<Stored<QuantityType>>.Invalid(ValidationIssue.SetMembersInvalid);
        }

        if (!isSet && command.BaseUnits < 1)
        {
            return Outcome<Stored<QuantityType>>.Invalid(ValidationIssue.BaseUnitsInvalid);
        }

        var now = clock.UtcNow;
        return await CustomerService.GuardAsync(async () =>
        {
            var baseUnit = (await repository.ListUnitsAsync(cancellationToken)).FirstOrDefault(u => u.Value.Id == command.BaseUnitId);
            if (baseUnit is null || baseUnit.Value.Kind != UnitKind.Count || !baseUnit.Value.IsActive)
            {
                return Outcome<Stored<QuantityType>>.Invalid(ValidationIssue.BaseUnitInvalid);
            }

            if ((await repository.ListQuantityTypesAsync(cancellationToken)).Any(q => string.Equals(q.Value.Code, command.Code, StringComparison.Ordinal)))
            {
                return Outcome<Stored<QuantityType>>.Invalid(ValidationIssue.CodeDuplicate);
            }

            var userId = decision.Current!.UserId;
            var type = isSet
                ? QuantityType.CreateSet(0, command.Code!, command.Name, command.BaseUnitId, command.Members!.Select(m => (m.Description!, m.BaseUnits)), userId, now)
                : QuantityType.CreateSimple(0, command.Code!, command.Name, command.BaseUnitId, command.BaseUnits, userId, now);
            return Outcome<Stored<QuantityType>>.Ok(await repository.AddQuantityTypeAsync(type, cancellationToken));
        });
    }

    public async Task<Outcome<Stored<QuantityType>>> RenameQuantityTypeAsync(Principal? principal, long quantityTypeId, long expectedVersion, string? name, CancellationToken cancellationToken = default)
    {
        if (!(await authorization.CheckAsync(principal, Permission.MaintainUnitsOfMeasure, cancellationToken)).IsAllowed)
        {
            return Outcome<Stored<QuantityType>>.Fail(OperationStatus.NotAuthorized);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Outcome<Stored<QuantityType>>.Invalid(ValidationIssue.NameRequired);
        }

        var now = clock.UtcNow;
        return await CustomerService.GuardAsync(async () => Outcome<Stored<QuantityType>>.Ok(
            await repository.UpdateQuantityTypeAsync(quantityTypeId, expectedVersion, q => q.Rename(name, now), cancellationToken)));
    }

    public async Task<Outcome<Stored<QuantityType>>> SetQuantityTypeActiveAsync(Principal? principal, long quantityTypeId, long expectedVersion, bool isActive, CancellationToken cancellationToken = default)
    {
        if (!(await authorization.CheckAsync(principal, Permission.MaintainUnitsOfMeasure, cancellationToken)).IsAllowed)
        {
            return Outcome<Stored<QuantityType>>.Fail(OperationStatus.NotAuthorized);
        }

        var now = clock.UtcNow;
        return await CustomerService.GuardAsync(async () => Outcome<Stored<QuantityType>>.Ok(
            await repository.UpdateQuantityTypeAsync(quantityTypeId, expectedVersion, q => q.SetActive(isActive, now), cancellationToken)));
    }
}
