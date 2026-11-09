namespace MT_GRMS.Domain.Enums;

/// <summary>
/// Các vai trò chuẩn hóa trong hệ thống MT-GRMS:
/// Admin: Quản trị viên hệ thống SaaS
/// StoreOwner: Chủ cửa hàng / Chủ chuỗi bán lẻ
/// Staff: Nhân viên vận hành cửa hàng (bán hàng, thu ngân, kho...)
/// </summary>
public enum UserRole
{
    Admin = 1,
    StoreOwner = 2,
    Staff = 3
}

/// <summary>
/// Hằng số chuỗi và phương thức mở rộng hỗ trợ ánh xạ vai trò người dùng
/// </summary>
public static class UserRoleNames
{
    public const string Admin = "ADMIN";
    public const string StoreOwner = "STORE_OWNER";
    public const string Staff = "STAFF";

    public static string ToStringRole(this UserRole role) => role switch
    {
        UserRole.Admin => Admin,
        UserRole.StoreOwner => StoreOwner,
        UserRole.Staff => Staff,
        _ => Staff
    };

    public static UserRole FromString(string? role)
    {
        if (string.IsNullOrWhiteSpace(role)) return UserRole.Staff;
        var r = role.Trim().ToUpperInvariant();
        if (r is "ADMIN" or "SUPER_ADMIN" or "SYSTEM_ADMIN") return UserRole.Admin;
        if (r is "STORE_OWNER" or "TENANT_ADMIN" or "OWNER") return UserRole.StoreOwner;
        return UserRole.Staff;
    }
}

