namespace MT_GRMS.Application.Branches.Dtos;

public class BranchDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? ManagerName { get; set; }
    public bool IsHeadquarter { get; set; } = false;
    public string Status { get; set; } = "Active";
    public int PendingOrdersCount { get; set; } = 0;
    public DateTime? LastOrderDate { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class BranchFilterQuery
{
    public string? Keyword { get; set; }
    public string? BranchCode { get; set; }
    public string? Status { get; set; }
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

public record BranchQuotaDto(
    int UsedBranches,
    int MaxBranches,
    bool CanAddMore,
    string CurrentPlanCode,
    string CurrentPlanName
);

public class CreateBranchRequest
{
    public string BranchCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? ManagerName { get; set; }
    public bool IsHeadquarter { get; set; } = false;

    public CreateBranchRequest() { }

    public CreateBranchRequest(
        string branchCode,
        string name,
        string address,
        string? phone = null,
        string? managerName = null,
        bool isHeadquarter = false,
        string? email = null)
    {
        BranchCode = branchCode;
        Name = name;
        Address = address;
        Phone = phone;
        ManagerName = managerName;
        IsHeadquarter = isHeadquarter;
        Email = email;
    }
}

public class UpdateBranchRequest
{
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? ManagerName { get; set; }
    public bool IsHeadquarter { get; set; } = false;

    public UpdateBranchRequest() { }

    public UpdateBranchRequest(
        string name,
        string address,
        string? phone = null,
        string? managerName = null,
        bool isHeadquarter = false,
        string? email = null)
    {
        Name = name;
        Address = address;
        Phone = phone;
        ManagerName = managerName;
        IsHeadquarter = isHeadquarter;
        Email = email;
    }
}

public class UpdateBranchStatusRequest
{
    public bool IsActive { get; set; }

    public UpdateBranchStatusRequest() { }

    public UpdateBranchStatusRequest(bool isActive)
    {
        IsActive = isActive;
    }
}

/// <summary>
/// DTO chi tiết đầy đủ của một chi nhánh gồm thông tin cơ bản và các thông số tổng quát
/// </summary>
public class BranchDetailDto
{
    // 1. Thông tin cơ bản của chi nhánh
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? ManagerName { get; set; }
    public bool IsHeadquarter { get; set; } = false;
    public string Status { get; set; } = "Active";
    public DateTime CreatedAt { get; set; }

    // 2. Các thông số tổng quát
    public int TotalProducts { get; set; }
    public decimal TotalStock { get; set; }
    public int LowStockCount { get; set; }
    public int TotalEmployees { get; set; }
    public int ActiveEmployees { get; set; }
    public int PendingOrdersCount { get; set; } = 0;
    public DateTime? LastOrderDate { get; set; }

    // Danh sách tóm tắt xem nhanh
    public List<BranchEmployeeSummaryItemDto> RecentEmployees { get; set; } = new();
    public List<BranchProductSummaryItemDto> RecentProducts { get; set; } = new();
}

public class BranchEmployeeSummaryItemDto
{
    public Guid Id { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Status { get; set; } = "Active";
    public DateTime? StartDate { get; set; }
}

public class BranchProductSummaryItemDto
{
    public Guid ProductId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string? Barcode { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string Unit { get; set; } = "Cái";
    public decimal SellingPrice { get; set; }
    public string Status { get; set; } = "Active";
}
