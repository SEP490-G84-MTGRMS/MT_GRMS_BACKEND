using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.HR.Employees;

namespace MT_GRMS.Api.Authorization;

public class BranchScopedOperationRequirement : IAuthorizationRequirement
{
    public string OperationName { get; }
    public BranchScopedOperationRequirement(string operationName) => OperationName = operationName;
}

public class BranchScopedResourceAuthorizationHandler : AuthorizationHandler<BranchScopedOperationRequirement>
{
    private readonly ITenantContext _tenantContext;
    private readonly IEmployeeRepository _employeeRepository;

    public BranchScopedResourceAuthorizationHandler(
        ITenantContext tenantContext,
        IEmployeeRepository employeeRepository)
    {
        _tenantContext = tenantContext;
        _employeeRepository = employeeRepository;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        BranchScopedOperationRequirement requirement)
    {
        // 1. Chặn Admin khỏi dữ liệu kinh doanh của Tenant
        if (context.User.IsInRole("ADMIN") ||
            string.Equals(context.User.FindFirst("role")?.Value, "ADMIN", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(context.User.FindFirst("is_admin")?.Value, "true", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(_tenantContext.CurrentRole, "ADMIN", StringComparison.OrdinalIgnoreCase))
        {
            context.Fail();
            return;
        }

        // 2. Token phải có tenant_id (Admin không có tenant_id)
        var tokenTenantId = context.User.FindFirst("tenant_id")?.Value;
        if (string.IsNullOrEmpty(tokenTenantId))
        {
            context.Fail();
            return;
        }

        // 3. Nếu là Resource-Based Authorization (có Resource cụ thể hoặc Scope)
        if (context.Resource is ITenantBranchResource resource)
        {
            var effectiveTenant = !string.IsNullOrEmpty(_tenantContext.CurrentTenantId)
                ? _tenantContext.CurrentTenantId
                : tokenTenantId;

            // Kiểm tra cô lập Tenant (Fail-Closed)
            if (string.IsNullOrEmpty(effectiveTenant) ||
                !string.Equals(resource.TenantId, effectiveTenant, StringComparison.Ordinal))
            {
                context.Fail();
                return;
            }

            // STORE_OWNER: Toàn quyền trên mọi chi nhánh trong tenant
            if (context.User.IsInRole("STORE_OWNER") ||
                string.Equals(context.User.FindFirst("role")?.Value, "STORE_OWNER", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(_tenantContext.CurrentRole, "STORE_OWNER", StringComparison.OrdinalIgnoreCase))
            {
                context.Succeed(requirement);
                return;
            }

            // STAFF: Kiểm tra chi nhánh được phân công (Single assignment qua Employee.BranchID)
            var isStaff = context.User.IsInRole("STAFF") ||
                          context.User.IsInRole("EMPLOYEE") ||
                          context.User.IsInRole("CASHIER") ||
                          string.Equals(context.User.FindFirst("role")?.Value, "STAFF", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(context.User.FindFirst("role")?.Value, "EMPLOYEE", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(context.User.FindFirst("role")?.Value, "CASHIER", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(_tenantContext.CurrentRole, "STAFF", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(_tenantContext.CurrentRole, "EMPLOYEE", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(_tenantContext.CurrentRole, "CASHIER", StringComparison.OrdinalIgnoreCase);

            if (isStaff)
            {
                if (!resource.BranchId.HasValue || resource.BranchId.Value == Guid.Empty)
                {
                    context.Fail();
                    return;
                }

                Guid? assignedBranchId = _tenantContext.CurrentBranchId;
                if (!assignedBranchId.HasValue || assignedBranchId.Value == Guid.Empty)
                {
                    var branchClaim = context.User.FindFirst("branch_id")?.Value;
                    if (Guid.TryParse(branchClaim, out var parsedClaimBranch))
                    {
                        assignedBranchId = parsedClaimBranch;
                    }
                }

                if (!assignedBranchId.HasValue || assignedBranchId.Value == Guid.Empty)
                {
                    var userIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                        ?? context.User.FindFirst("sub")?.Value;
                    if (!string.IsNullOrWhiteSpace(userIdClaim))
                    {
                        var emp = await _employeeRepository.GetByUserIdAsync(userIdClaim);
                        if (emp != null && emp.BranchId != Guid.Empty)
                        {
                            assignedBranchId = emp.BranchId;
                        }
                    }
                }

                if (assignedBranchId.HasValue && assignedBranchId.Value == resource.BranchId.Value)
                {
                    context.Succeed(requirement);
                    return;
                }

                context.Fail();
                return;
            }

            // Role không hợp lệ
            context.Fail();
            return;
        }

        // 4. Ở tầng Action Filter (chưa có resource): Cho phép STORE_OWNER hoặc STAFF tiếp tục vào action
        var isAllowedRole = context.User.IsInRole("STORE_OWNER") ||
                            context.User.IsInRole("STAFF") ||
                            context.User.IsInRole("EMPLOYEE") ||
                            context.User.IsInRole("CASHIER") ||
                            string.Equals(context.User.FindFirst("role")?.Value, "STORE_OWNER", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(context.User.FindFirst("role")?.Value, "STAFF", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(context.User.FindFirst("role")?.Value, "EMPLOYEE", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(context.User.FindFirst("role")?.Value, "CASHIER", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(_tenantContext.CurrentRole, "STORE_OWNER", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(_tenantContext.CurrentRole, "STAFF", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(_tenantContext.CurrentRole, "EMPLOYEE", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(_tenantContext.CurrentRole, "CASHIER", StringComparison.OrdinalIgnoreCase);

        if (isAllowedRole)
        {
            context.Succeed(requirement);
        }
        else
        {
            context.Fail();
        }
    }
}
