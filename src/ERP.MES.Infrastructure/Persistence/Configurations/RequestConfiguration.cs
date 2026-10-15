using ERP.MES.Domain.Identity;
using ERP.MES.Domain.Requests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.MES.Infrastructure.Persistence.Configurations;

public sealed class RequestConfiguration : IEntityTypeConfiguration<Request>
{
    public void Configure(EntityTypeBuilder<Request> builder)
    {
        builder.ToTable("Requests");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedOnAdd();

        // The customer-supplied number is stored exactly as given, compared as binary text (case-sensitive), never normalised
        // (Decision Log #31). The value object validates it; this only stores its text.
        builder.Property(r => r.RequestNo)
            .HasConversion(value => value.Value, text => RequestNo.Create(text))
            .UseCollation("BINARY")
            .IsRequired();

        builder.Property(r => r.CustomerId).IsRequired();
        builder.Property(r => r.RequestDate).IsRequired();
        builder.Property(r => r.JalaliRequestYear).IsRequired();
        builder.Property(r => r.Title);
        builder.Property(r => r.Description);

        // The approved status set is stored by its enum value; reference tables for controlled codes are an open mapping question.
        builder.Property(r => r.Status).HasConversion<int>().IsRequired();
        builder.Property(r => r.CreatedByUserId).IsRequired();

        // The acting user must exist (Decision Log #70): a real foreign key to Users, deletion restricted.
        builder.HasOne<User>().WithMany().HasForeignKey(r => r.CreatedByUserId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(r => r.CreatedAtUtc).IsRequired();
        builder.Property(r => r.UpdatedAtUtc).IsRequired();
        builder.Ignore(r => r.Key);

        // Application-managed row version and concurrency token (Decision Log #23); see ErpMesDbContext.ApplyRowVersions.
        builder.Property<long>(ErpMesDbContext.RowVersionPropertyName).IsConcurrencyToken().IsRequired();

        // Transactional rows are not physically deleted (Domain and Database Specification 25.3): every foreign key restricts deletion.
        builder.HasOne<Customer>().WithMany().HasForeignKey(r => r.CustomerId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(r => r.Items).WithOne().HasForeignKey("RequestId").IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(r => r.Items).HasField("_items").UsePropertyAccessMode(PropertyAccessMode.Field);

        // Business key: Customer + RequestNo + Jalali Request Year (Domain and Database Specification 4.2, 26).
        builder.HasIndex(r => new { r.CustomerId, r.RequestNo, r.JalaliRequestYear }).IsUnique();
    }
}
