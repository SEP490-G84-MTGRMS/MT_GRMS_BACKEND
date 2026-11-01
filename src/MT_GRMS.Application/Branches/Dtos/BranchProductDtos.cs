using System;
using System.Collections.Generic;

namespace MT_GRMS.Application.Branches.Dtos;

/// <summary>
/// DTO đại diện cho một sản phẩm kinh doanh tại một Chi nhánh / Cửa hàng
/// </summary>
public class BranchProductDto
{
    public Guid Id { get; set; }
    public Guid BranchId { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public Guid ProductId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public string Barcode { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal RetailPrice { get; set; }
    public decimal CostPrice { get; set; }
    public decimal CurrentStock { get; set; }
    public decimal MinStockLevel { get; set; }
    public string PrimarySupplierName { get; set; } = string.Empty;
    public string Status { get; set; } = "Active";
    public string? ShelfLocation { get; set; }
    public DateTime AssignedDate { get; set; }
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
    public string? SearchTerm { get; set; }
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
    public string? ShelfLocation { get; set; }
}

/// <summary>
/// Lệnh cập nhật giá bán lẻ niêm yết, mức tồn an toàn và vị trí kệ hàng tại chi nhánh
/// </summary>
public class UpdateBranchProductCommand
{
    public decimal RetailPrice { get; set; }
    public decimal MinStockLevel { get; set; }
    public string? ShelfLocation { get; set; }
}

/// <summary>
/// Yêu cầu chuyển đổi trạng thái Active / Inactive của sản phẩm tại chi nhánh
/// </summary>
public class ToggleBranchProductStatusRequest
{
    public string Status { get; set; } = "Active";
}
