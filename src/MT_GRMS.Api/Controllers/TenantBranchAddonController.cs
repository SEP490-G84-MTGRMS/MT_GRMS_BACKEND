using System.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Constants;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.Api.Controllers;

public record BranchAddonPurchaseRequest(int ExtraBranches, Guid RequestId);
public record BranchAddonQuote(int ExtraBranches, decimal Subtotal, decimal Vat, decimal Total, DateTime ExpiresAt);

[ApiController]
[Route("api/tenant/branch-addon")]
[Authorize(Roles = RoleConstants.StoreOwnerOrAdmin)]
public class TenantBranchAddonController(AppDbContext context) : BaseApiController
{
    private const decimal PricePerBranchMonth = 80000m;
    private string? TenantId => User.FindFirst("tenant_id")?.Value;

    private async Task<(Tenant? Tenant, BranchAddonQuote? Quote)> CreateQuoteAsync(int extraBranches, CancellationToken cancellationToken)
    {
        if (extraBranches is < 1 or > 100 || string.IsNullOrWhiteSpace(TenantId)) return (null, null);
        var tenant = await context.Tenants.SingleOrDefaultAsync(t => t.Id == TenantId, cancellationToken);
        if (tenant?.SubscriptionExpiresAt is not DateTime expiry || expiry <= DateTime.UtcNow || tenant.SubscriptionStatus != "ACTIVE" || tenant.CurrentPlan != "PRO") return (tenant, null);
        var subtotal = Math.Round(extraBranches * PricePerBranchMonth * (decimal)(expiry - DateTime.UtcNow).TotalDays / 30m, 0, MidpointRounding.AwayFromZero);
        var vat = Math.Round(subtotal * .1m, 0, MidpointRounding.AwayFromZero);
        return (tenant, new BranchAddonQuote(extraBranches, subtotal, vat, subtotal + vat, expiry));
    }

    [HttpGet("quote")]
    public async Task<IActionResult> Quote([FromQuery] int extraBranches, CancellationToken cancellationToken)
    {
        var (_, quote) = await CreateQuoteAsync(extraBranches, cancellationToken);
        return quote == null
            ? BadRequest(ApiResponse<object>.Fail("INVALID_BRANCH_ADDON", "Chỉ Gói Pro còn hiệu lực mới được mua thêm chi nhánh."))
            : Ok(ApiResponse<BranchAddonQuote>.Ok(quote));
    }

    [HttpPost("purchase")]
    public async Task<IActionResult> Purchase([FromBody] BranchAddonPurchaseRequest request, CancellationToken cancellationToken)
    {
        if (request.RequestId == Guid.Empty || request.ExtraBranches is < 1 or > 100 || string.IsNullOrWhiteSpace(TenantId)) return BadRequest();
        var code = $"BRA-{request.RequestId:N}";
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (await context.WalletTransactions.AsNoTracking().AnyAsync(x => x.TenantId == TenantId && x.TransactionCode == code && x.Status == "SUCCESS", cancellationToken))
            return Ok(ApiResponse<object>.Ok(new { alreadyProcessed = true }));
        var (tenant, quote) = await CreateQuoteAsync(request.ExtraBranches, cancellationToken);
        if (tenant == null || quote == null) return BadRequest(ApiResponse<object>.Fail("INVALID_BRANCH_ADDON", "Gói Pro không còn hiệu lực."));
        if (tenant.WalletBalance < quote.Total) return BadRequest(ApiResponse<object>.Fail("INSUFFICIENT_WALLET_BALANCE", "Số dư ví không đủ."));
        var before = tenant.WalletBalance;
        tenant.WalletBalance -= quote.Total;
        tenant.ExtraBranches += request.ExtraBranches;
        context.WalletTransactions.Add(new WalletTransaction { TenantId = tenant.Id, TransactionCode = code, Type = "PURCHASE_ADDON", Amount = quote.Total, BalanceBefore = before, BalanceAfter = tenant.WalletBalance, PaymentMethod = "WALLET", Status = "SUCCESS", Description = $"Mua thêm {request.ExtraBranches} chi nhánh đến {quote.ExpiresAt:yyyy-MM-dd}" });
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Ok(ApiResponse<BranchAddonQuote>.Ok(quote));
    }
}
