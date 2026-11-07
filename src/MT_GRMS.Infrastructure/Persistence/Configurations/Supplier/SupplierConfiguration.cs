using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.Supplier;

namespace MT_GRMS.Infrastructure.Persistence.Configurations.Supplier;

public class SupplierConfiguration : IEntityTypeConfiguration<Domain.Entities.Supplier.Supplier>
{
    public void Configure(EntityTypeBuilder<Domain.Entities.Supplier.Supplier> builder)
    {
        builder.ToTable("Suppliers");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Code).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(255).IsRequired();
        builder.Property(x => x.TradeName).HasMaxLength(255);
        builder.Property(x => x.SupplierType).HasConversion<int>().IsRequired();

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

        // Delivery configuration properties
        builder.Property(x => x.CutoffDayOffset).HasDefaultValue(1);
        builder.Property(x => x.CutoffTime).HasMaxLength(10).HasDefaultValue("17:00");
        builder.Property(x => x.DeliveryWeekdaysMask).HasDefaultValue(127);
        builder.Property(x => x.DeliveryDaysJson).HasMaxLength(500).HasDefaultValue("[\"Mon\",\"Tue\",\"Wed\",\"Thu\",\"Fri\",\"Sat\"]");
        builder.Property(x => x.MinOrderQuantity).HasDefaultValue(0);
        builder.Property(x => x.AllowUrgent).HasDefaultValue(false);
        builder.Property(x => x.UrgentLeadTimeHours).HasDefaultValue(4);
        builder.Property(x => x.UrgentFeePercent).HasColumnType("decimal(5,2)").HasDefaultValue(0);

        // Indexes
        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.Phone });
        builder.HasIndex(x => new { x.TenantId, x.Status });

        // Relationships
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
