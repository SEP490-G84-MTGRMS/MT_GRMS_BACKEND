using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.SaaS;

namespace MT_GRMS.Infrastructure.Persistence.Configurations.SaaS;

public class WalletTopupOrderConfiguration : IEntityTypeConfiguration<WalletTopupOrder>
{
    public void Configure(EntityTypeBuilder<WalletTopupOrder> builder)
    {
        builder.ToTable("WalletTopupOrders");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(50);

        builder.Property(x => x.TenantId).HasMaxLength(50).IsRequired();
        builder.Property(x => x.OrderCode).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Amount).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.TransferContent).HasMaxLength(100).IsRequired();
        builder.Property(x => x.BankCode).HasMaxLength(30).IsRequired();
        builder.Property(x => x.BankName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.AccountNumber).HasMaxLength(50).IsRequired();
        builder.Property(x => x.AccountName).HasMaxLength(150).IsRequired();
        builder.Property(x => x.QrUrl).HasMaxLength(500).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(30).HasDefaultValue("PENDING");
        builder.Property(x => x.ReferenceCode).HasMaxLength(100);

        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => x.OrderCode).IsUnique();
        builder.HasIndex(x => x.Status);

        builder.HasOne(x => x.Tenant)
            .WithMany(t => t.WalletTopupOrders)
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

