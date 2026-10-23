using ERP.MES.Domain.Costing;
using ERP.MES.Domain.Identity;
using ERP.MES.Domain.Materials;
using ERP.MES.Domain.Planning;
using ERP.MES.Domain.Requests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.MES.Infrastructure.Persistence.Configurations;

/// <summary>An immutable cost snapshot (Decision Log #104); one per calculation of an item, numbered per item. Money is stored as exact decimal Rial.</summary>
public sealed class ItemCostCalculationConfiguration : IEntityTypeConfiguration<ItemCostCalculation>
{
    public void Configure(EntityTypeBuilder<ItemCostCalculation> builder)
    {
        builder.ToTable("ItemCostCalculations");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedOnAdd();
        builder.Property(c => c.RequestItemId).IsRequired();
        builder.HasOne<RequestItem>().WithMany().HasForeignKey(c => c.RequestItemId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(c => c.SequenceNo).IsRequired();
        builder.HasIndex(c => new { c.RequestItemId, c.SequenceNo }).IsUnique();
        builder.Property(c => c.Formula).IsRequired();
        builder.Property(c => c.AcceptedBaseUnits).IsRequired();
        builder.Property(c => c.ConsumedBaseUnits).IsRequired();
        builder.Property(c => c.MaterialId);
        builder.HasOne<Material>().WithMany().HasForeignKey(c => c.MaterialId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        builder.Property(c => c.MaterialPriceId);
        builder.HasOne<MaterialPrice>().WithMany().HasForeignKey(c => c.MaterialPriceId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        builder.Property(c => c.PriceDate);
        builder.Property(c => c.MachiningTotalRial).IsRequired();
        builder.Property(c => c.MaterialTotalRial).IsRequired();
        builder.Property(c => c.TotalRial).IsRequired();
        builder.Property(c => c.UnitCostRial).IsRequired();
        builder.Property(c => c.CalculatedByUserId).IsRequired();
        builder.HasOne<User>().WithMany().HasForeignKey(c => c.CalculatedByUserId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(c => c.CalculatedAtUtc).IsRequired();
        builder.HasMany(c => c.Lines).WithOne().HasForeignKey("CalculationId").IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(c => c.Lines).HasField("_lines").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class ItemCostLineConfiguration : IEntityTypeConfiguration<ItemCostLine>
{
    public void Configure(EntityTypeBuilder<ItemCostLine> builder)
    {
        builder.ToTable("ItemCostLines");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedOnAdd();
        builder.Property(l => l.LineNo).IsRequired();
        builder.Property(l => l.Kind).HasConversion<int>().IsRequired();
        builder.Property(l => l.JobReportLineId);
        builder.HasOne<ERP.MES.Domain.Reporting.JobReportLine>().WithMany().HasForeignKey(l => l.JobReportLineId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        builder.Property(l => l.MachineId);
        builder.HasOne<Machine>().WithMany().HasForeignKey(l => l.MachineId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        builder.Property(l => l.RateId);
        builder.Property(l => l.WorkDate);
        builder.Property(l => l.Minutes);
        builder.Property(l => l.WeightKg);
        builder.Property(l => l.UnitPriceRial).IsRequired();
        builder.Property(l => l.AmountRial).IsRequired();
    }
}

/// <summary>The price decision of a snapshot: the one mutable row of costing (Draft, Submitted, Approved), under an application-managed row version.</summary>
public sealed class ItemPriceDecisionConfiguration : IEntityTypeConfiguration<ItemPriceDecision>
{
    public void Configure(EntityTypeBuilder<ItemPriceDecision> builder)
    {
        builder.ToTable("ItemPriceDecisions");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedOnAdd();
        builder.Property(d => d.CalculationId).IsRequired();
        builder.HasOne(d => d.Calculation).WithMany().HasForeignKey(d => d.CalculationId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(d => d.CalculationId).IsUnique();
        builder.Property(d => d.RequestItemId).IsRequired();
        builder.HasOne<RequestItem>().WithMany().HasForeignKey(d => d.RequestItemId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(d => d.Basis).HasConversion<int>().IsRequired();
        builder.Property(d => d.ProposedPriceRial);
        builder.Property(d => d.Status).HasConversion<int>().IsRequired();
        builder.Property(d => d.SubmittedByUserId);
        builder.HasOne<User>().WithMany().HasForeignKey(d => d.SubmittedByUserId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        builder.Property(d => d.SubmittedAtUtc);
        builder.Property(d => d.ApprovedPriceRial);
        builder.Property(d => d.Reason);
        builder.Property(d => d.ContractReference);
        builder.Property(d => d.ApprovedByUserId);
        builder.HasOne<User>().WithMany().HasForeignKey(d => d.ApprovedByUserId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        builder.Property(d => d.ApprovedAtUtc);
        builder.Property<long>(ErpMesDbContext.RowVersionPropertyName).IsConcurrencyToken().IsRequired();
    }
}
