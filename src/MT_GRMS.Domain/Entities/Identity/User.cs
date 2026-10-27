using System.ComponentModel.DataAnnotations.Schema;
using MT_GRMS.Domain.Common;

namespace MT_GRMS.Domain.Entities.Identity;

/// <summary>
/// Bảng Users (Tài khoản người dùng toàn hệ thống)
/// </summary>
public class User : BaseEntity<string>
{
    public User()
    {
        Id = Guid.NewGuid().ToString();
    }

    public string UserName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? AvatarUrl { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsEmailVerified { get; set; } = false;
    public bool MustChangePassword { get; set; } = false;
    public DateTime? LastLoginAt { get; set; }
    public string? LastLoginIp { get; set; }
    public string? SecurityStamp { get; set; }
    public string? PasswordResetOtp { get; set; }
    public DateTime? OtpExpiry { get; set; }
    public string? RefreshToken { get; set; }
    public DateTime? RefreshTokenExpiryTime { get; set; }
    public int AccessFailedCount { get; set; } = 0;
    public DateTime? LockoutEnd { get; set; }

    // Backward compatibility aliases
    [NotMapped]
    public string Username { get => UserName; set => UserName = value; }
    [NotMapped]
    public string Password { get => PasswordHash; set => PasswordHash = value; }

    // Navigations
    public ICollection<TenantUser> TenantUsers { get; set; } = new List<TenantUser>();
}

public class Role : BaseTenantEntity
{
    public string RoleName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string PermissionsJson { get; set; } = "[]";
}
