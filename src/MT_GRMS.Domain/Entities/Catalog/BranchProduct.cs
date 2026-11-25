using System.Text.Json;
using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Entities.SaaS;

namespace MT_GRMS.Domain.Entities.Catalog;

/// <summary>
/// Catalog kinh doanh tại chi nhánh: gán Product ↔ Branch + override giá bán / trạng thái.
/// Tồn kho theo lô nằm ở <see cref="Inventory.BranchStock"/>.
/// </summary>
public class BranchProduct : BaseTenantEntity
{
    public Guid BranchId { get; set; }
    public Guid ProductId { get; set; }

    /// <summary>NULL = dùng giá Master Product.SellingPrice.</summary>
    public decimal? SellingPriceOverride { get; set; }

    /// <summary>Giá bán đ/kg khi đóng khay riêng của chi nhánh (NULL = dùng Master Product.ProcessedSellingPricePerKg).</summary>
    public decimal? ProcessedSellingPricePerKg { get; set; }

    public string Status { get; set; } = "Active";
    public string? ShelfLocation { get; set; }

    /// <summary>
    /// Danh sách JSON các SupplierId được gán tường minh cho (Branch, Product).
    /// NULL = bản ghi cũ chưa cấu hình (fallback suy diễn động để tương thích ngược).
    /// </summary>
    public string? AllowedSupplierIdsJson { get; set; }

    public Branch? Branch { get; set; }
    public Product? Product { get; set; }
    public ICollection<BranchProductUnitPrice> UnitPriceOverrides { get; set; } = new List<BranchProductUnitPrice>();

    public List<Guid>? GetAllowedSupplierIds() => ParseAllowedSupplierIds(AllowedSupplierIdsJson);

    public static List<Guid>? ParseAllowedSupplierIds(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var raw = JsonSerializer.Deserialize<List<Guid>>(json);
            if (raw == null)
            {
                return null;
            }

            return raw.Where(id => id != Guid.Empty).Distinct().ToList();
        }
        catch
        {
            return null;
        }
    }

    public void SetAllowedSupplierIds(IEnumerable<Guid> supplierIds)
    {
        var normalized = (supplierIds ?? Array.Empty<Guid>())
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();
        AllowedSupplierIdsJson = JsonSerializer.Serialize(normalized);
    }

    public bool AddAllowedSupplierId(Guid supplierId)
    {
        if (supplierId == Guid.Empty)
        {
            return false;
        }

        var current = GetAllowedSupplierIds() ?? new List<Guid>();
        if (current.Contains(supplierId))
        {
            return false;
        }

        current.Add(supplierId);
        AllowedSupplierIdsJson = JsonSerializer.Serialize(current);
        return true;
    }
}
