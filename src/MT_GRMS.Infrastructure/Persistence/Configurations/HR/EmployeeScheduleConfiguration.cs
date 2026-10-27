using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.HR;

namespace MT_GRMS.Infrastructure.Persistence.Configurations.HR;

public class EmployeeScheduleConfiguration : IEntityTypeConfiguration<EmployeeSchedule>
{
    public void Configure(EntityTypeBuilder<EmployeeSchedule> builder)
    {
        builder.ToTable("EmployeeSchedules");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("ScheduleID");

        builder.Property(x => x.EmployeeId).HasColumnName("EmployeeID").IsRequired();
        builder.Property(x => x.PayslipId).HasColumnName("PayslipID");
        builder.Property(x => x.ShiftCode).HasMaxLength(30).IsRequired();
        builder.Property(x => x.ShiftName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.DurationHours).HasColumnType("decimal(4,2)").HasDefaultValue(8.00m);
        builder.Property(x => x.CheckInLatitude).HasColumnType("decimal(10,7)");
        builder.Property(x => x.CheckInLongitude).HasColumnType("decimal(10,7)");
        builder.Property(x => x.IsGpsValid).HasDefaultValue(true);
        builder.Property(x => x.LateMinutes).HasDefaultValue(0);
        builder.Property(x => x.ActualWorkHours).HasColumnType("decimal(4,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.Status).HasMaxLength(30).HasDefaultValue("scheduled");
        builder.Property(x => x.ApprovalType).HasMaxLength(30);
        builder.Property(x => x.ApprovalStatus).HasMaxLength(30).HasDefaultValue("NONE");
        builder.Property(x => x.ApprovalReason).HasMaxLength(255);
        builder.Property(x => x.EvidenceImageUrl).HasMaxLength(255);
        builder.Property(x => x.ApprovedBy).HasMaxLength(100);

        builder.HasOne(x => x.Employee)
            .WithMany(e => e.Schedules)
            .HasForeignKey(x => x.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Payslip)
            .WithMany(p => p.Schedules)
            .HasForeignKey(x => x.PayslipId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
