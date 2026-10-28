using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.Supplier;

namespace MT_GRMS.Infrastructure.Persistence.Configurations.Supplier;

public class SupplierConfiguration : IEntityTypeConfiguration<Domain.Entities.Supplier.Supplier>
{
    public void Configure(EntityTypeBuilder<Domain.Entities.Supplier.Supplier> builder)
    {
        builder.ToTable("suppliers");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Code).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(255).IsRequired();
        builder.Property(x => x.TradeName).HasMaxLength(255);
        builder.Property(x => x.SupplierType).HasConversion<int>().IsRequired();
        builder.Property(x => x.CategoryCode).HasMaxLength(50).HasDefaultValue("FRESH");

        builder.Property(x => x.ContactPerson).HasMaxLength(150).IsRequired();
        builder.Property(x => x.Phone).HasMaxLength(30).IsRequired();
        builder.Property(x => x.Email).HasMaxLength(150);
        builder.Property(x => x.Address).HasMaxLength(500);

        builder.Property(x => x.PaymentTermDays).HasDefaultValue(0);
        builder.Property(x => x.CreditLimit).HasColumnType("decimal(18,2)").HasDefaultValue(0);
        builder.Property(x => x.CurrentDebt).HasColumnType("decimal(18,2)").HasDefaultValue(0);
        builder.Property(x => x.OverdueDebt).HasColumnType("decimal(18,2)").HasDefaultValue(0);
        builder.Property(x => x.Rating).HasColumnType("decimal(3,2)").HasDefaultValue(5.0m);
        builder.Property(x => x.Status).HasConversion<int>().IsRequired();

        builder.Property(x => x.LinkedCustomerId).HasMaxLength(64);
        builder.Property(x => x.TaxCode).HasMaxLength(50);
        builder.Property(x => x.CompanyName).HasMaxLength(255);
        builder.Property(x => x.InvoiceAddress).HasMaxLength(500);
        builder.Property(x => x.BankName).HasMaxLength(100);
        builder.Property(x => x.BankAccount).HasMaxLength(50);
        builder.Property(x => x.AccountHolder).HasMaxLength(150);
        builder.Property(x => x.Note).HasMaxLength(1000);

        // Indexes
        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.Phone });
        builder.HasIndex(x => new { x.TenantId, x.Status });

        // Relationships
        builder.HasOne(x => x.Category)
            .WithMany(c => c.Suppliers)
            .HasForeignKey(x => x.CategoryId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.DeliveryConfig)
            .WithOne(d => d.Supplier)
            .HasForeignKey<SupplierDeliveryConfig>(d => d.SupplierId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.APLedgers)
            .WithOne(a => a.Supplier)
            .HasForeignKey(a => a.SupplierId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.BlackoutDates)
            .WithOne(b => b.Supplier)
            .HasForeignKey(b => b.SupplierId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.BranchAssignments)
            .WithOne(a => a.Supplier)
            .HasForeignKey(a => a.SupplierId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
