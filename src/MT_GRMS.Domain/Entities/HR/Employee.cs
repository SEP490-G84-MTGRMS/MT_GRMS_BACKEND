using System.ComponentModel.DataAnnotations.Schema;
using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Entities.Identity;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Domain.Entities.HR;

/// <summary>
/// Bảng Employees (Hồ sơ nhân viên gắn với Chi nhánh & Tenant)
/// Quan hệ 1-1 với bảng Users để lưu trữ thông tin định danh cá nhân (FullName, Phone, Email, AvatarUrl).
/// </summary>
public class Employee : BaseTenantEntity
{
    public Guid BranchId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string EmployeeCode { get; set; } = string.Empty;
    public string CitizenId { get; set; } = string.Empty;
    public DateTime? DateOfBirth { get; set; }
    public string? Gender { get; set; }
    public string? JobTitle { get; set; }
    public string SalaryType { get; set; } = "monthly";
    public decimal BaseRate { get; set; } = 0.00m;
    public string? BankAccountNumber { get; set; }
    public string? BankName { get; set; }
    public string? BankAccountHolder { get; set; }
    public List<EmployeeAllowance> Allowances { get; set; } = new();
    public decimal DebtAmount { get; set; } = 0.00m;
    public EmployeeStatus Status { get; set; } = EmployeeStatus.Active;

    private string? _fullName;
    private string? _phone;
    private string? _email;
    private string? _avatarUrl;

    // Computed properties ánh xạ trực tiếp sang User liên kết (Tránh trùng lặp dữ liệu với bảng Users)
    [NotMapped]
    public string FullName
    {
        get => User?.FullName ?? _fullName ?? string.Empty;
        set
        {
            _fullName = value;
            if (User != null) User.FullName = value;
        }
    }

    [NotMapped]
    public string Phone
    {
        get => User?.Phone ?? _phone ?? string.Empty;
        set
        {
            _phone = value;
            if (User != null) User.Phone = value;
        }
    }

    [NotMapped]
    public string? Email
    {
        get => User?.Email ?? _email;
        set
        {
            _email = value;
            if (User != null) User.Email = value ?? string.Empty;
        }
    }

    [NotMapped]
    public string? AvatarUrl
    {
        get => User?.AvatarUrl ?? _avatarUrl;
        set
        {
            _avatarUrl = value;
            if (User != null) User.AvatarUrl = value;
        }
    }

    // Navigations
    public Branch? Branch { get; set; }
    public User User { get; set; } = null!;
    public ICollection<Payslip> Payslips { get; set; } = new List<Payslip>();
    public ICollection<EmployeeSchedule> Schedules { get; set; } = new List<EmployeeSchedule>();
    public ICollection<EmployeeBonusViolation> BonusViolations { get; set; } = new List<EmployeeBonusViolation>();
}

/// <summary>
/// Khoản phụ cấp nhân viên (Lưu trữ dạng JSONB trong bảng Employees)
/// </summary>
public class EmployeeAllowance
{
    public string Name { get; set; } = string.Empty;
    public decimal Amount { get; set; } = 0.00m;
}
