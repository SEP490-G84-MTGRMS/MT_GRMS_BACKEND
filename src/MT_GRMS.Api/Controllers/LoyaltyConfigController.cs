using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Promotions;
using MT_GRMS.Application.Promotions.Dtos;
using Microsoft.EntityFrameworkCore;
using MT_GRMS.Infrastructure.Persistence;
using MT_GRMS.Domain.Entities.Sales;
using MT_GRMS.Domain.Exceptions;

namespace MT_GRMS.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/loyalty-config")]
public class LoyaltyConfigController : BaseApiController
{
    private readonly ILoyaltyService _loyaltyService;
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;

    public LoyaltyConfigController(ILoyaltyService loyaltyService, AppDbContext dbContext, ITenantContext tenantContext)
    {
        _loyaltyService = loyaltyService;
        _dbContext = dbContext;
        _tenantContext = tenantContext;
    }

    [HttpGet("ledger")]
    public async Task<IActionResult> GetLedger(CancellationToken cancellationToken)
    {
        var config = await _loyaltyService.GetConfigAsync(cancellationToken);
        var orders = await _dbContext.Orders
            .AsNoTracking()
            .Include(order => order.Customer)
            .Where(order => order.TenantId == _tenantContext.CurrentTenantId &&
                            order.CustomerId != null && order.Customer != null &&
                            order.Customer.CustomerCode != "KHACHLE" && order.Customer.Name != "Khách lẻ" &&
                            order.Status != "Cancelled")
            .OrderByDescending(order => order.CreatedAt)
            .Take(500)
            .Select(order => new
            {
                order.Id,
                order.OrderCode,
                order.CustomerId,
                CustomerName = order.Customer!.Name,
                CustomerPhone = order.Customer.Phone,
                CurrentBalance = order.Customer.RewardPoints,
                order.PointsRedeemed,
                order.FinalAmount,
                order.CreatedAt
            })
            .ToListAsync(cancellationToken);

        var balances = orders.GroupBy(order => order.CustomerId!.Value)
            .ToDictionary(group => group.Key, group => group.First().CurrentBalance);
        var result = new List<LoyaltyLedgerDto>();
        foreach (var order in orders)
        {
            var customerId = order.CustomerId!.Value;
            var balanceAfterOrder = balances[customerId];
            var earned = 0;
            if (config.IsEnabled && config.EarningRateAmount > 0)
            {
                earned = (int)Math.Floor(order.FinalAmount / config.EarningRateAmount) * config.EarningPoints;
                if (earned > 0)
                {
                    result.Add(new LoyaltyLedgerDto($"{order.Id}-earn", $"LP-{order.OrderCode}-E", customerId,
                        order.CustomerName, order.CustomerPhone ?? "", "EARN_PURCHASE", earned,
                        balanceAfterOrder, order.OrderCode, "Tích điểm từ đơn hàng", order.CreatedAt, "POS"));
                }
            }
            if (order.PointsRedeemed > 0)
            {
                result.Add(new LoyaltyLedgerDto($"{order.Id}-redeem", $"LP-{order.OrderCode}-R", customerId,
                    order.CustomerName, order.CustomerPhone ?? "", "REDEEM_PURCHASE", -order.PointsRedeemed,
                    balanceAfterOrder - earned, order.OrderCode, "Tiêu điểm khi thanh toán đơn hàng", order.CreatedAt, "POS"));
            }
            balances[customerId] = balanceAfterOrder - earned + order.PointsRedeemed;
        }

        var adjustments = await _dbContext.LoyaltyPointAdjustments
            .AsNoTracking()
            .Include(entry => entry.Customer)
            .OrderByDescending(entry => entry.CreatedAt)
            .Take(500)
            .Select(entry => new LoyaltyLedgerDto(
                entry.Id.ToString(), entry.TransactionCode, entry.CustomerId,
                entry.Customer.Name, entry.Customer.Phone, entry.Type,
                entry.PointsChanged, entry.BalanceAfter, null, entry.Reason,
                entry.CreatedAt, entry.PerformedBy))
            .ToListAsync(cancellationToken);

        result.AddRange(adjustments);
        result = result.OrderByDescending(entry => entry.CreatedAt).Take(500).ToList();

        return Ok(ApiResponse<IReadOnlyList<LoyaltyLedgerDto>>.Ok(result));
    }

    [HttpPost("adjustments")]
    public async Task<IActionResult> AdjustPoints([FromBody] AdjustLoyaltyPointsRequest request, CancellationToken cancellationToken)
    {
        var phone = request.CustomerPhone?.Trim() ?? string.Empty;
        var type = request.AdjustmentType?.Trim().ToUpperInvariant() ?? string.Empty;
        var reason = request.Reason?.Trim() ?? string.Empty;
        if (phone.Length == 0 || reason.Length == 0 || request.Points <= 0 || (type != "ADD" && type != "SUB"))
            throw new BusinessException("INVALID_POINT_ADJUSTMENT", "Số điện thoại, số điểm dương, loại điều chỉnh và lý do là bắt buộc.");
        if (reason.Length > 500)
            throw new BusinessException("INVALID_POINT_ADJUSTMENT", "Lý do điều chỉnh không được vượt quá 500 ký tự.");

        var customer = await _dbContext.Customers
            .FirstOrDefaultAsync(item => item.Phone == phone && item.CustomerCode != "KHACHLE", cancellationToken);
        if (customer == null)
            throw new NotFoundException("Khách hàng có số điện thoại", phone);

        var changed = type == "ADD" ? request.Points : -request.Points;
        if (customer.RewardPoints + changed < 0)
            throw new BusinessException("INSUFFICIENT_POINTS", $"Khách hàng chỉ có {customer.RewardPoints:N0} điểm, không thể trừ {request.Points:N0} điểm.");

        customer.RewardPoints += changed;
        var now = DateTime.UtcNow;
        var entry = new LoyaltyPointAdjustment
        {
            CustomerId = customer.Id,
            TransactionCode = $"LP-ADJ-{now:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
            Type = type == "ADD" ? "MANUAL_ADJUST_ADD" : "MANUAL_ADJUST_SUB",
            PointsChanged = changed,
            BalanceAfter = customer.RewardPoints,
            Reason = reason,
            PerformedBy = User.Identity?.Name ?? "Store Owner"
        };
        _dbContext.LoyaltyPointAdjustments.Add(entry);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var dto = new LoyaltyLedgerDto(entry.Id.ToString(), entry.TransactionCode, customer.Id,
            customer.Name, customer.Phone, entry.Type, entry.PointsChanged, entry.BalanceAfter,
            null, entry.Reason, entry.CreatedAt, entry.PerformedBy);
        return Ok(ApiResponse<LoyaltyLedgerDto>.Ok(dto, "Điều chỉnh điểm thành công"));
    }

    /// <summary>
    /// Lấy cấu hình hệ thống tích điểm và tiêu điểm thành viên (REQ-LOYALTY-01)
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetConfig(CancellationToken cancellationToken)
    {
        var config = await _loyaltyService.GetConfigAsync(cancellationToken);
        return Ok(ApiResponse<LoyaltyConfigDto>.Ok(config));
    }

    /// <summary>
    /// Cập nhật cấu hình tích điểm và tiêu điểm thành viên (REQ-LOYALTY-01)
    /// </summary>
    [HttpPut]
    public async Task<IActionResult> UpdateConfig([FromBody] UpdateLoyaltyConfigRequest request, CancellationToken cancellationToken)
    {
        var updated = await _loyaltyService.UpdateConfigAsync(request, cancellationToken);
        return Ok(ApiResponse<LoyaltyConfigDto>.Ok(updated, "Cập nhật cấu hình tích điểm thành công"));
    }
}

