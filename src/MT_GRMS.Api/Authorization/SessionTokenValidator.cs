using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using MT_GRMS.Application.Auth;
using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Api.Authorization;

/// <summary>Revalidates signed claims against current identity data before tenant context is created.</summary>
public static class SessionTokenValidator
{
    public static async Task ValidateAsync(TokenValidatedContext context)
    {
        try
        {
            var principal = context.Principal!;
            var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var roles = principal.Claims.Where(c => c.Type == ClaimTypes.Role || c.Type == "role")
                .Select(c => c.Value).Distinct(StringComparer.Ordinal).ToArray();
            var tenantClaims = principal.FindAll("tenant_id").ToArray();
            var branchClaims = principal.FindAll("branch_id").ToArray();
            var stamps = principal.FindAll("security_stamp").ToArray();
            if (string.IsNullOrWhiteSpace(userId) || roles.Length != 1 ||
                roles[0] is not (UserRoleNames.Admin or UserRoleNames.StoreOwner or UserRoleNames.Staff) ||
                tenantClaims.Length > 1 || branchClaims.Length > 1 || stamps.Length != 1)
            {
                context.Fail("Ngữ cảnh xác thực không hợp lệ.");
                return;
            }

            var repo = context.HttpContext.RequestServices.GetRequiredService<IUserRepository>();
            var user = await repo.GetByIdAsync(userId, context.HttpContext.RequestAborted);
            if (user == null || !user.IsActive || string.IsNullOrWhiteSpace(user.SecurityStamp) ||
                !string.Equals(stamps[0].Value, user.SecurityStamp, StringComparison.Ordinal))
            {
                context.Fail("Phiên đăng nhập đã bị thu hồi.");
                return;
            }

            if (user.IsAdmin)
            {
                if (roles[0] != UserRoleNames.Admin || tenantClaims.Length != 0 || branchClaims.Length != 0)
                    context.Fail("Admin không có ngữ cảnh tenant.");
                return;
            }

            var memberships = user.TenantUsers.Where(t => t.IsActive && !string.IsNullOrWhiteSpace(t.TenantId)).ToArray();
            if (roles[0] == UserRoleNames.Admin || memberships.Length != 1 || tenantClaims.Length != 1 ||
                memberships[0].TenantId != tenantClaims[0].Value ||
                LegacyRoleReader.ReadStoredRole(memberships[0].Role).ToStringRole() != roles[0] ||
                principal.FindAll("is_admin").Any(c => c.Value != "false"))
            {
                context.Fail("Quyền hoặc tenant của phiên đã thay đổi.");
                return;
            }

            if (roles[0] == UserRoleNames.Staff)
            {
                var assigned = await repo.GetEmployeeBranchIdAsync(userId, memberships[0].TenantId, context.HttpContext.RequestAborted);
                if (assigned == null || assigned == Guid.Empty || branchClaims.Length != 1 ||
                    !Guid.TryParse(branchClaims[0].Value, out var branch) || branch != assigned)
                    context.Fail("Chi nhánh phân công của phiên không hợp lệ.");
            }
        }
        catch (ArgumentException)
        {
            context.Fail("Role không hợp lệ.");
        }
        catch (OperationCanceledException)
        {
            context.Fail("Xác thực bị gián đoạn.");
        }
    }
}
