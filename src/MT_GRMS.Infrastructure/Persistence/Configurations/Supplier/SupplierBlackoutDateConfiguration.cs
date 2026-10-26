using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.Supplier;

namespace MT_GRMS.Infrastructure.Persistence.Configurations.Supplier;

public class SupplierBlackoutDateConfiguration : IEntityTypeConfiguration<SupplierBlackoutDate>
{
    public void Configure(EntityTypeBuilder<SupplierBlackoutDate> builder)
    {
        builder.ToTable("supplier_blackout_dates");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Reason).HasMaxLength(255).IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.SupplierId, x.StartDate, x.EndDate });
    }
}
