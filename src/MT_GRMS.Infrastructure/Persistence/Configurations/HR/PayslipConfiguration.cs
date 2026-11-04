using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.HR;

namespace MT_GRMS.Infrastructure.Persistence.Configurations.HR;

public class PayslipConfiguration : IEntityTypeConfiguration<Payslip>
{
    public void Configure(EntityTypeBuilder<Payslip> builder)
    {
        builder.ToTable("Payslips");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("PayslipID");

        builder.Property(x => x.EmployeeId).HasColumnName("EmployeeID").IsRequired();
        builder.Property(x => x.PayslipCode).HasMaxLength(50).IsRequired();
        builder.Property(x => x.PeriodMonth).HasMaxLength(7).IsRequired();
        builder.Property(x => x.StandardDays).HasColumnType("decimal(5,2)").HasDefaultValue(26.00m);
        builder.Property(x => x.WorkedDays).HasColumnType("decimal(5,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.OtHours).HasColumnType("decimal(5,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.ContractBaseSalary).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.BaseSalaryEarned).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.OvertimePay).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.LunchAllowance).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.TravelAllowance).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.Bonus).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.TotalEarnings).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.AdvanceDeduction).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.SocialInsurance).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.ViolationDeduction).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.TotalDeductions).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.NetSalary).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.Status).HasMaxLength(30).HasDefaultValue("UNPAID");
        builder.Property(x => x.PaymentMethod).HasMaxLength(30);

        builder.HasIndex(x => x.PayslipCode).IsUnique();

        builder.HasOne(x => x.Employee)
            .WithMany(e => e.Payslips)
            .HasForeignKey(x => x.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
