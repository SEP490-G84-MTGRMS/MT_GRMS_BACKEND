using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.Finance;

namespace MT_GRMS.Infrastructure.Persistence.Configurations.Finance;

public class ServiceExpenseConfiguration : IEntityTypeConfiguration<ServiceExpense>
{
    public void Configure(EntityTypeBuilder<ServiceExpense> builder)
    {
        builder.ToTable("ServiceExpenses");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("ExpenseID");

        builder.Property(x => x.TenantId).HasColumnName("TenantID").HasMaxLength(50).IsRequired();
        builder.Property(x => x.BranchId).HasColumnName("BranchID").IsRequired();
        builder.Property(x => x.ExpenseCode).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Category).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Amount).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.VendorName).HasMaxLength(150);
        builder.Property(x => x.Status).HasMaxLength(30).HasDefaultValue("Paid");
        builder.Property(x => x.InvoiceNumber).HasMaxLength(50);
        builder.Property(x => x.Note).HasMaxLength(500);

        builder.HasIndex(x => x.ExpenseCode).IsUnique();

        builder.HasOne(x => x.Branch)
            .WithMany()
            .HasForeignKey(x => x.BranchId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
