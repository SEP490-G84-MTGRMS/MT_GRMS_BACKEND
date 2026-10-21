namespace MT_GRMS.Application.Branches.Dtos;

public class BranchDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? ManagerName { get; set; }
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
