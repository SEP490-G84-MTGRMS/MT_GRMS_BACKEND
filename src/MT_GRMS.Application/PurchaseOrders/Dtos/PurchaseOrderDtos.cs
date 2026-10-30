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
}

public class OrderMatrixCellDto
{
    public Guid ProductId { get; set; }
    public DateTime DeliveryDate { get; set; }
    public decimal EmployeeQty { get; set; }
    public decimal? StoreQty { get; set; }
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
    public decimal OrderedQty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineAmount { get; set; }
    public DateTime? DeliveryDate { get; set; }
}

public class PurchaseOrderDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public Guid BranchId { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public Guid SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public DateTime? DeliveryDate { get; set; }
    public string Status { get; set; } = "DRAFT";
    public decimal TotalQty { get; set; }
    public decimal TotalAmount { get; set; }
    public string? Note { get; set; }
    public Guid? ParentDraftId { get; set; }
    public DateTime CreatedAt { get; set; }
    public IReadOnlyList<PurchaseOrderDetailDto> Details { get; set; } = new List<PurchaseOrderDetailDto>();
}

public class PurchaseOrderFilterQuery
{
    public Guid? BranchId { get; set; }
    public Guid? SupplierId { get; set; }
    public string? Status { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}
