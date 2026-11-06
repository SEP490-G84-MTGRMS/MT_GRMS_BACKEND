using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.HR;
using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Infrastructure.Persistence.Configurations.HR;

public class PayslipConfiguration : IEntityTypeConfiguration<Payslip>
{
    public void Configure(EntityTypeBuilder<Payslip> builder)
    {
        builder.ToTable("Payslips");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("PayslipID");

        builder.Property(x => x.EmployeeId).HasColumnName("EmployeeID").IsRequired();
        builder.Property(x => x.BranchId).HasColumnName("BranchID").IsRequired();
        builder.Property(x => x.TenantId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.PayslipCode).HasMaxLength(50).IsRequired();
        builder.Property(x => x.PeriodMonth).HasMaxLength(7).IsRequired();

        // Cấu hình lương
        builder.Property(x => x.SalaryType)
            .HasConversion(
                v => v.ToString().ToUpper(),
                v => Enum.Parse<SalaryType>(v, true))
            .HasMaxLength(30)
            .HasDefaultValue(SalaryType.Monthly);

        builder.Property(x => x.BaseRate).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.StandardDays).HasColumnType("decimal(5,2)").HasDefaultValue(26.00m);
        builder.Property(x => x.StandardDayHours).HasColumnType("decimal(5,2)").HasDefaultValue(8.00m);

        // Chấm công
        builder.Property(x => x.WorkedDays).HasColumnType("decimal(5,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.TotalWorkHours).HasColumnType("decimal(7,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.TotalShifts).HasDefaultValue(0);
        builder.Property(x => x.OtHours).HasColumnType("decimal(5,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.PaidLeaveDays).HasColumnType("decimal(5,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.UnpaidLeaveDays).HasColumnType("decimal(5,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.TotalLateMinutes).HasDefaultValue(0);
        builder.Property(x => x.TotalEarlyLeaveMinutes).HasDefaultValue(0);

        // Thu nhập
        builder.Property(x => x.BaseSalaryEarned).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.OvertimePay).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.TotalAllowance).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.AllowancesDetailJson).HasColumnType("text");
        builder.Property(x => x.Bonus).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.TotalEarnings).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);

        // Giảm trừ
        builder.Property(x => x.AdvanceDeduction).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.RemainingDebt).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.SocialInsurance).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.ViolationDeduction).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.TotalDeductions).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);

        // Thực nhận & trạng thái
        builder.Property(x => x.NetSalary).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.Status)
            .HasConversion(
                v => v.ToString().ToUpper(),
                v => Enum.Parse<PayslipStatus>(v, true))
            .HasMaxLength(30)
            .HasDefaultValue(PayslipStatus.Unpaid);

        // Thanh toán & sổ quỹ
        builder.Property(x => x.PaymentMethod).HasMaxLength(30);
        builder.Property(x => x.PaidBy).HasMaxLength(100);
        builder.Property(x => x.BankAccountNumber).HasMaxLength(50);
        builder.Property(x => x.BankName).HasMaxLength(100);
        builder.Property(x => x.BankAccountHolder).HasMaxLength(100);
        builder.Property(x => x.CashbookVoucherCode).HasMaxLength(50);

        // Phê duyệt & ghi chú
        builder.Property(x => x.ApprovedBy).HasMaxLength(100);
        builder.Property(x => x.Note).HasMaxLength(500);

        // Indexes
        builder.HasIndex(x => x.PayslipCode).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.BranchId, x.PeriodMonth });

        // Relationships
        builder.HasOne(x => x.Employee)
            .WithMany(e => e.Payslips)
            .HasForeignKey(x => x.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Branch)
            .WithMany()
            .HasForeignKey(x => x.BranchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.CashbookTransaction)
            .WithMany()
            .HasForeignKey(x => x.CashbookTransactionId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
