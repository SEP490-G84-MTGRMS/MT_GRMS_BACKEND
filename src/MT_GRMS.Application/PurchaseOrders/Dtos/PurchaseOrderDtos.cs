using System;
using System.Collections.Generic;

namespace MT_GRMS.Application.PurchaseOrders.Dtos;

public class OrderMatrixQuery
{
    public Guid BranchId { get; set; }
    public Guid? SupplierId { get; set; }
    public DateTime ViewFrom { get; set; }
    public int DurationDays { get; set; } = 7;
}

public class OrderMatrixProductDto
{
    public Guid Id { get; set; }
    public Guid SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public string Barcode { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Moq { get; set; }
    public decimal Soh { get; set; }
    public decimal SalesPerDay { get; set; }
    public decimal SafetyStock { get; set; }

    // Trạng thái nghiệp vụ Active / Inactive
    public bool IsProductActive { get; set; } = true;
    public string? ProductInactiveReason { get; set; }

    public bool IsSupplierActive { get; set; } = true;
    public string? SupplierInactiveReason { get; set; }

    public bool IsSupplierProductActive { get; set; } = true;
    public string? SupplierProductInactiveReason { get; set; }

    public bool IsSupplierBranchActive { get; set; } = true;
    public string? SupplierBranchInactiveReason { get; set; }

    public bool IsBranchProductActive { get; set; } = true;
    public string? BranchProductInactiveReason { get; set; }

    public bool IsRowDisabled => !IsProductActive || !IsSupplierActive || !IsSupplierProductActive || !IsSupplierBranchActive || !IsBranchProductActive;
    public string? RowDisabledReason { get; set; }
}

public class OrderMatrixSupplierDeliveryConfigDto
{
    public Guid SupplierId { get; set; }
    public string SupplierCode { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public bool IsAssignedToBranch { get; set; } = true;
    public string? InactiveReason { get; set; }

    public int LeadTimeDays { get; set; } = 1;
    public string CutoffTime { get; set; } = "15:00";
    public List<int> ActiveDeliveryDays { get; set; } = new() { 1, 2, 3, 4, 5, 6 };
    public List<string> BlackoutDates { get; set; } = new();
}

public class OrderMatrixCellDto
{
    public Guid ProductId { get; set; }
    public DateTime DeliveryDate { get; set; }
    public decimal EmployeeQty { get; set; }
    public decimal? StoreQty { get; set; }
    public bool IsOwnerAdjusted { get; set; }
    public decimal FinalQty { get; set; }
}

public class OrderMatrixResponse
{
    public Guid BranchId { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public Guid? SupplierId { get; set; }
    public DateTime ViewFrom { get; set; }
    public int DurationDays { get; set; }
    public IReadOnlyList<OrderMatrixProductDto> Products { get; set; } = new List<OrderMatrixProductDto>();
    public IReadOnlyList<OrderMatrixCellDto> Cells { get; set; } = new List<OrderMatrixCellDto>();
    public IReadOnlyList<OrderMatrixSupplierDeliveryConfigDto> SupplierConfigs { get; set; } = new List<OrderMatrixSupplierDeliveryConfigDto>();
    public Guid? DraftPoId { get; set; }
    public string Status { get; set; } = "DRAFT";
}

public class SaveMatrixDraftCommand
{
    public Guid BranchId { get; set; }
    public Guid? SupplierId { get; set; }
    public DateTime ViewFrom { get; set; }
    public DateTime ViewTo { get; set; }
    public string? Note { get; set; }
    public List<OrderMatrixCellDto> Cells { get; set; } = new();
}

public class SplitMatrixPoItemCommand
{
    public Guid ProductId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public string Unit { get; set; } = string.Empty;
}

public class SplitMatrixPoGroupCommand
{
    public Guid SupplierId { get; set; }
    public DateTime DeliveryDate { get; set; }
    public List<SplitMatrixPoItemCommand> Items { get; set; } = new();
}

public class SplitMatrixPoCommand
{
    public Guid BranchId { get; set; }
    public Guid? ParentDraftId { get; set; }
    public string? Note { get; set; }
    public List<SplitMatrixPoGroupCommand> Groups { get; set; } = new();
}

public class PurchaseOrderDetailDto
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal? EmployeeQty { get; set; }
    public decimal? StoreQty { get; set; }
    public bool IsOwnerAdjusted { get; set; }
    public decimal? FinalQty { get; set; }
    public decimal OrderedQty { get; set; }
    public decimal ReceivedQty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineAmount { get; set; }
    public DateTime? DeliveryDate { get; set; }
}

public class PurchaseOrderDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public Guid BranchId { get; set; }
    public string BranchCode { get; set; } = string.Empty;
    public string BranchName { get; set; } = string.Empty;
    public Guid SupplierId { get; set; }
    public string SupplierCode { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public DateTime? DeliveryDate { get; set; }
    public string Status { get; set; } = "DRAFT";
    public decimal TotalQty { get; set; }
    public decimal TotalAmount { get; set; }
    public string? Note { get; set; }
    public string? CreatedByRole { get; set; }
    public Guid? ParentDraftId { get; set; }
    public DateTime CreatedAt { get; set; }
    public IReadOnlyList<PurchaseOrderDetailDto> Details { get; set; } = new List<PurchaseOrderDetailDto>();
}

public class UpdatePurchaseOrderStatusRequest
{
    public string Status { get; set; } = string.Empty;
    public string? Reason { get; set; }
}

public class PurchaseOrderFilterQuery
{
    public Guid? BranchId { get; set; }
    public Guid? SupplierId { get; set; }
    public string? Status { get; set; }
    public string? Keyword { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}
