using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Common;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.Api.Controllers;

[ApiController]
[Route("api/customers")]
[Route("api/v1/customers")]
public class CustomersController : BaseApiController
{
    private readonly AppDbContext _context;

    public CustomersController(AppDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Lấy danh sách toàn bộ khách hàng thành viên phục vụ quầy bán hàng POS
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? search, CancellationToken cancellationToken)
    {
        var query = _context.Customers.Where(c => !c.IsDeleted).AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var trimmed = search.Trim();
            query = query.Where(c => c.Name.Contains(trimmed) || c.Phone.Contains(trimmed) || c.CustomerCode.Contains(trimmed));
        }

        var list = await query
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new
            {
                id = c.Id,
                code = c.CustomerCode,
                name = c.Name,
                phone = c.Phone,
                email = c.Email,
                address = c.Address,
                membershipTier = c.MembershipTier,
                loyaltyPoints = c.RewardPoints,
                totalSpending = c.TotalSpending,
                currentDebt = c.TotalDebt,
                debtLimit = c.DebtLimit,
                allowDebt = c.AllowDebt,
                isBlockedForDebt = c.IsBlockedForDebt
            })
            .ToListAsync(cancellationToken);

        return Ok(ApiResponse<object>.Ok(list));
    }

    /// <summary>
    /// Lấy chi tiết khách hàng theo ID
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        var c = await _context.Customers.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, cancellationToken);
        if (c == null)
        {
            return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Không tìm thấy khách hàng."));
        }

        return Ok(ApiResponse<object>.Ok(new
        {
            id = c.Id,
            code = c.CustomerCode,
            name = c.Name,
            phone = c.Phone,
            email = c.Email,
            address = c.Address,
            membershipTier = c.MembershipTier,
            loyaltyPoints = c.RewardPoints,
            totalSpending = c.TotalSpending,
            currentDebt = c.TotalDebt,
            debtLimit = c.DebtLimit,
            allowDebt = c.AllowDebt,
            isBlockedForDebt = c.IsBlockedForDebt
        }));
    }
}
