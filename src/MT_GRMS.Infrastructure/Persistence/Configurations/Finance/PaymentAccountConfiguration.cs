using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.Finance;

namespace MT_GRMS.Infrastructure.Persistence.Configurations.Finance;

public class PaymentAccountConfiguration : IEntityTypeConfiguration<PaymentAccount>
{
    public void Configure(EntityTypeBuilder<PaymentAccount> builder)
    {
        builder.ToTable("PaymentAccounts");
        builder.Property(x => x.BankCode).HasMaxLength(20).IsRequired();
        builder.Property(x => x.BankName).HasMaxLength(150).IsRequired();
        builder.Property(x => x.AccountNumber).HasMaxLength(50).IsRequired();
        builder.Property(x => x.AccountHolderName).HasMaxLength(150).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.BankCode, x.AccountNumber }).IsUnique();
    }
}

public class BranchPaymentAccountConfiguration : IEntityTypeConfiguration<BranchPaymentAccount>
{
    public void Configure(EntityTypeBuilder<BranchPaymentAccount> builder)
    {
        builder.ToTable("BranchPaymentAccounts");
        // Một chi nhánh chỉ có duy nhất một tài khoản nhận chuyển khoản.
        // Một tài khoản vẫn có thể được dùng bởi nhiều chi nhánh.
        builder.HasIndex(x => new { x.TenantId, x.BranchId }).IsUnique();
        builder.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.PaymentAccount).WithMany(x => x.BranchAssignments)
            .HasForeignKey(x => x.PaymentAccountId).OnDelete(DeleteBehavior.Restrict);
    }
}

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
        builder.HasOne(x => x.PaymentAccount).WithMany().HasForeignKey(x => x.PaymentAccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
