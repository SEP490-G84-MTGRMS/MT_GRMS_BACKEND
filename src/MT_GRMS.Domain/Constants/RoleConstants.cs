namespace MT_GRMS.Domain.Constants;

/// <summary>
/// Các hằng số phân quyền chuẩn 3 Roles của hệ thống MT-GRMS:
/// 1. Admin (SaaS Admin / SystemAdmin)
/// 2. StoreOwner (Chủ hộ kinh doanh)
/// 3. Employee (Nhân viên thu ngân / Bán hàng / Kho)
/// </summary>
public static class RoleConstants
{
    public const string Admin = "Admin";
    public const string AdminUpper = "ADMIN";

    public const string StoreOwner = "StoreOwner";
    public const string StoreOwnerUpper = "STORE_OWNER";

    public const string Employee = "Employee";
    public const string EmployeeUpper = "EMPLOYEE";

    /// <summary>
    /// Quyền chỉ dành cho Quản trị viên cấp cao (SaaS Admin)
    /// </summary>
    public const string AdminOnly = "ADMIN";

    /// <summary>
    /// Quyền dành cho Chủ hộ kinh doanh và Quản trị viên hệ thống (Không cho phép Employee)
    /// </summary>
    public const string StoreOwnerOrAdmin = "StoreOwner,STORE_OWNER,Admin,ADMIN,Manager";

    /// <summary>
    /// Tất cả nhân sự trong hệ thống bao gồm Employee
    /// </summary>
    public const string AllRoles = "StoreOwner,STORE_OWNER,Admin,ADMIN,Employee,EMPLOYEE,Manager";
}
