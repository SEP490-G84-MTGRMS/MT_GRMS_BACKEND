using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.Inventory;

namespace MT_GRMS.Infrastructure.Persistence.Configurations.Inventory;

public sealed class TransferStatusHistoryConfiguration : IEntityTypeConfiguration<TransferStatusHistory>
{
    public void Configure(EntityTypeBuilder<TransferStatusHistory> builder)
    {
        builder.ToTable("TransferStatusHistories");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.FromStatus).HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.ToStatus).HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.ActorRole).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Action).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(500);
        builder.HasOne(x => x.Transfer).WithMany(x => x.StatusHistory).HasForeignKey(x => x.TransferId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.TransferId, x.CreatedAt });
    }
}
