using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.HR;

namespace MT_GRMS.Infrastructure.Persistence.Configurations.HR;

public class EmployeeBonusViolationConfiguration : IEntityTypeConfiguration<EmployeeBonusViolation>
{
    public void Configure(EntityTypeBuilder<EmployeeBonusViolation> builder)
    {
        builder.ToTable("EmployeeBonusViolations");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("BonusViolationID");

        builder.Property(x => x.TenantId).HasColumnName("TenantID").HasMaxLength(50).IsRequired();
        builder.Property(x => x.EmployeeId).HasColumnName("EmployeeID").IsRequired();
        builder.Property(x => x.PayslipId).HasColumnName("PayslipID");
        builder.Property(x => x.Type).HasMaxLength(30).IsRequired();
        builder.Property(x => x.Amount).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.Reason).HasMaxLength(255).IsRequired();
        builder.Property(x => x.EvidenceUrl).HasMaxLength(255);

        builder.HasOne(x => x.Employee)
            .WithMany(e => e.BonusViolations)
            .HasForeignKey(x => x.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Payslip)
            .WithMany(p => p.BonusViolations)
            .HasForeignKey(x => x.PayslipId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
