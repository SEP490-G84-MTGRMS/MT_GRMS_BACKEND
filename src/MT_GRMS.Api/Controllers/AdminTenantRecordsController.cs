using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MT_GRMS.Domain.Constants;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.Api.Controllers;

[ApiController]
[Route("api/admin/tenants/{tenantId}/records")]
[Authorize(Roles = RoleConstants.AdminOnly)]
public class AdminTenantRecordsController(AppDbContext context) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(string tenantId, string section = "purchases", int page = 1,
        int pageSize = 10, CancellationToken cancellationToken = default)
    {
        if (section != "purchases")
            return BadRequest(new { success = false, message = "Chỉ hỗ trợ lịch sử mua gói dịch vụ." });

        if (!await context.Tenants.AsNoTracking().AnyAsync(t => t.Id == tenantId && !t.IsDeleted, cancellationToken))
            return NotFound(new { success = false, message = "Không tìm thấy khách hàng." });

        var purchases = context.WalletTransactions.AsNoTracking()
            .Where(t => t.TenantId == tenantId && !t.IsDeleted && t.Type == "PURCHASE_PLAN");
        var total = await purchases.CountAsync(cancellationToken);
        pageSize = Math.Clamp(pageSize, 1, 100);
        page = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(total / (double)pageSize)));
        var items = await purchases.OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(t => new { t.Id, code = t.TransactionCode, t.Amount, t.Description,
                t.PaymentMethod, t.Status, t.CreatedAt })
            .ToListAsync(cancellationToken);

        return Ok(new { success = true, data = new { items, totalItems = total, page, pageSize } });
    }
}
