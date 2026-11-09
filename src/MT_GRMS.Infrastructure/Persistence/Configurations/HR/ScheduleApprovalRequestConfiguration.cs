using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.HR;
using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Infrastructure.Persistence.Configurations.HR;

public class ScheduleApprovalRequestConfiguration : IEntityTypeConfiguration<ScheduleApprovalRequest>
{
    public void Configure(EntityTypeBuilder<ScheduleApprovalRequest> builder)
    {
        builder.ToTable("ScheduleApprovalRequests");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("RequestId");

        builder.Property(x => x.ScheduleId).HasColumnName("ScheduleID").IsRequired();
        builder.Property(x => x.EmployeeId).HasColumnName("EmployeeID").IsRequired();
        builder.Property(x => x.BranchId).HasColumnName("BranchID").IsRequired();
        builder.Property(x => x.TargetEmployeeId).HasColumnName("TargetEmployeeID");
        builder.Property(x => x.TargetScheduleId).HasColumnName("TargetScheduleID");

        builder.Property(x => x.RequestType)
            .HasConversion(
                v => v.ToString(),
                v => Enum.Parse<ScheduleRequestType>(v, true))
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasConversion(
                v => v.ToString(),
                v => Enum.Parse<ScheduleApprovalStatus>(v, true))
            .HasMaxLength(30)
            .HasDefaultValue(ScheduleApprovalStatus.Pending);

        builder.Property(x => x.SwapType)
            .HasConversion(
                v => v.HasValue ? v.Value.ToString() : null,
                v => string.IsNullOrEmpty(v) ? (ShiftSwapType?)null : Enum.Parse<ShiftSwapType>(v, true))
            .HasMaxLength(30);

        builder.Property(x => x.LeaveType)
            .HasConversion(
                v => v.HasValue ? v.Value.ToString() : null,
                v => string.IsNullOrEmpty(v) ? (LeaveType?)null : Enum.Parse<LeaveType>(v, true))
            .HasMaxLength(30);

        builder.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        builder.Property(x => x.EvidenceImageUrl).HasMaxLength(1000);
        builder.Property(x => x.EvidenceImageUrls)
            .HasConversion(
                v => JsonSerializer.Serialize(v ?? new List<string>(), (JsonSerializerOptions?)null),
                v => string.IsNullOrWhiteSpace(v)
                    ? new List<string>()
                    : (JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>()))
            .HasColumnType("text")
            .HasDefaultValueSql("'[]'::text");
        builder.Property(x => x.ReviewNote).HasMaxLength(500);
        builder.Property(x => x.ApprovedBy).HasMaxLength(100);

        builder.HasIndex(x => new { x.TenantId, x.Status });
        builder.HasIndex(x => new { x.TenantId, x.BranchId, x.Status });
        builder.HasIndex(x => new { x.TenantId, x.ScheduleId });

        builder.HasOne(x => x.Schedule)
            .WithMany(s => s.ApprovalRequests)
            .HasForeignKey(x => x.ScheduleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Employee)
            .WithMany()
            .HasForeignKey(x => x.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.TargetEmployee)
            .WithMany()
            .HasForeignKey(x => x.TargetEmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.TargetSchedule)
            .WithMany()
            .HasForeignKey(x => x.TargetScheduleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

