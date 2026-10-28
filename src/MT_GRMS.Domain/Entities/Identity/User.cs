using MT_GRMS.Domain.Common;

namespace MT_GRMS.Domain.Entities.Identity;

/// <summary>
/// Thực thể User:
/// - Id: string Uid (chuỗi định danh duy nhất)
/// - Username: tên đăng nhập
/// - Password: mật khẩu
/// - Các trường mã PIN bảo mật phục vụ chốt kỳ tính thuế (PBKDF2)
/// </summary>
public class User
{
    public string Id { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    // Security PIN (PBKDF2 >= 600.000 iterations)
    public string? SecurityPinHash { get; set; }
    public string? SecurityPinSalt { get; set; }
    public int FailedPinAttempts { get; set; } = 0;
    public DateTime? PinLockedUntilUtc { get; set; }
}

public class Role : BaseTenantEntity
{
    public string RoleName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string PermissionsJson { get; set; } = "[]";
}
