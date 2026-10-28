using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.Sales;

namespace MT_GRMS.Infrastructure.Persistence.Configurations.Sales;

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("Customers");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("CustomerID");

        builder.Property(x => x.TenantId).HasColumnName("TenantID").HasMaxLength(50).IsRequired();
        builder.Property(x => x.CustomerCode).HasMaxLength(30).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Phone).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Email).HasMaxLength(100);
        builder.Property(x => x.Address).HasMaxLength(255);
        builder.Property(x => x.MembershipTier).HasMaxLength(30).HasDefaultValue("Standard");
        builder.Property(x => x.RewardPoints).HasDefaultValue(0);
        builder.Property(x => x.TotalSpending).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.TotalDebt).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.DebtLimit).HasColumnType("decimal(18,2)").HasDefaultValue(5000000.00m);
        builder.Property(x => x.IsBlockedForDebt).HasDefaultValue(false);
        builder.Property(x => x.AllowDebt).HasDefaultValue(false);

        builder.HasIndex(x => new { x.TenantId, x.CustomerCode }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.Phone }).IsUnique();
    }
}
