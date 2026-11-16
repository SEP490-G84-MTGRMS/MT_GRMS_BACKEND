using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Infrastructure.Persistence.Configurations.Inventory;

public sealed class TransferRequestConfiguration : IEntityTypeConfiguration<TransferRequest>
{
    public void Configure(EntityTypeBuilder<TransferRequest> builder)
    {
        builder.ToTable("TransferRequests");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.RequestCode).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).HasDefaultValue(TransferRequestStatus.Open);
        builder.Property(x => x.Priority).HasConversion<string>().HasMaxLength(20).HasDefaultValue(TransferPriority.Urgent);
        builder.Property(x => x.Note).HasMaxLength(500);
        builder.Property(x => x.CancelReason).HasMaxLength(255);
        builder.HasIndex(x => new { x.TenantId, x.RequestCode }).IsUnique();
        builder.HasMany(x => x.Transfers).WithOne(x => x.TransferRequest).HasForeignKey(x => x.TransferRequestId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class TransferRequestDetailConfiguration : IEntityTypeConfiguration<TransferRequestDetail>
{
    public void Configure(EntityTypeBuilder<TransferRequestDetail> builder)
    {
        builder.ToTable("TransferRequestDetails");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.RequestedQuantity).HasColumnType("decimal(12,2)");
        builder.Property(x => x.AllocatedQuantity).HasColumnType("decimal(12,2)");
        builder.Property(x => x.ReceivedQuantity).HasColumnType("decimal(12,2)");
        builder.Property(x => x.Unit).HasMaxLength(30).IsRequired();
        builder.HasOne(x => x.TransferRequest).WithMany(x => x.Details).HasForeignKey(x => x.TransferRequestId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.TransferRequestId, x.ProductId }).IsUnique();
    }
}
