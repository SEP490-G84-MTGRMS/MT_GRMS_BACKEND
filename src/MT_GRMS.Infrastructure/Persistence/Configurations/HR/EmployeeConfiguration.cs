using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.HR;
using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Infrastructure.Persistence.Configurations.HR;

public class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> builder)
    {
        builder.ToTable("Employees");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("EmployeeID");

        builder.Property(x => x.TenantId).HasColumnName("TenantID").HasMaxLength(50).IsRequired();
        builder.Property(x => x.BranchId).HasColumnName("BranchID").IsRequired();
        builder.Property(x => x.UserId).HasColumnName("UserID").HasMaxLength(50).IsRequired();
        builder.Property(x => x.EmployeeCode).HasMaxLength(30).IsRequired();
        builder.Property(x => x.CitizenId).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Gender).HasMaxLength(10);
        builder.Property(x => x.JobTitle).HasMaxLength(100);
        builder.Property(x => x.SalaryType).HasMaxLength(30).HasDefaultValue("monthly");
        builder.Property(x => x.BaseRate).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.BankAccountNumber).HasMaxLength(30);
        builder.Property(x => x.BankName).HasMaxLength(100);
        builder.Property(x => x.BankAccountHolder).HasMaxLength(100);
        builder.Property(x => x.Allowances)
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<EmployeeAllowance>>(v, (JsonSerializerOptions?)null) ?? new List<EmployeeAllowance>()
            );
        builder.Property(x => x.DebtAmount).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.Status)
            .HasConversion(
                v => v.ToString().ToLower(),
                v => Enum.Parse<EmployeeStatus>(v, true))
            .HasMaxLength(30)
            .HasDefaultValue(EmployeeStatus.Active);

        builder.HasIndex(x => new { x.TenantId, x.CitizenId }).IsUnique().HasFilter("\"IsDeleted\" = false");
        builder.HasIndex(x => new { x.TenantId, x.EmployeeCode }).IsUnique().HasFilter("\"IsDeleted\" = false");
        builder.HasIndex(x => new { x.TenantId, x.UserId }).IsUnique().HasFilter("\"IsDeleted\" = false");

        builder.HasOne(x => x.Branch)
            .WithMany()
            .HasForeignKey(x => x.BranchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
