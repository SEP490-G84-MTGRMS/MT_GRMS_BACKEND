using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Debt.Dtos;
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
        [FromQuery] string? search,
        [FromQuery] string? tier,
        [FromQuery] string? debtStatus,
        CancellationToken ct)
    {
        var tenantId = _tenantContext.CurrentTenantId;
        var query = _context.Customers
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(c => c.Name.ToLower().Contains(s) || c.Phone.Contains(s) || c.CustomerCode.ToLower().Contains(s));
        }

        if (!string.IsNullOrWhiteSpace(tier) && !tier.Equals("ALL", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(c => c.MembershipTier == tier);
        }

        var customers = await query
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(ct);

        var result = new List<CustomerResponse>();
        foreach (var c in customers)
        {
            var currentDebt = c.TotalDebt;
            var debtLimit = c.DebtLimit;

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
                i.Status == DebtInvoiceStatus.FullyPaid
                    ? "Paid"
                    : i.Status == DebtInvoiceStatus.PartiallyPaid
                        ? "Partial"
                        : "Unpaid"
            ))
            .ToListAsync(ct);

        // Load recent debt repayment history
        var recentPayments = await _context.CustomerDebtEntries
            .AsNoTracking()
            .Where(e => e.CustomerId == id && e.TenantId == tenantId && e.EntryType == CustomerDebtType.DebtRepayment)
            .OrderByDescending(e => e.OccurredAtUtc)
            .Take(10)
            .Select(e => new CustomerPaymentSummaryResponse(
                string.IsNullOrEmpty(e.ReferenceCode) ? e.EntryCode : e.ReferenceCode,
                e.OccurredAtUtc.ToString("yyyy-MM-dd HH:mm"),
                e.Amount,
                e.Description.Contains("Bank") || e.Description.Contains("VietQR") ? "VietQR" : "Cash",
                e.Description,
                "Thu ngân"
            ))
            .ToListAsync(ct);

        var res = new CustomerResponse(
            c.Id,
            c.CustomerCode,
            c.Name,
            c.Phone,
            c.Email,
            c.Address,
            c.MembershipTier,
            c.RewardPoints,
            c.TotalSpending,
            c.TotalDebt,
            c.DebtLimit,
            c.IsBlockedForDebt ? "Inactive" : "Active",
            c.AllowDebt,
            c.IsBlockedForDebt,
            c.CreatedAt,
            openInvoices,
            recentPayments
        );

        return Ok(ApiResponse<CustomerResponse>.Ok(res));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<CustomerResponse>>> CreateCustomer([FromBody] CreateCustomerRequest request, CancellationToken ct)
    {
        var tenantId = _tenantContext.CurrentTenantId;
        if (string.IsNullOrWhiteSpace(request.FullName))
        {
            return BadRequest(ApiResponse<object>.Fail("VALIDATION_ERROR", "Tên khách hàng không được để trống."));
        }
        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            return BadRequest(ApiResponse<object>.Fail("VALIDATION_ERROR", "Số điện thoại không được để trống."));
        }

        var existingPhone = await _context.Customers
            .AnyAsync(c => c.Phone == request.PhoneNumber.Trim() && c.TenantId == tenantId, ct);

        if (existingPhone)
        {
            return Conflict(ApiResponse<object>.Fail("PHONE_EXISTS", "Số điện thoại này đã được sử dụng cho một khách hàng khác."));
        }

        var customerId = Guid.NewGuid();
        var code = "KH" + DateTime.UtcNow.ToString("yyMMddHHmmss") + Random.Shared.Next(10, 99);

        var customer = new Customer
        {
            Id = customerId,
            TenantId = tenantId,
            CustomerCode = code,
            Name = request.FullName.Trim(),
            Phone = request.PhoneNumber.Trim(),
            Email = request.Email?.Trim(),
            Address = request.Address?.Trim(),
            MembershipTier = request.MembershipTier ?? "Standard",
            RewardPoints = 0,
            TotalSpending = 0m,
            TotalDebt = 0m,
            DebtLimit = request.DebtLimit,
            AllowDebt = request.DebtLimit > 0,
            IsBlockedForDebt = false,
            PrepaymentBalance = 0m,
            MaxDebtDays = 30,
            DebtStatus = CustomerDebtStatus.GoodStanding
        };

        await _context.Customers.AddAsync(customer, ct);
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

            if (customer.TotalDebt > customer.DebtLimit)
            {
                customer.DebtStatus = CustomerDebtStatus.LimitExceededBlocked;
                customer.IsBlockedForDebt = true;
            }
            else if (customer.DebtStatus == CustomerDebtStatus.LimitExceededBlocked)
            {
                customer.DebtStatus = CustomerDebtStatus.GoodStanding;
                customer.IsBlockedForDebt = false;
            }
        }

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
            customer.TotalDebt,
            customer.DebtLimit,
            customer.IsBlockedForDebt ? "Inactive" : "Active",
            customer.AllowDebt,
            customer.IsBlockedForDebt,
            customer.CreatedAt
        );

        return Ok(ApiResponse<CustomerResponse>.Ok(response, "Cập nhật khách hàng thành công."));
    }

    [HttpGet("debts/summary")]
    public async Task<ActionResult<ApiResponse<CustomerKpiSummaryResponse>>> GetDebtsSummary(CancellationToken ct)
    {
        var tenantId = _tenantContext.CurrentTenantId;
        var customers = await _context.Customers
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId)
            .ToListAsync(ct);

        var totalCustomers = customers.Count;
        decimal totalDebtAR = customers.Sum(c => c.TotalDebt);
        int overLimitCount = customers.Count(c => c.TotalDebt > c.DebtLimit);
        int totalLoyaltyPoints = customers.Sum(c => c.RewardPoints);

        var summary = new CustomerKpiSummaryResponse(
            totalCustomers,
            totalDebtAR,
            overLimitCount,
            totalLoyaltyPoints
        );

        return Ok(ApiResponse<CustomerKpiSummaryResponse>.Ok(summary));
    }
}

