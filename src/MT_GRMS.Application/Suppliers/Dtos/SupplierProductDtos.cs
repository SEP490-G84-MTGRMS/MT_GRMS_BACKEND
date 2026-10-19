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
    public decimal AgreedWholesalePrice { get; set; }
    public int MinimumOrderQuantity { get; set; } = 1;
    public bool IsPrimarySupplier { get; set; }
    public string Status { get; set; } = "Active";

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class AssignSupplierProductCommand
{
    public Guid ProductId { get; set; }
    public string DeliveryUoM { get; set; } = "Cái";
    public decimal AgreedWholesalePrice { get; set; }
    public int MinimumOrderQuantity { get; set; } = 1;
    public bool IsPrimarySupplier { get; set; } = false;
    public string? SupplierProductCode { get; set; }
}

public class UpdateSupplierProductCommand
{
    public string DeliveryUoM { get; set; } = "Cái";
    public decimal AgreedWholesalePrice { get; set; }
    public int MinimumOrderQuantity { get; set; } = 1;
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
    public decimal AgreedWholesalePrice { get; set; }
    public int MinimumOrderQuantity { get; set; } = 1;
    public bool IsPrimarySupplier { get; set; }
    public string Status { get; set; } = "Active";
}

public class SupplierProductQueryFilter
{
    public string? SearchTerm { get; set; }
    public Guid? CategoryId { get; set; }
    public SupplierProductStatus? Status { get; set; }
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}
