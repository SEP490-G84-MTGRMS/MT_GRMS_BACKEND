using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.Finance;

namespace MT_GRMS.Infrastructure.Persistence.Configurations.Finance;

public class CustomerDebtProfileConfiguration : IEntityTypeConfiguration<CustomerDebtProfile>
{
    public void Configure(EntityTypeBuilder<CustomerDebtProfile> builder)
    {
        builder.ToTable("CustomerDebtProfiles");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.CreditLimit).HasColumnType("decimal(18,2)");
        builder.Property(e => e.CurrentDebtBalance).HasColumnType("decimal(18,2)");
        builder.Property(e => e.PrepaymentBalance).HasColumnType("decimal(18,2)");
        builder.Property(e => e.RowVersion).IsRowVersion();

        builder.HasIndex(e => new { e.TenantId, e.CustomerId }).IsUnique();
        builder.HasIndex(e => new { e.TenantId, e.DebtStatus });
    }
}

public class CustomerDebtInvoiceConfiguration : IEntityTypeConfiguration<CustomerDebtInvoice>
{
    public void Configure(EntityTypeBuilder<CustomerDebtInvoice> builder)
    {
        builder.ToTable("CustomerDebtInvoices");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.OrderCode).HasMaxLength(64).IsRequired();
        builder.Property(e => e.TotalDebtAmount).HasColumnType("decimal(18,2)");
        builder.Property(e => e.RemainingDebtAmount).HasColumnType("decimal(18,2)");

        builder.HasIndex(e => new { e.TenantId, e.CustomerId, e.Status, e.DueDate });
        builder.HasIndex(e => new { e.TenantId, e.OrderId });
    }
}

public class CustomerDebtEntryConfiguration : IEntityTypeConfiguration<CustomerDebtEntry>
{
    public void Configure(EntityTypeBuilder<CustomerDebtEntry> builder)
    {
        builder.ToTable("CustomerDebtEntries");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.EntryCode).HasMaxLength(64).IsRequired();
        builder.Property(e => e.Amount).HasColumnType("decimal(18,2)");
        builder.Property(e => e.BalanceAfter).HasColumnType("decimal(18,2)");
        builder.Property(e => e.PrepaymentAfter).HasColumnType("decimal(18,2)");
        builder.Property(e => e.ReferenceCode).HasMaxLength(64);
        builder.Property(e => e.Description).HasMaxLength(500);

        builder.HasIndex(e => new { e.TenantId, e.CustomerId, e.OccurredAtUtc });
        builder.HasIndex(e => new { e.TenantId, e.EntryCode }).IsUnique();
    }
}

public class CustomerDebtAllocationConfiguration : IEntityTypeConfiguration<CustomerDebtAllocation>
{
    public void Configure(EntityTypeBuilder<CustomerDebtAllocation> builder)
    {
        builder.ToTable("CustomerDebtAllocations");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.AllocatedAmount).HasColumnType("decimal(18,2)");

        builder.HasIndex(e => new { e.TenantId, e.DebtEntryId });
        builder.HasIndex(e => new { e.TenantId, e.DebtInvoiceId });
    }
}
