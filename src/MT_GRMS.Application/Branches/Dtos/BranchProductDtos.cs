using System;
using System.Collections.Generic;

namespace MT_GRMS.Application.Branches.Dtos;

/// <summary>
/// DTO đại diện cho một sản phẩm kinh doanh tại một Chi nhánh / Cửa hàng
/// </summary>
public class BranchProductUnitPriceDto
{
    public Guid? UnitId { get; set; }
    public string UnitName { get; set; } = string.Empty;
    public decimal ConversionRate { get; set; } = 1m;
    public decimal Price { get; set; }
    public bool IsBaseUnit { get; set; }
}

public class BranchProductDto
{
    public Guid Id { get; set; }
    public Guid BranchId { get; set; }
    public string BranchCode { get; set; } = string.Empty;
    public string BranchName { get; set; } = string.Empty;
    public Guid ProductId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public string Barcode { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    /// <summary>ĐVT nhập (DeliveryUoM / purchase) — dùng hiển thị tồn giống màn Lô.</summary>
    public string Unit { get; set; } = string.Empty;
    /// <summary>ĐVT tồn gốc (Product.BaseUnit) — QuantityOnHand / CurrentStock theo đơn vị này.</summary>
    public string StockUnit { get; set; } = string.Empty;
    /// <summary>1 ĐVT nhập = bao nhiêu ĐVT tồn (base). Mặc định 1.</summary>
    public decimal PurchaseToStockRate { get; set; } = 1m;
    public string? ImageUrl { get; set; }
    public decimal RetailPrice { get; set; }
    public decimal CostPrice { get; set; }
    /// <summary>Tồn khả dụng bán (RULE-01) theo ĐVT tồn gốc — chỉ cộng lô còn hạn.</summary>
    public decimal CurrentStock { get; set; }
    public decimal MinStockLevel { get; set; }
    public Guid? SupplierId { get; set; }
    public string PrimarySupplierCode { get; set; } = string.Empty;
    public string PrimarySupplierName { get; set; } = string.Empty;
    /// <summary>NCC chính (tenant-wide) trên quan hệ SupplierProduct — chỉ để badge/gợi ý.</summary>
    public bool IsPrimarySupplier { get; set; }
    /// <summary>true = còn ít nhất 1 NCC Active phục vụ CN cho SP này.</summary>
    public bool HasActiveSupplierSource { get; set; } = true;
    /// <summary>true = SP đang bán tại CN nhưng mất nguồn NCC (không còn assignment Active).</summary>
    public bool LostSupplierSource { get; set; }
    public int MinOrderQuantity { get; set; } = 1;
    public string Status { get; set; } = "Active";
    public string? ShelfLocation { get; set; }
    public DateTime AssignedDate { get; set; }
    public List<BranchProductUnitPriceDto> UnitPrices { get; set; } = new();
    /// <summary>Master AllowRepack — bật giá đóng khay (PROCESSED đ/kg).</summary>
    public bool AllowRepack { get; set; }
    /// <summary>Giá đ/kg khi đóng khay. Null/≤0 → dùng RetailPrice (RAW).</summary>
    public decimal? ProcessedSellingPricePerKg { get; set; }
}

/// <summary>
/// DTO tổng hợp chỉ số của từng Chi nhánh (Tổng mặt hàng, Tổng tồn kho SOH, Cảnh báo thiếu hàng)
/// </summary>
public class BranchSummaryDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string? ManagerName { get; set; }
    public string? Phone { get; set; }
    public int TotalProducts { get; set; }
    public decimal TotalStock { get; set; }
    public int LowStockCount { get; set; }
}

/// <summary>
/// Bộ lọc truy vấn danh sách sản phẩm theo chi nhánh có phân trang
/// </summary>
public class BranchProductQueryFilter
{
    public bool PreviewOnly { get; set; }
    public string? SearchTerm { get; set; }
    public string? BranchCode { get; set; }
    public List<string>? Skus { get; set; }
    public List<string>? Names { get; set; }
    public List<string>? BranchCodes { get; set; }
    public List<string>? BranchNames { get; set; }
    public List<string>? SupplierCodes { get; set; }
    public List<string>? SupplierNames { get; set; }
    public string? Category { get; set; }
    public string? Status { get; set; }
    public string? StockAlertFilter { get; set; } // "ALL", "LOW", "OUT_OF_STOCK", "NORMAL"
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

/// <summary>
/// Lệnh gán một hoặc nhiều sản phẩm từ Master Catalog vào Chi nhánh
/// </summary>
public class AssignBranchProductsCommand
{
    public List<Guid> ProductIds { get; set; } = new();
    public decimal? DefaultRetailPrice { get; set; }
    public decimal? DefaultMinStock { get; set; }
    public int? DefaultMinOrderQuantity { get; set; }
    public string? ShelfLocation { get; set; }
}

/// <summary>
/// Lệnh cập nhật giá bán lẻ niêm yết, mức tồn an toàn, MOQ, trạng thái và vị trí kệ hàng tại chi nhánh
/// </summary>
public class UpdateBranchProductCommand
{
    public decimal RetailPrice { get; set; }
    public decimal? MinStockLevel { get; set; }
    /// <summary>Bỏ qua — MOQ chỉ sửa ở màn SupplierProducts.</summary>
    public int? MinOrderQuantity { get; set; }
    public string? Status { get; set; }
    public Guid? SupplierId { get; set; }
    public string? ShelfLocation { get; set; }
    /// <summary>
    /// Giá bán theo từng ĐVT tại CN. Mỗi dòng có UnitId sẽ upsert vào BranchProductUnitPrices
    /// (khác Master → lưu override; trùng Master → xóa override).
    /// </summary>
    public List<BranchProductUnitPriceDto>? UnitPrices { get; set; }
}

/// <summary>
/// Yêu cầu chuyển đổi trạng thái Active / Inactive của sản phẩm tại chi nhánh
/// </summary>
public class ToggleBranchProductStatusRequest
{
    public string Status { get; set; } = "Active";
}

/// <summary>
/// Sản phẩm khả dụng để gán vào chi nhánh: thuộc NCC Active đang liên kết với chi nhánh đó.
/// </summary>
public class AssignableBranchProductDto
{
    public Guid ProductId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Barcode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public string BaseUoM { get; set; } = "Cái";
    public string? ImageUrl { get; set; }
    public decimal CostPrice { get; set; }
    public decimal SellingPrice { get; set; }
    public int Moq { get; set; } = 1;
    public Guid SupplierId { get; set; }
    public string SupplierCode { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public bool IsAlreadyAssigned { get; set; }
    public List<BranchProductUnitPriceDto> UnitPrices { get; set; } = new();
}

/// <summary>Kết quả import Excel giá bán theo ĐVT (từng dòng SP).</summary>
public class BranchProductExcelImportRowResultDto
{
    public int Row { get; set; }
    public string? ProductId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    /// <summary>Tóm tắt giá ĐVT đã áp (vd: Hộp:10.000; Lốc:32.000).</summary>
    public string? UnitPricesSummary { get; set; }
    /// <summary>OK | ERROR</summary>
    public string Status { get; set; } = "ERROR";
    public string Message { get; set; } = string.Empty;
}

public class BranchProductExcelImportSummaryDto
{
    public int TotalRows { get; set; }
    public int SuccessCount { get; set; }
    public int FailedCount { get; set; }
}

public class BranchProductExcelImportResultDto
{
    public bool IsPreview { get; set; }
    public string FileName { get; set; } = string.Empty;
    public Guid BranchId { get; set; }
    public string BranchCode { get; set; } = string.Empty;
    public string BranchName { get; set; } = string.Empty;
    public BranchProductExcelImportSummaryDto Summary { get; set; } = new();
    public List<BranchProductExcelImportRowResultDto> Rows { get; set; } = new();
}

public class BranchProductExcelFileDto
{
    public byte[] Content { get; set; } = Array.Empty<byte>();
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
}
