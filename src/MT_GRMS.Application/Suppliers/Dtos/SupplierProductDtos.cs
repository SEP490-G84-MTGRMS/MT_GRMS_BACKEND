using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Application.Suppliers.Dtos;

public class SupplierProductDto
{
    public Guid Id { get; set; }
    public Guid SupplierId { get; set; }
    public string SupplierCode { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;

    public Guid ProductId { get; set; }
    public string Barcode { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string? CategoryName { get; set; }
    public string BaseUoM { get; set; } = "Cái";

    public string? SupplierProductCode { get; set; }
    public string DeliveryUoM { get; set; } = "Cái";
    /// <summary>NULL = chưa thỏa thuận.</summary>
    public decimal? AgreedWholesalePrice { get; set; }
    public decimal? CostPrice { get; set; }
    public decimal EffectivePurchasePrice { get; set; }
    public int MinimumOrderQuantity { get; set; } = 1;
    public int AllowedShortageQuantity { get; set; } = 0;
    public string? ImageUrl { get; set; }
    public bool IsPrimarySupplier { get; set; }
    public string Status { get; set; } = "Active";
    public int SyncedBranchesCount { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class AssignSupplierProductCommand
{
    public Guid ProductId { get; set; }
    public string DeliveryUoM { get; set; } = "Cái";
    /// <summary>NULL hoặc 0 = chưa thỏa thuận.</summary>
    public decimal? AgreedWholesalePrice { get; set; }
    public int MinimumOrderQuantity { get; set; } = 1;
    public int AllowedShortageQuantity { get; set; } = 0;
    public bool IsPrimarySupplier { get; set; } = false;
    public string? SupplierProductCode { get; set; }
}

public class BatchAssignSupplierProductItemCommand
{
    public Guid ProductId { get; set; }
    public decimal? AgreedWholesalePrice { get; set; }
    public int MinimumOrderQuantity { get; set; } = 1;
    public int AllowedShortageQuantity { get; set; } = 0;
}

public class BatchAssignSupplierProductsCommand
{
    public List<Guid> ProductIds { get; set; } = new();
    public List<BatchAssignSupplierProductItemCommand> Items { get; set; } = new();
    public string? DeliveryUoM { get; set; }
    public decimal? AgreedWholesalePrice { get; set; }
    public int MinimumOrderQuantity { get; set; } = 1;
    public int AllowedShortageQuantity { get; set; } = 0;
    public string? SupplierProductCode { get; set; }
}

public class BatchAssignSupplierProductsResultDto
{
    public int AssignedCount { get; set; }
    public int SyncedBranchesCount { get; set; }
    public List<SupplierProductDto> Items { get; set; } = new();
}

public class UpdateSupplierProductCommand
{
    public string DeliveryUoM { get; set; } = "Cái";
    public decimal? AgreedWholesalePrice { get; set; }
    public int MinimumOrderQuantity { get; set; } = 1;
    public int AllowedShortageQuantity { get; set; } = 0;
    public bool IsPrimarySupplier { get; set; } = false;
    public string? SupplierProductCode { get; set; }
}

public class ToggleSupplierProductStatusRequest
{
    public SupplierProductStatus Status { get; set; } = SupplierProductStatus.Active;
}

public class SetPrimarySupplierRequest
{
    public bool IsPrimarySupplier { get; set; } = true;
}

public class ProductSupplierMappingDto
{
    public Guid SupplierId { get; set; }
    public string SupplierCode { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public string? SupplierProductCode { get; set; }
    public string DeliveryUoM { get; set; } = "Cái";
    public decimal? AgreedWholesalePrice { get; set; }
    public int MinimumOrderQuantity { get; set; } = 1;
    public int AllowedShortageQuantity { get; set; } = 0;
    public bool IsPrimarySupplier { get; set; }
    public string Status { get; set; } = "Active";
}

public class SupplierProductQueryFilter
{
    public string? SearchTerm { get; set; }
    public string? SupplierSearch { get; set; }
    public List<string>? Skus { get; set; }
    public List<string>? Names { get; set; }
    public List<string>? SupplierCodes { get; set; }
    public List<string>? SupplierNames { get; set; }
    public string? CategoryName { get; set; }
    public Guid? CategoryId { get; set; }
    public SupplierProductStatus? Status { get; set; }
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

/// <summary>Kết quả import Excel giá nhập sỉ + MOQ theo từng dòng.</summary>
public class SupplierProductExcelImportRowResultDto
{
    public int Row { get; set; }
    public string? ProductId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public decimal? AgreedWholesalePrice { get; set; }
    public int? Moq { get; set; }
    /// <summary>OK | ERROR</summary>
    public string Status { get; set; } = "ERROR";
    public string Message { get; set; } = string.Empty;
}

public class SupplierProductExcelImportSummaryDto
{
    public int TotalRows { get; set; }
    public int SuccessCount { get; set; }
    public int FailedCount { get; set; }
}

public class SupplierProductExcelImportResultDto
{
    public string FileName { get; set; } = string.Empty;
    public Guid SupplierId { get; set; }
    public string SupplierCode { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public SupplierProductExcelImportSummaryDto Summary { get; set; } = new();
    public List<SupplierProductExcelImportRowResultDto> Rows { get; set; } = new();
}

public class SupplierProductExcelFileDto
{
    public byte[] Content { get; set; } = Array.Empty<byte>();
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
}
