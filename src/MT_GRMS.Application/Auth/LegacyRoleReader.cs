using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Application.Auth;

/// <summary>Temporary database compatibility reader; never accepts token or request roles.
/// Remove after role-data migration. SUNSET: 2026-12-31.</summary>
public static class LegacyRoleReader
{
    public static UserRole ReadStoredRole(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "ADMIN" or "SUPER_ADMIN" or "SYSTEM_ADMIN" => UserRole.Admin,
        "STORE_OWNER" or "STOREOWNER" or "TENANT_ADMIN" or "OWNER" => UserRole.StoreOwner,
        "STAFF" or "EMPLOYEE" or "CASHIER" or "MANAGER" or "BRANCH_MANAGER" => UserRole.Staff,
        _ => throw new ArgumentException("Role dữ liệu không hợp lệ.", nameof(value))
    };
}
