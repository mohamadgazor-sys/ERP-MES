using ERP.MES.Application.Persistence;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Application.Requests;

/// <summary>What Planning typed to define one item. Quantity is in the chosen quantity type; weight is kg per base unit; length and diameter are millimetres.</summary>
public sealed record DefineItemCommand(
    string? ItemNo, string? PartName, decimal Quantity, Priority Priority, long? QuantityTypeId, long? MaterialId, decimal? WeightKgPerBaseUnit, decimal? LengthMm, decimal? DiameterMm);

/// <summary>What Planning typed to plan one stage of an item: title, minutes per piece, the production unit and optionally the machine. Planned quantity defaults to the item's quantity.</summary>
public sealed record DefineStageCommand(string? StageTitle, decimal PlannedMinutesPerPiece, long ProductionUnitId, long? MachineId, decimal? PlannedQuantity = null);

/// <summary>What the signed-in user may do in item definition, for enabling buttons (the Application's answer; every command checks again).</summary>
public sealed record ItemDefinitionCapabilities(bool CanDefineMake, bool CanDefineRepair)
{
    public static ItemDefinitionCapabilities None { get; } = new(false, false);

    public bool CanDefine => CanDefineMake || CanDefineRepair;
}

/// <summary>
/// Item and stage definition by Planning (Decision Log #86, #95): the item's specification (quantity type, material, weight, dimensions) and its stages with production unit and machine.
/// Drawing no longer defines items. The command is refused unless the Management, Drawing, contract, inquiry and clarification gates hold (<see cref="RequestGateService"/>, evaluated here in
/// the Application, not in the screen). A MAKE item needs a quantity type, a material and a weight; a REPAIR item may omit them. The quantity type must be an active QC definition, the material an
/// active Warehouse material, the unit and machine active Planning resources, and a machine must belong to the chosen unit. Every command reloads the user from the database, passes the row
/// versions the caller holds and writes the change with its audit entry in one transaction.
/// </summary>
public sealed class ItemDefinitionService(
    IRequestRepository requests,
    IQcMasterRepository qc,
    IMaterialRepository materials,
    IPlanningMasterRepository planning,
    RequestGateService gates,
    CurrentAuthorization authorization,
    IClock clock,
    IAuditContext auditContext)
{
    private static readonly Permission[] DefinePermissions = [Permission.DefineMakeItems, Permission.DefineRepairItemsAndEditAnyItem, Permission.ChangePlanningPriority];

    public async Task<ItemDefinitionCapabilities> GetCapabilitiesAsync(Principal? principal, CancellationToken cancellationToken = default)
    {
        var set = await authorization.EvaluateAsync(principal, DefinePermissions, cancellationToken);
        return set.Current is null
            ? ItemDefinitionCapabilities.None
            : new ItemDefinitionCapabilities(set.Has(Permission.DefineMakeItems), set.Has(Permission.DefineRepairItemsAndEditAnyItem));
    }

    public async Task<Outcome<Stored<Request>>> DefineItemAsync(Principal? principal, long requestId, long expectedRequestVersion, DefineItemCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var set = await authorization.EvaluateAsync(principal, DefinePermissions, cancellationToken);
        if (set.Current is null || !(set.Has(Permission.DefineMakeItems) || set.Has(Permission.DefineRepairItemsAndEditAnyItem)))
        {
            return Outcome<Stored<Request>>.Fail(OperationStatus.NotAuthorized);
        }

        if (!Enum.IsDefined(command.Priority))
        {
            return Outcome<Stored<Request>>.Invalid(ValidationIssue.PriorityInvalid);
        }

        if (command.Priority != Priority.Normal && !set.Has(Permission.ChangePlanningPriority))
        {
            return Outcome<Stored<Request>>.Fail(OperationStatus.NotAuthorized);
        }

        if (string.IsNullOrWhiteSpace(command.ItemNo))
        {
            return Outcome<Stored<Request>>.Invalid(ValidationIssue.ItemNoRequired);
        }

        if (string.IsNullOrWhiteSpace(command.PartName))
        {
            return Outcome<Stored<Request>>.Invalid(ValidationIssue.PartNameRequired);
        }

        if (command.Quantity <= 0)
        {
            return Outcome<Stored<Request>>.Invalid(ValidationIssue.QuantityInvalid);
        }

        var specification = new ItemSpecification(command.QuantityTypeId, command.MaterialId, command.WeightKgPerBaseUnit, command.LengthMm, command.DiameterMm);
        var user = set.Current;
        var now = clock.UtcNow;
        return await CustomerService.GuardAsync(async () =>
        {
            var current = await requests.FindAsync(requestId, cancellationToken);
            if (current is null)
            {
                return Outcome<Stored<Request>>.Fail(OperationStatus.NotFound);
            }

            var mayDefine = current.Value.WorkType == WorkType.Make ? set.Has(Permission.DefineMakeItems) : set.Has(Permission.DefineRepairItemsAndEditAnyItem);
            if (!mayDefine)
            {
                return Outcome<Stored<Request>>.Fail(OperationStatus.NotAuthorized);
            }

            if (current.Value.Status is RequestStatus.Cancelled or RequestStatus.Closed)
            {
                return Outcome<Stored<Request>>.Invalid(ValidationIssue.RequestClosedForItems);
            }

            var gate = await gates.EvaluateAsync(requestId, cancellationToken);
            if (gate is null || !gate.Satisfied)
            {
                return Outcome<Stored<Request>>.Invalid(ValidationIssue.ItemGateNotSatisfied);
            }

            var problem = specification.Check(current.Value.WorkType);
            if (problem != SpecificationProblem.None)
            {
                return Outcome<Stored<Request>>.Invalid(problem switch
                {
                    SpecificationProblem.WeightInvalid => ValidationIssue.WeightInvalid,
                    SpecificationProblem.DimensionInvalid => ValidationIssue.DimensionInvalid,
                    _ => ValidationIssue.ItemSpecificationIncomplete,
                });
            }

            if (command.QuantityTypeId is { } typeId)
            {
                var type = await qc.FindQuantityTypeAsync(typeId, cancellationToken);
                if (type is null || !type.Value.IsActive)
                {
                    return Outcome<Stored<Request>>.Invalid(ValidationIssue.QuantityTypeInvalid);
                }

                // QC inspects in base units: the item keeps how many base units one ordered unit counts at the time it is defined (Decision Log #90, #101).
                specification = specification with { BaseUnitsPerUnit = type.Value.BaseUnitsPerType };
            }

            if (command.MaterialId is { } materialId)
            {
                var material = (await materials.ListMaterialsAsync(cancellationToken)).FirstOrDefault(m => m.Value.Id == materialId);
                if (material is null || !material.Value.IsActive)
                {
                    return Outcome<Stored<Request>>.Invalid(ValidationIssue.MaterialInvalid);
                }
            }

            if (current.Value.HasItemNo(command.ItemNo))
            {
                return Outcome<Stored<Request>>.Invalid(ValidationIssue.ItemNoDuplicate);
            }

            try
            {
                var saved = await requests.UpdateRowsAsync(
                    requestId,
                    RowVersionExpectations.ForRequest(expectedRequestVersion),
                    r => r.DefineItem(0, command.ItemNo, command.PartName, command.Quantity, specification, user.UserId, now, command.Priority),
                    r => [Audit(user, now, AuditActions.ItemDefined, requestId, $"Request {r.RequestNo.Value}: item '{command.ItemNo}' defined by Planning ({r.WorkType.Code()}, quantity {command.Quantity})")],
                    cancellationToken);
                return Outcome<Stored<Request>>.Ok(new Stored<Request>(saved.Value, saved.Versions.Request));
            }
            catch (ItemSpecificationException)
            {
                return Outcome<Stored<Request>>.Invalid(ValidationIssue.ItemSpecificationIncomplete);
            }
            catch (InvalidOperationException)
            {
                return Outcome<Stored<Request>>.Fail(OperationStatus.InvalidState);
            }
        });
    }

    public async Task<Outcome<Stored<Request>>> DefineStageAsync(
        Principal? principal, long requestId, long expectedRequestVersion, long itemId, long expectedItemVersion, DefineStageCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var set = await authorization.EvaluateAsync(principal, DefinePermissions, cancellationToken);
        if (set.Current is null || !(set.Has(Permission.DefineMakeItems) || set.Has(Permission.DefineRepairItemsAndEditAnyItem)))
        {
            return Outcome<Stored<Request>>.Fail(OperationStatus.NotAuthorized);
        }

        if (string.IsNullOrWhiteSpace(command.StageTitle) || command.StageTitle.Trim().Length > 200)
        {
            return Outcome<Stored<Request>>.Invalid(ValidationIssue.StageTitleRequired);
        }

        if (command.PlannedMinutesPerPiece <= 0)
        {
            return Outcome<Stored<Request>>.Invalid(ValidationIssue.StageMinutesInvalid);
        }

        if (command.PlannedQuantity is <= 0)
        {
            return Outcome<Stored<Request>>.Invalid(ValidationIssue.QuantityInvalid);
        }

        var user = set.Current;
        var now = clock.UtcNow;
        return await CustomerService.GuardAsync(async () =>
        {
            var current = await requests.FindAsync(requestId, cancellationToken);
            if (current is null)
            {
                return Outcome<Stored<Request>>.Fail(OperationStatus.NotFound);
            }

            var mayDefine = current.Value.WorkType == WorkType.Make ? set.Has(Permission.DefineMakeItems) : set.Has(Permission.DefineRepairItemsAndEditAnyItem);
            if (!mayDefine)
            {
                return Outcome<Stored<Request>>.Fail(OperationStatus.NotAuthorized);
            }

            if (current.Value.Items.All(i => i.Id != itemId))
            {
                return Outcome<Stored<Request>>.Invalid(ValidationIssue.ItemNotFound);
            }

            var gate = await gates.EvaluateAsync(requestId, cancellationToken);
            if (gate is null || !gate.Satisfied)
            {
                return Outcome<Stored<Request>>.Invalid(ValidationIssue.ItemGateNotSatisfied);
            }

            var unit = (await planning.ListUnitsAsync(cancellationToken)).FirstOrDefault(u => u.Value.Id == command.ProductionUnitId);
            if (unit is null || !unit.Value.IsActive)
            {
                return Outcome<Stored<Request>>.Invalid(ValidationIssue.ProductionUnitInvalid);
            }

            if (command.MachineId is { } machineId)
            {
                var machine = (await planning.ListMachinesAsync(cancellationToken)).FirstOrDefault(m => m.Value.Id == machineId);
                if (machine is null || !machine.Value.IsActive)
                {
                    return Outcome<Stored<Request>>.Invalid(ValidationIssue.MachineInvalid);
                }

                if (machine.Value.ProductionUnitId != command.ProductionUnitId)
                {
                    return Outcome<Stored<Request>>.Invalid(ValidationIssue.MachineUnitMismatch);
                }
            }

            try
            {
                var saved = await requests.UpdateRowsAsync(
                    requestId,
                    RowVersionExpectations.ForRequest(expectedRequestVersion).WithItem(itemId, expectedItemVersion),
                    r => r.Items.Single(i => i.Id == itemId).DefineInitialStage(command.StageTitle!.Trim(), command.PlannedMinutesPerPiece, command.PlannedQuantity, command.ProductionUnitId, command.MachineId),
                    r => [Audit(user, now, AuditActions.ItemStageDefined, requestId, $"Request {r.RequestNo.Value}: stage of item '{r.Items.Single(i => i.Id == itemId).ItemNo}' planned in unit {unit.Value.Code}" + (command.MachineId is null ? string.Empty : $" on machine {command.MachineId}"))],
                    cancellationToken);
                return Outcome<Stored<Request>>.Ok(new Stored<Request>(saved.Value, saved.Versions.Request));
            }
            catch (InvalidOperationException)
            {
                // The route can only be defined before its first stage is released.
                return Outcome<Stored<Request>>.Fail(OperationStatus.InvalidState);
            }
        });
    }

    private AuditLogEntry Audit(Principal user, DateTime now, string action, long requestId, string summary) =>
        new(now, user.UserId, auditContext.MachineName, auditContext.OsUserName, auditContext.DatabaseLogin, action, "Request", requestId.ToString(System.Globalization.CultureInfo.InvariantCulture), summary);
}
