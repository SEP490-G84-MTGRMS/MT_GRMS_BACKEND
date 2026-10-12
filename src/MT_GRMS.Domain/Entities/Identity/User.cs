using MT_GRMS.Domain.Common;

namespace MT_GRMS.Domain.Entities.Identity;

/// <summary>
/// Thực thể User tối giản phục vụ xác thực đăng nhập:
/// - Id: string Uid (chuỗi định danh duy nhất, ví dụ: usr_xxx hoặc NanoId/Guid string)
/// - Username: tên đăng nhập
/// - Password: mật khẩu
/// </summary>
public class User
{
    public string Id { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class Role : BaseTenantEntity
{
    public string RoleName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string PermissionsJson { get; set; } = "[]";
}
