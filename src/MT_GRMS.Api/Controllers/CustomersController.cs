using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Debt.Dtos;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Entities.Sales;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.Api.Controllers;

[Route("api/customers")]
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
        [FromQuery] string? search,
        [FromQuery] string? tier,
        [FromQuery] string? debtStatus,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var tenantId = _tenantContext.CurrentTenantId;
        var query = _context.Customers
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(c =>
                c.Phone.ToLower().Contains(s) ||
                c.Name.ToLower().Contains(s) ||
                c.CustomerCode.ToLower().Contains(s));
        }

        if (!string.IsNullOrWhiteSpace(tier) && !tier.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(c => c.MembershipTier == tier);
        }

        var customers = await query
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(ct);

        // Load profiles to get real-time debt balance
        var customerIds = customers.Select(c => c.Id).ToList();
        var profiles = await _context.CustomerDebtProfiles
            .AsNoTracking()
            .Where(p => p.TenantId == tenantId && customerIds.Contains(p.CustomerId))
            .ToDictionaryAsync(p => p.CustomerId, ct);

        var result = new List<CustomerResponse>();
        foreach (var c in customers)
        {
            profiles.TryGetValue(c.Id, out var profile);
            var currentDebt = profile?.CurrentDebtBalance ?? c.TotalDebt;
            var debtLimit = profile?.CreditLimit > 0 ? profile.CreditLimit : c.DebtLimit;

            // Apply debt status filter
            if (!string.IsNullOrWhiteSpace(debtStatus) && !debtStatus.Equals("ALL", StringComparison.OrdinalIgnoreCase))
            {
                if (debtStatus.Equals("NO_DEBT", StringComparison.OrdinalIgnoreCase) && currentDebt > 0)
                    continue;
                if (debtStatus.Equals("IN_LIMIT", StringComparison.OrdinalIgnoreCase) && (currentDebt <= 0 || currentDebt > debtLimit))
                    continue;
                if (debtStatus.Equals("OVER_LIMIT", StringComparison.OrdinalIgnoreCase) && currentDebt <= debtLimit)
                    continue;
            }

            result.Add(new CustomerResponse(
                c.Id,
                c.CustomerCode,
                c.Name,
                c.Phone,
                c.Email,
                c.Address,
                c.MembershipTier,
                c.RewardPoints,
                c.TotalSpending,
                currentDebt,
                debtLimit,
                c.IsBlockedForDebt ? "Inactive" : "Active",
                c.AllowDebt,
                c.IsBlockedForDebt,
                c.CreatedAt
            ));
        }

        return Ok(ApiResponse<List<CustomerResponse>>.Ok(result));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<CustomerResponse>>> GetCustomerById(Guid id, CancellationToken ct)
    {
        var tenantId = _tenantContext.CurrentTenantId;
        var c = await _context.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenantId, ct);

        if (c == null)
        {
            return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Khách hàng không tồn tại."));
        }

        var profile = await _context.CustomerDebtProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.CustomerId == id && p.TenantId == tenantId, ct);

        var currentDebt = profile?.CurrentDebtBalance ?? c.TotalDebt;
        var debtLimit = profile?.CreditLimit > 0 ? profile.CreditLimit : c.DebtLimit;

        // Load open invoices
        var openInvoices = await _context.CustomerDebtInvoices
            .AsNoTracking()
            .Where(i => i.CustomerId == id && i.TenantId == tenantId)
            .OrderByDescending(i => i.CreatedAt)
            .Take(10)
            .Select(i => new CustomerInvoiceSummaryResponse(
                i.OrderCode,
                i.CreatedAt.ToString("yyyy-MM-dd"),
                i.TotalDebtAmount,
                i.Status.ToString()
            ))
            .ToListAsync(ct);

        // Load recent debt repayments
        var payments = await _context.CustomerDebtEntries
            .AsNoTracking()
            .Where(e => e.CustomerId == id && e.TenantId == tenantId && e.EntryType == CustomerDebtType.DebtRepayment)
            .OrderByDescending(e => e.OccurredAtUtc)
            .Take(10)
            .Select(e => new CustomerPaymentSummaryResponse(
                e.EntryCode,
                e.OccurredAtUtc.ToString("yyyy-MM-dd"),
                e.Amount,
                e.Description.Contains("VietQR") ? "VietQR" : "Cash",
                "Thu ngân",
                e.Description
            ))
            .ToListAsync(ct);

        var response = new CustomerResponse(
            c.Id,
            c.CustomerCode,
            c.Name,
            c.Phone,
            c.Email,
            c.Address,
            c.MembershipTier,
            c.RewardPoints,
            c.TotalSpending,
            currentDebt,
            debtLimit,
            c.IsBlockedForDebt ? "Inactive" : "Active",
            c.AllowDebt,
            c.IsBlockedForDebt,
            c.CreatedAt,
            openInvoices,
            payments
        );

        return Ok(ApiResponse<CustomerResponse>.Ok(response));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<CustomerResponse>>> CreateCustomer([FromBody] CreateCustomerRequest request, CancellationToken ct)
    {
        var tenantId = _tenantContext.CurrentTenantId;
        var phone = request.PhoneNumber.Trim();

        var exists = await _context.Customers
            .AnyAsync(c => c.TenantId == tenantId && c.Phone == phone, ct);

        if (exists)
        {
            return BadRequest(ApiResponse<object>.Fail("DUPLICATE_PHONE", $"Số điện thoại '{phone}' đã được đăng ký bởi khách hàng khác."));
        }

        var customerId = Guid.NewGuid();
        var code = "KH-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");

        var customer = new Customer
        {
            Id = customerId,
            TenantId = tenantId,
            CustomerCode = code,
            Name = request.FullName.Trim(),
            Phone = phone,
            Email = request.Email?.Trim(),
            Address = request.Address?.Trim(),
            MembershipTier = request.MembershipTier ?? "Standard",
            DebtLimit = request.DebtLimit,
            AllowDebt = request.DebtLimit > 0,
            CreatedAt = DateTime.UtcNow
        };

        await _context.Customers.AddAsync(customer, ct);

        // Initialize CustomerDebtProfile
        var profile = new CustomerDebtProfile
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CustomerId = customerId,
            CreditLimit = request.DebtLimit,
            MaxDebtDays = 30,
            CurrentDebtBalance = 0m,
            PrepaymentBalance = 0m,
            DebtStatus = CustomerDebtStatus.GoodStanding
        };

        await _context.CustomerDebtProfiles.AddAsync(profile, ct);
        await _context.SaveChangesAsync(ct);

        var response = new CustomerResponse(
            customer.Id,
            customer.CustomerCode,
            customer.Name,
            customer.Phone,
            customer.Email,
            customer.Address,
            customer.MembershipTier,
            customer.RewardPoints,
            customer.TotalSpending,
            0m,
            customer.DebtLimit,
            "Active",
            customer.AllowDebt,
            false,
            customer.CreatedAt
        );

        return Ok(ApiResponse<CustomerResponse>.Ok(response, "Tạo khách hàng thành công."));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ApiResponse<CustomerResponse>>> UpdateCustomer(Guid id, [FromBody] UpdateCustomerRequest request, CancellationToken ct)
    {
        var tenantId = _tenantContext.CurrentTenantId;
        var customer = await _context.Customers
            .FirstOrDefaultAsync(c => c.Id == id && c.TenantId == tenantId, ct);

        if (customer == null)
        {
            return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Khách hàng không tồn tại."));
        }

        if (request.FullName != null) customer.Name = request.FullName.Trim();
        if (request.Email != null) customer.Email = request.Email.Trim();
        if (request.Address != null) customer.Address = request.Address.Trim();
        if (request.MembershipTier != null) customer.MembershipTier = request.MembershipTier;
        if (request.Status != null) customer.IsBlockedForDebt = request.Status.Equals("Inactive", StringComparison.OrdinalIgnoreCase);

        if (request.DebtLimit.HasValue)
        {
            customer.DebtLimit = request.DebtLimit.Value;
            customer.AllowDebt = customer.DebtLimit > 0;

            var profile = await _context.CustomerDebtProfiles
                .FirstOrDefaultAsync(p => p.CustomerId == id && p.TenantId == tenantId, ct);

            if (profile != null)
            {
                profile.CreditLimit = customer.DebtLimit;
                if (profile.CurrentDebtBalance > profile.CreditLimit)
                {
                    profile.DebtStatus = CustomerDebtStatus.LimitExceededBlocked;
                }
                else if (profile.DebtStatus == CustomerDebtStatus.LimitExceededBlocked)
                {
                    profile.DebtStatus = CustomerDebtStatus.GoodStanding;
                }
            }
        }

        await _context.SaveChangesAsync(ct);

        var updatedProfile = await _context.CustomerDebtProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.CustomerId == id && p.TenantId == tenantId, ct);

        var currentDebt = updatedProfile?.CurrentDebtBalance ?? customer.TotalDebt;
        var limit = updatedProfile?.CreditLimit > 0 ? updatedProfile.CreditLimit : customer.DebtLimit;

        var response = new CustomerResponse(
            customer.Id,
            customer.CustomerCode,
            customer.Name,
            customer.Phone,
            customer.Email,
            customer.Address,
            customer.MembershipTier,
            customer.RewardPoints,
            customer.TotalSpending,
            currentDebt,
            limit,
            customer.IsBlockedForDebt ? "Inactive" : "Active",
            customer.AllowDebt,
            customer.IsBlockedForDebt,
            customer.CreatedAt
        );

        return Ok(ApiResponse<CustomerResponse>.Ok(response, "Cập nhật thông tin khách hàng thành công."));
    }

    [HttpGet("kpi-summary")]
    public async Task<ActionResult<ApiResponse<CustomerKpiSummaryResponse>>> GetKpiSummary(CancellationToken ct)
    {
        var tenantId = _tenantContext.CurrentTenantId;
        var customers = await _context.Customers
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId)
            .ToListAsync(ct);

        var customerIds = customers.Select(c => c.Id).ToList();
        var profiles = await _context.CustomerDebtProfiles
            .AsNoTracking()
            .Where(p => p.TenantId == tenantId && customerIds.Contains(p.CustomerId))
            .ToDictionaryAsync(p => p.CustomerId, ct);

        var totalCustomers = customers.Count;
        decimal totalDebtAR = 0;
        int overLimitCount = 0;
        int totalLoyaltyPoints = 0;

        foreach (var c in customers)
        {
            profiles.TryGetValue(c.Id, out var profile);
            var debt = profile?.CurrentDebtBalance ?? c.TotalDebt;
            var limit = profile?.CreditLimit > 0 ? profile.CreditLimit : c.DebtLimit;

            totalDebtAR += debt;
            if (debt > limit) overLimitCount++;
            totalLoyaltyPoints += c.RewardPoints;
        }

        var summary = new CustomerKpiSummaryResponse(
            totalCustomers,
            totalDebtAR,
            overLimitCount,
            totalLoyaltyPoints
        );

        return Ok(ApiResponse<CustomerKpiSummaryResponse>.Ok(summary));
    }
}
