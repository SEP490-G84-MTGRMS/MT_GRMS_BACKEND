using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using MT_GRMS.Domain.Common;

namespace MT_GRMS.Domain.Entities.Catalog;

/// <summary>
/// Bảng ProductUnits — Thực thể Yếu (Weak Entity) trong mô hình ERD.
/// <para>
/// - Phụ thuộc nhận dạng hoàn toàn vào Thực thể Mạnh (Strong Entity / Aggregate Root) <see cref="Product"/> thông qua Identifying Relationship.
/// - Khóa ngoại xác định: <see cref="ProductId"/> (bắt buộc, không thể tồn tại độc lập).
/// - Khóa bộ phận (Partial Key / Discriminator): <see cref="UnitName"/> phân biệt các đơn vị tính trong phạm vi của một Product.
/// - Vòng đời (Life-cycle): Gắn liền với Product, chịu sự ràng buộc xóa tầng (Cascade Delete).
/// </para>
/// </summary>
public class ProductUnit : BaseEntity
{
    /// <summary>
    /// Khóa ngoại xác định (Identifying Foreign Key) liên kết đến Thực thể Mạnh Product.
    /// Không cho phép giá trị rỗng/null vì thực thể yếu không thể tồn tại độc lập.
    /// </summary>
    [Required]
    [ForeignKey(nameof(Product))]
    public Guid ProductId { get; set; }

    /// <summary>
    /// Khóa bộ phận (Partial Key / Discriminator) trong ERD để phân biệt các đơn vị quy đổi của sản phẩm.
    /// Bộ thuộc tính (ProductId, UnitName) là duy nhất trong phạm vi từng sản phẩm.
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string UnitName { get; set; } = string.Empty;

    public decimal ConversionRate { get; set; } = 1.00m;
    public decimal Price { get; set; } = 0.00m;
    public string? Barcode { get; set; }
    public bool IsBaseUnit { get; set; } = false;

    // Backward compatibility & ubiquitous language aliases
    [NotMapped]
    public decimal SellingPrice { get => Price; set => Price = value; }

    /// <summary>
    /// Thực thể Mạnh chủ quản (Owner Entity).
    /// Bắt buộc non-nullable (Total Participation) vì ProductUnit không bao giờ có trạng thái mồ côi.
    /// </summary>
    public Product Product { get; set; } = null!;

    /// <summary>
    /// Constructor mặc định cho EF Core và Object Initializer.
    /// </summary>
    public ProductUnit() { }

    /// <summary>
    /// Constructor tiện ích để khởi tạo đơn vị tính quy đổi phụ thuộc vào Product.
    /// </summary>
    public ProductUnit(
        Guid productId,
        string unitName,
        decimal conversionRate,
        decimal price,
        string? barcode = null,
        bool isBaseUnit = false)
    {
        if (productId == Guid.Empty)
            throw new ArgumentException("ProductId không được để trống vì ProductUnit là thực thể yếu phụ thuộc vào Product.", nameof(productId));

        ProductId = productId;
        UnitName = string.IsNullOrWhiteSpace(unitName)
            ? throw new ArgumentException("UnitName không được để trống.", nameof(unitName))
            : unitName.Trim();
        ConversionRate = isBaseUnit ? 1.00m : (conversionRate <= 0 ? 1.00m : conversionRate);
        Price = price < 0 ? 0.00m : price;
        Barcode = barcode;
        IsBaseUnit = isBaseUnit;
    }
}
