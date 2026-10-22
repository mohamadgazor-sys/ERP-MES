using ERP.MES.Application.Persistence;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Calendar;
using ERP.MES.Domain.Materials;
using ERP.MES.Domain.Quality;

namespace ERP.MES.Application.Planning;

/// <summary>The material list as loaded at one moment. <see cref="Prices"/> is empty and <see cref="PricesVisible"/> false for a user who may not read prices.</summary>
public sealed record MaterialData(IReadOnlyList<Stored<Material>> Materials, IReadOnlyList<MaterialPrice> Prices, bool PricesVisible);

/// <summary>What the signed-in user may do on the materials screen, for enabling buttons (the Application's answer; every command checks again).</summary>
public sealed record MaterialCapabilities(bool CanMaintain, bool CanChangePrices, bool CanViewPrices)
{
    public static MaterialCapabilities None { get; } = new(false, false, false);
}

/// <summary>
/// The Warehouse-owned material list and the price-per-kg history (Decision Log #10). Warehouse registers materials; Warehouse and Management add prices (existing permission
/// <see cref="Permission.ChangeMaterialPricePerKg"/>); Planning reads prices as a cost input. A price is an append-only history entry that applies from a Jalali date, must be later than
/// the material's latest entry, and is audited with its reason in the same transaction. Every command reloads the user from the database (Decision Log #74).
/// </summary>
public sealed class MaterialService(IMaterialRepository repository, CurrentAuthorization authorization, IClock clock, IJalaliDateService dates, IAuditContext auditContext)
{
    private static readonly Permission[] AllPermissions = [Permission.MaintainMaterials, Permission.ChangeMaterialPricePerKg, Permission.ViewMaterialPrices];

    public async Task<MaterialCapabilities> GetCapabilitiesAsync(Principal? principal, CancellationToken cancellationToken = default)
    {
        var set = await authorization.EvaluateAsync(principal, AllPermissions, cancellationToken);
        return set.Current is null
            ? MaterialCapabilities.None
            : new MaterialCapabilities(set.Has(Permission.MaintainMaterials), set.Has(Permission.ChangeMaterialPricePerKg), set.Has(Permission.ViewMaterialPrices));
    }

    public async Task<Outcome<MaterialData>> LoadAsync(Principal? principal, CancellationToken cancellationToken = default)
    {
        var set = await authorization.EvaluateAsync(principal, [Permission.ViewMaterials, Permission.ViewMaterialPrices], cancellationToken);
        if (!set.Has(Permission.ViewMaterials))
        {
            return Outcome<MaterialData>.Fail(OperationStatus.NotAuthorized);
        }

        var pricesVisible = set.Has(Permission.ViewMaterialPrices);
        return await CustomerService.GuardAsync(async () => Outcome<MaterialData>.Ok(new MaterialData(
            await repository.ListMaterialsAsync(cancellationToken), pricesVisible ? await repository.ListPricesAsync(cancellationToken) : [], pricesVisible)));
    }

    public async Task<Outcome<Stored<Material>>> AddMaterialAsync(Principal? principal, string? code, string? name, string? grade, string? form, CancellationToken cancellationToken = default)
    {
        var decision = await authorization.CheckAsync(principal, Permission.MaintainMaterials, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<Stored<Material>>.Fail(OperationStatus.NotAuthorized);
        }

        if (!UnitOfMeasure.IsValidCode(code))
        {
            return Outcome<Stored<Material>>.Invalid(ValidationIssue.CodeInvalid);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Outcome<Stored<Material>>.Invalid(ValidationIssue.NameRequired);
        }

        if (TextTooLong(grade) || TextTooLong(form))
        {
            return Outcome<Stored<Material>>.Invalid(ValidationIssue.MaterialTextTooLong);
        }

        var now = clock.UtcNow;
        return await CustomerService.GuardAsync(async () =>
        {
            if ((await repository.ListMaterialsAsync(cancellationToken)).Any(m => string.Equals(m.Value.Code, code, StringComparison.Ordinal)))
            {
                return Outcome<Stored<Material>>.Invalid(ValidationIssue.CodeDuplicate);
            }

            var material = new Material(0, code!, name, grade, form, decision.Current!.UserId, now);
            return Outcome<Stored<Material>>.Ok(await repository.AddMaterialAsync(material, [Audit(decision.Current, now, AuditActions.MaterialAdded, "Material", code!, $"Material {code} registered")], cancellationToken));
        });
    }

    public async Task<Outcome<Stored<Material>>> UpdateMaterialAsync(Principal? principal, long materialId, long expectedVersion, string? name, string? grade, string? form, CancellationToken cancellationToken = default)
    {
        var decision = await authorization.CheckAsync(principal, Permission.MaintainMaterials, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<Stored<Material>>.Fail(OperationStatus.NotAuthorized);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Outcome<Stored<Material>>.Invalid(ValidationIssue.NameRequired);
        }

        if (TextTooLong(grade) || TextTooLong(form))
        {
            return Outcome<Stored<Material>>.Invalid(ValidationIssue.MaterialTextTooLong);
        }

        var now = clock.UtcNow;
        return await CustomerService.GuardAsync(async () =>
        {
            var current = (await repository.ListMaterialsAsync(cancellationToken)).FirstOrDefault(m => m.Value.Id == materialId);
            if (current is null)
            {
                return Outcome<Stored<Material>>.Fail(OperationStatus.NotFound);
            }

            return Outcome<Stored<Material>>.Ok(await repository.UpdateMaterialAsync(
                materialId, expectedVersion, m => m.Update(name, grade, form, now),
                [Audit(decision.Current!, now, AuditActions.MaterialChanged, "Material", current.Value.Code, $"Material {current.Value.Code} name, grade or form changed")], cancellationToken));
        });
    }

    public async Task<Outcome<Stored<Material>>> SetMaterialActiveAsync(Principal? principal, long materialId, long expectedVersion, bool isActive, CancellationToken cancellationToken = default)
    {
        if (!(await authorization.CheckAsync(principal, Permission.MaintainMaterials, cancellationToken)).IsAllowed)
        {
            return Outcome<Stored<Material>>.Fail(OperationStatus.NotAuthorized);
        }

        var now = clock.UtcNow;
        return await CustomerService.GuardAsync(async () => Outcome<Stored<Material>>.Ok(await repository.UpdateMaterialAsync(materialId, expectedVersion, m => m.SetActive(isActive, now), cancellationToken: cancellationToken)));
    }

    /// <summary>
    /// Adds a price per kg (Rial) for an active material, applying from the typed Jalali date; it must be later than the material's latest entry (the history is never rewritten).
    /// Warehouse and Management only; the change is audited with the old and new value.
    /// </summary>
    public async Task<Outcome<MaterialPrice>> AddPriceAsync(
        Principal? principal, long materialId, decimal pricePerKgRial, string? effectiveFromJalali, string? reason, CancellationToken cancellationToken = default)
    {
        var decision = await authorization.CheckAsync(principal, Permission.ChangeMaterialPricePerKg, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<MaterialPrice>.Fail(OperationStatus.NotAuthorized);
        }

        if (pricePerKgRial <= 0 || pricePerKgRial > MaterialPrice.MaxPricePerKgRial)
        {
            return Outcome<MaterialPrice>.Invalid(ValidationIssue.PriceInvalid);
        }

        if (string.IsNullOrWhiteSpace(effectiveFromJalali))
        {
            return Outcome<MaterialPrice>.Invalid(ValidationIssue.EffectiveDateRequired);
        }

        if (!dates.TryParseDate(effectiveFromJalali, out var jalali, out var dateError))
        {
            return Outcome<MaterialPrice>.Invalid(ValidationIssue.EffectiveDateInvalid, dateError);
        }

        if (reason is not null && reason.Trim().Length > MaterialPrice.MaxReasonLength)
        {
            return Outcome<MaterialPrice>.Invalid(ValidationIssue.ReasonTooLong);
        }

        var effectiveFrom = dates.ToGregorian(jalali);
        var now = clock.UtcNow;
        return await CustomerService.GuardAsync(async () =>
        {
            var material = (await repository.ListMaterialsAsync(cancellationToken)).FirstOrDefault(m => m.Value.Id == materialId);
            if (material is null || !material.Value.IsActive)
            {
                return Outcome<MaterialPrice>.Invalid(ValidationIssue.MaterialInvalid);
            }

            var latest = (await repository.ListPricesAsync(cancellationToken)).Where(p => p.MaterialId == materialId).MaxBy(p => p.EffectiveFrom);
            if (latest is not null && effectiveFrom <= latest.EffectiveFrom)
            {
                return Outcome<MaterialPrice>.Invalid(ValidationIssue.PriceNotLater);
            }

            var price = new MaterialPrice(0, materialId, pricePerKgRial, effectiveFrom, reason, decision.Current!.UserId, now);
            string Rial(decimal value) => value.ToString("0.############################", System.Globalization.CultureInfo.InvariantCulture);
            var summary = $"Material {material.Value.Code}: price per kg {(latest is null ? "set" : Rial(latest.PricePerKgRial) + " ->")} {Rial(pricePerKgRial)} Rial from {effectiveFrom:yyyy-MM-dd}"
                + (string.IsNullOrWhiteSpace(reason) ? string.Empty : "; reason recorded with the price");
            return Outcome<MaterialPrice>.Ok(await repository.AddPriceAsync(price, [Audit(decision.Current, now, AuditActions.MaterialPriceChanged, "Material", material.Value.Code, summary)], cancellationToken));
        });
    }

    private static bool TextTooLong(string? value) => value is not null && value.Trim().Length > Material.MaxDescriptorLength;

    private AuditLogEntry Audit(Principal user, DateTime now, string action, string targetType, string targetId, string summary) =>
        new(now, user.UserId, auditContext.MachineName, auditContext.OsUserName, auditContext.DatabaseLogin, action, targetType, targetId, summary);
}
