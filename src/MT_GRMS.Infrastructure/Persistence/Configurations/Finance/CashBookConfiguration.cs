using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.Finance;

namespace MT_GRMS.Infrastructure.Persistence.Configurations.Finance;

public class CashBookConfiguration : IEntityTypeConfiguration<CashBook>
{
    public void Configure(EntityTypeBuilder<CashBook> builder)
    {
        builder.ToTable("CashBooks");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("CashBookID");

        builder.Property(x => x.BranchId).HasColumnName("BranchID").IsRequired();
        builder.Property(x => x.TransactionCode).HasMaxLength(50).IsRequired();
        builder.Property(x => x.SourceDocType).HasMaxLength(50).IsRequired();
        builder.Property(x => x.SourceDocId).HasColumnName("SourceDocID").HasMaxLength(50);
        builder.Property(x => x.TransactionType).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Amount).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.OpeningBalance).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.CurrentBalance).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);
        builder.Property(x => x.Description).HasMaxLength(255);

        builder.HasIndex(x => x.TransactionCode).IsUnique();

        builder.HasOne(x => x.Branch)
            .WithMany()
            .HasForeignKey(x => x.BranchId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
