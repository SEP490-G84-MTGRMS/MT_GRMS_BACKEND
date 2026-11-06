using MT_GRMS.Domain.Common;

namespace MT_GRMS.Domain.Entities.HR;

/// <summary>
/// Bảng EmployeeAllowances (Khoản phụ cấp cố định của nhân viên)
/// </summary>
public class EmployeeAllowance : BaseTenantEntity
{
    public Guid EmployeeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Amount { get; set; } = 0.00m;

    // Navigations
    public Employee? Employee { get; set; }
}

