using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.Finance;

namespace MT_GRMS.Infrastructure.Persistence.Configurations.Finance;

public class PaymentTransactionConfiguration : IEntityTypeConfiguration<PaymentTransaction>
{
    public void Configure(EntityTypeBuilder<PaymentTransaction> builder)
    {
        builder.ToTable("PaymentTransactions");
        builder.Property(x => x.BankTransactionId).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Gateway).HasMaxLength(50).IsRequired();
        builder.Property(x => x.AmountIn).HasColumnType("decimal(18,2)");
        builder.Property(x => x.Accumulated).HasColumnType("decimal(18,2)");
        builder.Property(x => x.ReferenceNumber).HasMaxLength(100);
        builder.HasIndex(x => new { x.TenantId, x.BankTransactionId }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.BranchId, x.TransactionDate });
        builder.HasOne(x => x.FinanceAccount).WithMany().HasForeignKey(x => x.FinanceAccountId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.TenantId, x.FinanceAccountId, x.TransactionDate });
    }
}
