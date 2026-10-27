using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.SaaS;

namespace MT_GRMS.Infrastructure.Persistence.Configurations.SaaS;

public class SubscriptionPlanConfiguration : IEntityTypeConfiguration<SubscriptionPlan>
{
    public void Configure(EntityTypeBuilder<SubscriptionPlan> builder)
    {
        builder.ToTable("SubscriptionPlans");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("PlanID").HasMaxLength(50);

        builder.Property(x => x.PlanCode).HasMaxLength(50).IsRequired();
        builder.Property(x => x.PlanName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.PriceMonthly).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.PriceYearly).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.MaxBranches).HasDefaultValue(1);
        builder.Property(x => x.MaxStaff).HasDefaultValue(5);
        builder.Property(x => x.IsActive).HasDefaultValue(true);

        builder.HasIndex(x => x.PlanCode).IsUnique();
    }
}
