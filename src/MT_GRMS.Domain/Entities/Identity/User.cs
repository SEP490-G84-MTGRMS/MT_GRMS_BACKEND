using System.ComponentModel.DataAnnotations.Schema;
using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Domain.Entities.Identity;

/// <summary>
/// Bảng Users (Tài khoản người dùng toàn hệ thống và mã PIN bảo mật PBKDF2)
/// </summary>
public class User : BaseEntity<string>
{
    public User()
    {
        Id = Uuid7.NewString();
    }

    public string UserName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? AvatarUrl { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsAdmin { get; set; } = false;
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

    // Security PIN (PBKDF2 >= 600.000 iterations)
    public string? SecurityPinHash { get; set; }
    public string? SecurityPinSalt { get; set; }
    public int FailedPinAttempts { get; set; } = 0;
    public DateTime? PinLockedUntilUtc { get; set; }

    // Backward compatibility aliases
    [NotMapped]
    public string Username { get => UserName; set => UserName = value; }
    [NotMapped]
    public string Password { get => PasswordHash; set => PasswordHash = value; }

    // Vai trò người dùng (Enum UserRole: Admin, StoreOwner, Staff)
    [NotMapped]
    public string Role
    {
        get => IsAdmin ? UserRoleNames.Admin : (TenantUsers.FirstOrDefault()?.Role ?? UserRoleNames.StoreOwner);
        set
        {
            if (value?.Equals(UserRoleNames.Admin, StringComparison.OrdinalIgnoreCase) == true)
            {
                IsAdmin = true;
            }
            var tu = TenantUsers.FirstOrDefault();
            if (tu != null)
            {
                tu.Role = value ?? UserRoleNames.Staff;
            }
        }
    }

    [NotMapped]
    public UserRole UserRole => UserRoleNames.FromString(Role);

    // Navigations
    public ICollection<TenantUser> TenantUsers { get; set; } = new List<TenantUser>();
}

public class Role : BaseTenantEntity
{
    public string RoleName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string PermissionsJson { get; set; } = "[]";
}
