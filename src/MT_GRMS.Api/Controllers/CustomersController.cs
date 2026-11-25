using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Debt.Dtos;
using MT_GRMS.Domain.Constants;
using MT_GRMS.Domain.Entities.Sales;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CustomersController : BaseApiController
{
    private readonly AppDbContext _context;
    private readonly ITenantContext _tenantContext;

    public CustomersController(AppDbContext context, ITenantContext tenantContext)
    {
        _context = context;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<CustomerResponse>>>> GetCustomers(
        [FromQuery] string? search, [FromQuery] string? tier, [FromQuery] string? debtStatus, CancellationToken ct)
    {
        var tenantId = _tenantContext.CurrentTenantId;
        var query = _context.Customers.AsNoTracking().Where(c => c.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var keyword = search.Trim().ToLower();
            var customerIdsFromDebt = await _context.Orders.AsNoTracking()
                .Where(o => o.TenantId == tenantId && o.CustomerId.HasValue &&
                    o.OrderCode.ToLower().Contains(keyword) &&
                    (o.PaymentMethod == "DEBT" || o.PaymentMethod == "CREDIT"))
                .Select(o => o.CustomerId!.Value).Distinct().ToListAsync(ct);
            query = query.Where(c => c.Name.ToLower().Contains(keyword) || c.Phone.Contains(keyword) ||
                c.CustomerCode.ToLower().Contains(keyword) || customerIdsFromDebt.Contains(c.Id));
        }

        if (!string.IsNullOrWhiteSpace(tier) && !tier.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            var normalizedTier = CanonicalTiers.TryGetValue(tier.Trim(), out var canonicalTier)
                ? canonicalTier
                : tier.Trim();
            var normalizedTierLower = normalizedTier.ToLower();
            query = query.Where(c => c.MembershipTier.ToLower() == normalizedTierLower);
        }
        if (debtStatus is not null && debtStatus.Equals("NO_DEBT", StringComparison.OrdinalIgnoreCase))
            query = query.Where(c => c.TotalDebt <= 0);
        else if (debtStatus is not null && !debtStatus.Equals("ALL", StringComparison.OrdinalIgnoreCase))
            query = query.Where(c => c.TotalDebt > 0);

        var customers = await query.OrderByDescending(c => c.CreatedAt).ToListAsync(ct);
        var customerIds = customers.Select(c => c.Id).ToList();
        var debtStats = await _context.Orders.AsNoTracking()
            .Where(o => o.TenantId == tenantId && o.CustomerId.HasValue && customerIds.Contains(o.CustomerId.Value)
                && (o.PaymentMethod == "DEBT" || o.PaymentMethod == "CREDIT")
                && o.Status == "Completed" && !o.IsDeleted && o.FinalAmount > 0)
            .GroupBy(o => o.CustomerId!.Value)
            .Select(g => new { CustomerId = g.Key, Count = g.Count(), Total = g.Sum(o => o.FinalAmount) })
            .ToDictionaryAsync(x => x.CustomerId, ct);
        return Ok(ApiResponse<List<CustomerResponse>>.Ok(customers.Select(c =>
            MapCustomer(c, debtOrderCount: debtStats.GetValueOrDefault(c.Id)?.Count ?? 0,
                totalDebtPurchased: debtStats.GetValueOrDefault(c.Id)?.Total ?? 0m)).ToList()));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<CustomerResponse>>> GetCustomerById(Guid id, CancellationToken ct)
    {
        var tenantId = _tenantContext.CurrentTenantId;
        var customer = await _context.Customers.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId, ct);
        if (customer == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Khách hàng không tồn tại."));

        var debtOrders = await _context.Orders.AsNoTracking()
            .Where(o => o.CustomerId == id && o.TenantId == tenantId && o.Status == "Completed" && !o.IsDeleted &&
                (o.PaymentMethod == "DEBT" || o.PaymentMethod == "CREDIT"))
            .OrderByDescending(o => o.OrderDate).Take(10)
            .Select(o => new CustomerInvoiceSummaryResponse(o.OrderCode, o.OrderDate.ToString("yyyy-MM-dd"),
                o.FinalAmount, "Debt"))
            .ToListAsync(ct);

        var rawRecentPayments = await _context.CashbookEntries.AsNoTracking()
            .Where(e => e.SourceId == id && e.TenantId == tenantId && !e.IsDeleted &&
                (e.SourceType == "DEBT_REPAYMENT" || e.SourceType == "DEBT") &&
                e.EntryType != CashbookEntryType.Payment)
            .OrderByDescending(e => e.OccurredAtUtc)
            .ToListAsync(ct);
        var paymentIds = rawRecentPayments.Select(e => e.Id).ToList();
        var reversedPaymentIds = paymentIds.Count == 0
            ? new HashSet<Guid>()
            : (await _context.CashbookEntries.AsNoTracking()
                .Where(r => r.TenantId == tenantId && !r.IsDeleted &&
                    (r.SourceType == "COMPENSATING" || r.SourceType == "REVERSAL") &&
                    ((r.SourceId.HasValue && paymentIds.Contains(r.SourceId.Value)) ||
                     (r.ReversedEntryId.HasValue && paymentIds.Contains(r.ReversedEntryId.Value))))
                .Select(r => new { r.SourceId, r.ReversedEntryId })
                .ToListAsync(ct))
                .SelectMany(r => new[] { r.SourceId, r.ReversedEntryId })
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .ToHashSet();
        var recentPayments = rawRecentPayments
            .Where(e => !reversedPaymentIds.Contains(e.Id))
            .Take(10)
            .Select(e => new CustomerPaymentSummaryResponse(
                string.IsNullOrEmpty(e.ReferenceCode) ? e.EntryCode : e.ReferenceCode,
                e.OccurredAtUtc.ToString("yyyy-MM-dd HH:mm"), e.Amount,
                e.AccountType.ToString(), "Chủ cửa hàng", e.Description))
            .ToList();

        var debtStats = await _context.Orders.AsNoTracking()
            .Where(o => o.CustomerId == id && o.TenantId == tenantId && o.Status == "Completed" && !o.IsDeleted
                && (o.PaymentMethod == "DEBT" || o.PaymentMethod == "CREDIT") && o.FinalAmount > 0)
            .GroupBy(o => o.CustomerId!.Value)
            .Select(g => new { Count = g.Count(), Total = g.Sum(o => o.FinalAmount) })
            .FirstOrDefaultAsync(ct);
        return Ok(ApiResponse<CustomerResponse>.Ok(MapCustomer(customer, debtOrders, recentPayments,
            debtStats?.Count ?? 0, debtStats?.Total ?? 0m)));
    }

    private static readonly Dictionary<string, string> CanonicalTiers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Standard"] = "Standard",
        ["Silver"] = "Silver",
        ["Gold"] = "Gold",
        ["Platinum"] = "Platinum",
        ["VIP"] = "VIP",
        ["Bronze"] = "Bronze",
        ["Diamond"] = "Diamond"
    };

    private static readonly System.Text.RegularExpressions.Regex VnPhoneRegex =
        new(@"^(0|\+84)[35789]\d{8}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex EmailRegex =
        new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", System.Text.RegularExpressions.RegexOptions.Compiled);

    [HttpPost]
    public async Task<ActionResult<ApiResponse<CustomerResponse>>> CreateCustomer(
        [FromBody] CreateCustomerRequest request, CancellationToken ct)
    {
        var tenantId = _tenantContext.CurrentTenantId;
        var cleanName = CleanText(request.FullName);
        if (string.IsNullOrWhiteSpace(cleanName) || cleanName.Length > 100 || request.FullName.Contains('<') || request.FullName.Contains('>'))
            return BadRequest(ApiResponse<object>.Fail("VALIDATION_ERROR", "Tên khách hàng không hợp lệ (tối đa 100 ký tự)."));
        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
            return BadRequest(ApiResponse<object>.Fail("VALIDATION_ERROR", "Số điện thoại không được để trống."));

        var phone = request.PhoneNumber.Trim();
        if (phone.Length > 20 || !VnPhoneRegex.IsMatch(phone))
            return BadRequest(ApiResponse<object>.Fail("VALIDATION_ERROR", "Số điện thoại Việt Nam không đúng định dạng (10 chữ số, bắt đầu 03/05/07/08/09)."));

        var email = request.Email?.Trim();
        if (!string.IsNullOrWhiteSpace(email) && (email.Length > 100 || !EmailRegex.IsMatch(email)))
            return BadRequest(ApiResponse<object>.Fail("VALIDATION_ERROR", "Địa chỉ email không hợp lệ."));

        var address = CleanText(request.Address);
        if (address != null && address.Length > 255)
            return BadRequest(ApiResponse<object>.Fail("VALIDATION_ERROR", "Địa chỉ không được vượt quá 255 ký tự."));

        var isStoreOwner =
            (User?.IsInRole(SystemRoles.StoreOwner) == true) ||
            string.Equals(_tenantContext.CurrentRole, SystemRoles.StoreOwner, StringComparison.OrdinalIgnoreCase);
        var requestedTier = request.MembershipTier?.Trim();
        if (!isStoreOwner && !string.IsNullOrWhiteSpace(requestedTier) &&
            !requestedTier.Equals("Standard", StringComparison.OrdinalIgnoreCase))
        {
            return StatusCode(403, ApiResponse<object>.Fail(
                "FORBIDDEN",
                "Chỉ chủ cửa hàng mới có quyền gán hạng thành viên cho khách hàng.",
                403));
        }

        var rawTier = !isStoreOwner || string.IsNullOrWhiteSpace(requestedTier) ? "Standard" : requestedTier;
        if (rawTier.Length > 30 || !CanonicalTiers.TryGetValue(rawTier, out var tier))
            return BadRequest(ApiResponse<object>.Fail("VALIDATION_ERROR", "Hạng thành viên không hợp lệ."));

        if (await _context.Customers.AnyAsync(c => c.Phone == phone && c.TenantId == tenantId, ct))
            return Conflict(ApiResponse<object>.Fail("PHONE_EXISTS", "Số điện thoại này đã được sử dụng cho một khách hàng khác."));

        var customer = new Customer
        {
            TenantId = tenantId,
            CustomerCode = "KH" + DateTime.UtcNow.ToString("yyMMddHHmmss") + Random.Shared.Next(10, 99),
            Name = cleanName,
            Phone = phone,
            Email = email,
            Address = address,
            MembershipTier = tier,
            TotalDebt = 0m
        };
        await _context.Customers.AddAsync(customer, ct);
        await _context.SaveChangesAsync(ct);
        return Ok(ApiResponse<CustomerResponse>.Ok(MapCustomer(customer), "Tạo khách hàng thành công."));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = SystemRoles.StoreOwner)]
    public async Task<ActionResult<ApiResponse<CustomerResponse>>> UpdateCustomer(
        Guid id, [FromBody] UpdateCustomerRequest request, CancellationToken ct)
    {
        var tenantId = _tenantContext.CurrentTenantId;
        var customer = await _context.Customers.FirstOrDefaultAsync(c => c.Id == id && c.TenantId == tenantId, ct);
        if (customer == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Khách hàng không tồn tại."));

        if (request.FullName != null)
        {
            var cleanName = CleanText(request.FullName);
            if (string.IsNullOrWhiteSpace(cleanName) || cleanName.Length > 100 || request.FullName.Contains('<') || request.FullName.Contains('>'))
                return BadRequest(ApiResponse<object>.Fail("VALIDATION_ERROR", "Tên khách hàng không hợp lệ (tối đa 100 ký tự)."));
            customer.Name = cleanName;
        }
        if (request.Email != null)
        {
            var email = request.Email.Trim();
            if (!string.IsNullOrWhiteSpace(email) && (email.Length > 100 || !EmailRegex.IsMatch(email)))
                return BadRequest(ApiResponse<object>.Fail("VALIDATION_ERROR", "Địa chỉ email không hợp lệ."));
            customer.Email = email;
        }
        if (request.Address != null)
        {
            var address = CleanText(request.Address);
            if (address != null && address.Length > 255)
                return BadRequest(ApiResponse<object>.Fail("VALIDATION_ERROR", "Địa chỉ không được vượt quá 255 ký tự."));
            customer.Address = address;
        }
        if (request.MembershipTier != null)
        {
            var rawTier = request.MembershipTier.Trim();
            if (rawTier.Length > 30 || !CanonicalTiers.TryGetValue(rawTier, out var canonicalTier))
                return BadRequest(ApiResponse<object>.Fail("VALIDATION_ERROR", "Hạng thành viên không hợp lệ."));
            customer.MembershipTier = canonicalTier;
        }

        await _context.SaveChangesAsync(ct);
        return Ok(ApiResponse<CustomerResponse>.Ok(MapCustomer(customer), "Cập nhật khách hàng thành công."));
    }

    [HttpGet("debts/summary")]
    [Authorize(Roles = SystemRoles.StoreOwner)]
    public async Task<ActionResult<ApiResponse<CustomerKpiSummaryResponse>>> GetDebtsSummary(CancellationToken ct)
    {
        var tenantId = _tenantContext.CurrentTenantId;
        var customers = await _context.Customers.AsNoTracking().Where(c => c.TenantId == tenantId).ToListAsync(ct);
        var summary = new CustomerKpiSummaryResponse(customers.Count, customers.Sum(c => c.TotalDebt),
            customers.Count(c => c.TotalDebt > 0), customers.Sum(c => c.RewardPoints));
        return Ok(ApiResponse<CustomerKpiSummaryResponse>.Ok(summary));
    }

    private static string? CleanText(string? value) => value == null
        ? null
        : System.Text.RegularExpressions.Regex.Replace(value, @"<[^>]*>", string.Empty).Trim();

    private static CustomerResponse MapCustomer(Customer c,
        List<CustomerInvoiceSummaryResponse>? invoices = null,
        List<CustomerPaymentSummaryResponse>? payments = null,
        int debtOrderCount = 0,
        decimal totalDebtPurchased = 0m) => new(
        c.Id, c.CustomerCode, c.Name, c.Phone, c.Email, c.Address, c.MembershipTier,
        c.RewardPoints, c.TotalSpending, c.TotalDebt, "Active", c.CreatedAt, invoices, payments,
        debtOrderCount, totalDebtPurchased);
}
