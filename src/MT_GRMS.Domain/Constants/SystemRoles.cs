namespace MT_GRMS.Domain.Constants;

public static class SystemRoles
{
    public const string Admin = "ADMIN";
    public const string StoreOwner = "STORE_OWNER";
    public const string Staff = "STAFF";
    public const string Employee = "EMPLOYEE";

    /// <summary>
    /// Quyền dành cho Quản trị viên và Chủ cửa hàng
    /// </summary>
    public const string OwnerOrAdmin = "STORE_OWNER,ADMIN";

    /// <summary>
    /// Quyền dành cho tất cả người dùng phân hệ Nhân sự (Chủ cửa hàng, Quản trị viên và Nhân viên)
    /// Hỗ trợ fallback EMPLOYEE cho các token cũ chưa hết hạn
    /// </summary>
    public const string AllStaffAndOwner = "STORE_OWNER,ADMIN,STAFF,EMPLOYEE";
}

