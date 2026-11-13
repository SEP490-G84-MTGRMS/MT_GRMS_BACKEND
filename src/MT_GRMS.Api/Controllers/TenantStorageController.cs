using System.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Constants;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.Api.Controllers;

public record StoragePurchaseRequest(int ExtraGb, Guid RequestId);
public record StorageQuote(int ExtraGb, decimal Subtotal, decimal Vat, decimal Total, DateTime ExpiresAt);
public record StorageUsage(long UsedBytes, long LimitBytes, int BaseGb, int ExtraGb, DateTime? AddonExpiresAt);

[ApiController]
[Route("api/tenant/storage")]
[Authorize]
public class TenantStorageController(AppDbContext context) : BaseApiController
{
    private const long Gb = 1024L * 1024 * 1024;
    private const decimal PricePerGbMonth = 15000m;

    private string? TenantId => User.FindFirst("tenant_id")?.Value;

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(TenantId)) return Unauthorized();
        var tenant = await context.Tenants.AsNoTracking().SingleOrDefaultAsync(t => t.Id == TenantId, cancellationToken);
        if (tenant == null) return NotFound();
        var extra = tenant.StorageAddonExpiresAt > DateTime.UtcNow ? tenant.ExtraStorageGb : 0;
        return Ok(ApiResponse<StorageUsage>.Ok(new StorageUsage(tenant.StorageUsedBytes, (2L + extra) * Gb, 2, extra, tenant.StorageAddonExpiresAt)));
    }

    private async Task<(Tenant? Tenant, StorageQuote? Quote)> QuoteAsync(int extraGb, CancellationToken cancellationToken)
    {
        if (extraGb < 1 || extraGb > 100 || string.IsNullOrWhiteSpace(TenantId)) return (null, null);
        var tenant = await context.Tenants.SingleOrDefaultAsync(t => t.Id == TenantId, cancellationToken);
        if (tenant?.SubscriptionExpiresAt is not DateTime expiry || expiry <= DateTime.UtcNow || tenant.SubscriptionStatus != "ACTIVE") return (tenant, null);
        var remainingDays = (decimal)(expiry - DateTime.UtcNow).TotalDays;
        var subtotal = Math.Round(extraGb * PricePerGbMonth * remainingDays / 30m, 0, MidpointRounding.AwayFromZero);
        var vat = Math.Round(subtotal * 0.1m, 0, MidpointRounding.AwayFromZero);
        return (tenant, new StorageQuote(extraGb, subtotal, vat, subtotal + vat, expiry));
    }

    [HttpGet("quote")]
    [Authorize(Roles = RoleConstants.StoreOwnerOrAdmin)]
    public async Task<IActionResult> Quote([FromQuery] int extraGb, CancellationToken cancellationToken)
    {
        var (_, quote) = await QuoteAsync(extraGb, cancellationToken);
        return quote == null ? BadRequest(ApiResponse<object>.Fail("INVALID_STORAGE_PURCHASE", "Cần gói trả phí còn hiệu lực và số GB từ 1 đến 100.")) : Ok(ApiResponse<StorageQuote>.Ok(quote));
    }

    [HttpPost("purchase")]
    [Authorize(Roles = RoleConstants.StoreOwnerOrAdmin)]
    public async Task<IActionResult> Purchase([FromBody] StoragePurchaseRequest request, CancellationToken cancellationToken)
    {
        if (request.RequestId == Guid.Empty || request.ExtraGb is < 1 or > 100 || string.IsNullOrWhiteSpace(TenantId)) return BadRequest();
        var code = $"STO-{request.RequestId:N}";
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var previous = await context.WalletTransactions.AsNoTracking().AnyAsync(x => x.TenantId == TenantId && x.TransactionCode == code && x.Status == "SUCCESS", cancellationToken);
        if (previous)
        {
            await transaction.CommitAsync(cancellationToken);
            return Ok(ApiResponse<object>.Ok(new { alreadyProcessed = true }));
        }
        var (tenant, quote) = await QuoteAsync(request.ExtraGb, cancellationToken);
        if (tenant == null || quote == null) return BadRequest(ApiResponse<object>.Fail("INVALID_STORAGE_PURCHASE", "Gói trả phí không còn hiệu lực hoặc số GB không hợp lệ."));
        if (tenant.WalletBalance < quote.Total) return BadRequest(ApiResponse<object>.Fail("INSUFFICIENT_WALLET_BALANCE", "Số dư ví không đủ."));
        var before = tenant.WalletBalance;
        tenant.WalletBalance -= quote.Total;
        tenant.ExtraStorageGb = (tenant.StorageAddonExpiresAt > DateTime.UtcNow ? tenant.ExtraStorageGb : 0) + request.ExtraGb;
        tenant.StorageAddonExpiresAt = quote.ExpiresAt;
        context.WalletTransactions.Add(new WalletTransaction
        {
            TenantId = tenant.Id, TransactionCode = code, Type = "PURCHASE_ADDON", Amount = quote.Total,
            BalanceBefore = before, BalanceAfter = tenant.WalletBalance, PaymentMethod = "WALLET", Status = "SUCCESS",
            Description = $"Mua thêm {request.ExtraGb} GB đến {quote.ExpiresAt:yyyy-MM-dd}"
        });
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Ok(ApiResponse<StorageQuote>.Ok(quote));
    }
}
