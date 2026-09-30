using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MT_GRMS.Domain.Entities.Catalog;

namespace MT_GRMS.Infrastructure.Persistence.Configurations;

/// <summary>
/// KHUNG MẪU RỖNG CẤU HÌNH CODE-FIRST FLUENT API (EF Core):
/// - Định nghĩa cấu trúc bảng Database từ Entity.
/// - Đặt tên bảng, kiểu dữ liệu cột (decimal 18,2 tránh sai số), độ dài chuỗi, Index và Khóa ngoại.
/// </summary>
public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        // 1. Tên bảng Database
        builder.ToTable("products");

        // 2. Khóa chính
        builder.HasKey(x => x.Id);

        // 3. Cấu hình kiểu dữ liệu & ràng buộc cột
        builder.Property(x => x.SKU).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Barcode).HasMaxLength(50);
        builder.Property(x => x.ProductName).HasMaxLength(255).IsRequired();
        builder.Property(x => x.BasePrice).HasColumnType("decimal(18,2)");
        builder.Property(x => x.CostPrice).HasColumnType("decimal(18,2)");
        builder.Property(x => x.UnitName).HasMaxLength(30).HasDefaultValue("Cái");

        // 4. Đánh chỉ mục (Index) hỗ trợ truy vấn nhanh
        builder.HasIndex(x => new { x.TenantId, x.Barcode });
        builder.HasIndex(x => new { x.TenantId, x.SKU });

        // 5. Quan hệ (Relationships / Foreign Keys)
        builder.HasOne(x => x.Category)
            .WithMany(c => c.Products)
            .HasForeignKey(x => x.CategoryId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
