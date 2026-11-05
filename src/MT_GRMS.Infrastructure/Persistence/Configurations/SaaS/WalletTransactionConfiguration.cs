using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.SaaS;

namespace MT_GRMS.Infrastructure.Persistence.Configurations.SaaS;

public class WalletTransactionConfiguration : IEntityTypeConfiguration<WalletTransaction>
{
    public void Configure(EntityTypeBuilder<WalletTransaction> builder)
    {
        builder.ToTable("WalletTransactions");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(50);

        builder.Property(x => x.TenantId).HasMaxLength(50).IsRequired();
        builder.Property(x => x.TransactionCode).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Type).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Amount).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.BalanceBefore).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.BalanceAfter).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(255).IsRequired();
        builder.Property(x => x.PaymentMethod).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(30).HasDefaultValue("SUCCESS");
        builder.Property(x => x.ReferenceCode).HasMaxLength(100);
        builder.Property(x => x.Metadata).HasMaxLength(1000);

        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => x.TransactionCode).IsUnique();
        builder.HasIndex(x => x.ReferenceCode);

        builder.HasOne(x => x.Tenant)
            .WithMany(t => t.WalletTransactions)
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

