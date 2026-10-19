using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.Supplier;

namespace MT_GRMS.Infrastructure.Persistence.Configurations.Supplier;

public class SupplierAPLedgerConfiguration : IEntityTypeConfiguration<SupplierAPLedger>
{
    public void Configure(EntityTypeBuilder<SupplierAPLedger> builder)
    {
        builder.ToTable("supplier_ap_ledgers");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.DocType).HasConversion<int>().IsRequired();
        builder.Property(x => x.DocNo).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(500).IsRequired();
        builder.Property(x => x.Amount).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.Direction).IsRequired();
        builder.Property(x => x.BalanceAfter).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.CreatedByName).HasMaxLength(150).IsRequired();
        builder.Property(x => x.IdempotencyKey).HasMaxLength(150);

        builder.HasIndex(x => new { x.TenantId, x.SupplierId, x.TransactionDate });
        builder.HasIndex(x => new { x.TenantId, x.IdempotencyKey })
            .IsUnique()
            .HasFilter("\"IdempotencyKey\" IS NOT NULL");
    }
}
