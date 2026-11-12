using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.Finance;

namespace MT_GRMS.Infrastructure.Persistence.Configurations.Finance;

public class BusinessDayClosingConfiguration : IEntityTypeConfiguration<BusinessDayClosing>
{
    public void Configure(EntityTypeBuilder<BusinessDayClosing> builder)
    {
        builder.ToTable("BusinessDayClosings");
        builder.Property(x => x.ReferenceCode).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Note).HasMaxLength(500);
        builder.Property(x => x.TotalCashSales).HasColumnType("decimal(18,2)");
        builder.Property(x => x.TotalBankSales).HasColumnType("decimal(18,2)");
        builder.Property(x => x.OwnerShiftlessCashSales).HasColumnType("decimal(18,2)");
        builder.Property(x => x.OwnerShiftlessBankSales).HasColumnType("decimal(18,2)");
        builder.Property(x => x.TotalCashIn).HasColumnType("decimal(18,2)");
        builder.Property(x => x.TotalCashOut).HasColumnType("decimal(18,2)");
        builder.Property(x => x.TotalRetainedFloat).HasColumnType("decimal(18,2)");
        builder.Property(x => x.TotalDroppedToSafe).HasColumnType("decimal(18,2)");
        builder.Property(x => x.TotalDiscrepancy).HasColumnType("decimal(18,2)");
        builder.HasIndex(x => new { x.TenantId, x.BranchId, x.BusinessDate }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.ReferenceCode }).IsUnique();
    }
}
