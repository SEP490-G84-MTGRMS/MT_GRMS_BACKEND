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
