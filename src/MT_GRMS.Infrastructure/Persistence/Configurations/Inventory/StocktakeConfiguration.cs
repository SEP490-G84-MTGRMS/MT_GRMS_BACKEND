using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.Inventory;

namespace MT_GRMS.Infrastructure.Persistence.Configurations.Inventory;

public class StocktakeConfiguration : IEntityTypeConfiguration<Stocktake>
{
    public void Configure(EntityTypeBuilder<Stocktake> builder)
    {
        builder.ToTable("Stocktakes");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Code).HasMaxLength(50).IsRequired();
        builder.Property(x => x.BranchId).IsRequired();
        builder.Property(x => x.Scope).HasMaxLength(50).HasDefaultValue("LOT");
        builder.Property(x => x.ReasonType).HasMaxLength(50).HasDefaultValue("PERIODIC");
        builder.Property(x => x.Status).HasMaxLength(50).HasDefaultValue("DRAFT");
        builder.Property(x => x.CreatedByName).HasMaxLength(150);
        builder.Property(x => x.ReviewedByName).HasMaxLength(150);
        builder.Property(x => x.ReviewNote).HasMaxLength(1000);
        builder.Property(x => x.Note).HasMaxLength(1000);

        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.BranchId, x.Status });

        builder.HasOne(x => x.Branch)
            .WithMany()
            .HasForeignKey(x => x.BranchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Lines)
            .WithOne(l => l.Stocktake)
            .HasForeignKey(l => l.StocktakeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class StocktakeLineConfiguration : IEntityTypeConfiguration<StocktakeLine>
{
    public void Configure(EntityTypeBuilder<StocktakeLine> builder)
    {
        builder.ToTable("StocktakeLines");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).HasMaxLength(50).IsRequired();
        builder.Property(x => x.StocktakeId).IsRequired();
        builder.Property(x => x.ProductId).IsRequired();
        builder.Property(x => x.BatchCode).HasMaxLength(100);
        builder.Property(x => x.UnitCost).HasColumnType("decimal(18,2)").HasDefaultValue(0.00m);

        builder.Property(x => x.SystemQty).HasColumnType("decimal(18,3)").HasDefaultValue(0.000m);
        builder.Property(x => x.GoodQty).HasColumnType("decimal(18,3)").HasDefaultValue(0.000m);
        builder.Property(x => x.DefectiveQty).HasColumnType("decimal(18,3)").HasDefaultValue(0.000m);
        builder.Property(x => x.DamagedQty).HasColumnType("decimal(18,3)").HasDefaultValue(0.000m);
        builder.Property(x => x.NearExpiryQty).HasColumnType("decimal(18,3)").HasDefaultValue(0.000m);
        builder.Property(x => x.ExpiredQty).HasColumnType("decimal(18,3)").HasDefaultValue(0.000m);
        builder.Property(x => x.ActualQty).HasColumnType("decimal(18,3)").HasDefaultValue(0.000m);
        builder.Property(x => x.VarianceQty).HasColumnType("decimal(18,3)").HasDefaultValue(0.000m);
        builder.Property(x => x.ReturnedQty).HasColumnType("decimal(18,3)").HasDefaultValue(0.000m);

        builder.Property(x => x.DefectiveReasonCode).HasMaxLength(50);
        builder.Property(x => x.NearExpiryAction).HasMaxLength(50);
        builder.Property(x => x.OriginalGrnCode).HasMaxLength(50);
        builder.Property(x => x.EvidenceUrl).HasMaxLength(1000);
        builder.Property(x => x.Note).HasMaxLength(1000);

        builder.HasIndex(x => x.StocktakeId);
        builder.HasIndex(x => x.BranchStockId);
        builder.HasIndex(x => x.ProductId);

        builder.HasOne(x => x.Product)
            .WithMany()
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.BranchStock)
            .WithMany()
            .HasForeignKey(x => x.BranchStockId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

