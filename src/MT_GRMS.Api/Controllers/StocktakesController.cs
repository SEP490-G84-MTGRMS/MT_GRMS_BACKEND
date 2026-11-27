using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Stocktakes;
using MT_GRMS.Application.Stocktakes.Dtos;
using MT_GRMS.Application.SupplierReturns.Dtos;

namespace MT_GRMS.Api.Controllers;

[ApiController]
[Route("api/stocktakes")]
[Authorize(Policy = "TenantOwnerOrAssignedBranchStaff")]
public class StocktakesController : BaseApiController
{
    private readonly IStocktakeService _service;
    private readonly ITenantContext _tenantContext;
    private readonly IAuthorizationService _authorizationService;

    public StocktakesController(
        IStocktakeService service,
        ITenantContext tenantContext,
        IAuthorizationService authorizationService)
    {
        _service = service;
        _tenantContext = tenantContext;
        _authorizationService = authorizationService;
    }

    [HttpGet]
    [Authorize(Policy = "TenantOwnerOrAssignedBranchStaff")]
    public async Task<IActionResult> GetPaged(
        [FromQuery] StocktakeFilterQuery query,
        CancellationToken cancellationToken)
    {
        var effectiveBranchId = query.BranchId ?? (User.IsInRole("STORE_OWNER") ? null : _tenantContext.CurrentBranchId);
        var scope = new TenantBranchScope(_tenantContext.CurrentTenantId, effectiveBranchId);
        var authResult = await _authorizationService.AuthorizeAsync(User, scope, "TenantOwnerOrAssignedBranchStaff");
        if (!authResult.Succeeded) return Forbid();

        var result = await _service.GetPagedAsync(query, cancellationToken);
        return Ok(ApiResponse<PagedResult<StocktakeListItemDto>>.Ok(result));
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = "TenantOwnerOrAssignedBranchStaff")]
    public async Task<IActionResult> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await _service.GetByIdAsync(id, cancellationToken);
        var scope = new TenantBranchScope(_tenantContext.CurrentTenantId, result.BranchId);
        var authResult = await _authorizationService.AuthorizeAsync(User, scope, "TenantOwnerOrAssignedBranchStaff");
        if (!authResult.Succeeded) return Forbid();

        return Ok(ApiResponse<StocktakeDetailDto>.Ok(result));
    }

    [HttpPost]
    [Authorize(Policy = "TenantOwnerOrAssignedBranchStaff")]
    public async Task<IActionResult> Create(
        [FromBody] CreateStocktakeCommand command,
        CancellationToken cancellationToken)
    {
        var scope = new TenantBranchScope(_tenantContext.CurrentTenantId, command.BranchId);
        var authResult = await _authorizationService.AuthorizeAsync(User, scope, "TenantOwnerOrAssignedBranchStaff");
        if (!authResult.Succeeded) return Forbid();

        var result = await _service.CreateAsync(command, cancellationToken);
        return CreatedAtAction(
            nameof(GetById),
            new { id = result.Id },
            ApiResponse<StocktakeDetailDto>.Ok(result, "Tạo phiếu kiểm kê hàng hóa thành công!"));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "TenantOwnerOrAssignedBranchStaff")]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateStocktakeCommand command,
        CancellationToken cancellationToken)
    {
        var existing = await _service.GetByIdAsync(id, cancellationToken);
        var scope = new TenantBranchScope(_tenantContext.CurrentTenantId, existing.BranchId);
        var authResult = await _authorizationService.AuthorizeAsync(User, scope, "TenantOwnerOrAssignedBranchStaff");
        if (!authResult.Succeeded) return Forbid();

        var result = await _service.UpdateAsync(id, command, cancellationToken);
        return Ok(ApiResponse<StocktakeDetailDto>.Ok(result, "Đã cập nhật phiếu kiểm kê thành công."));
    }

    [HttpPost("{id:guid}/submit")]
    [Authorize(Policy = "TenantOwnerOrAssignedBranchStaff")]
    public async Task<IActionResult> Submit(
        Guid id,
        CancellationToken cancellationToken)
    {
        var existing = await _service.GetByIdAsync(id, cancellationToken);
        var scope = new TenantBranchScope(_tenantContext.CurrentTenantId, existing.BranchId);
        var authResult = await _authorizationService.AuthorizeAsync(User, scope, "TenantOwnerOrAssignedBranchStaff");
        if (!authResult.Succeeded) return Forbid();

        var result = await _service.SubmitAsync(id, cancellationToken);
        return Ok(ApiResponse<StocktakeDetailDto>.Ok(result, "Đã gửi phiếu kiểm kê lên Store Owner phê duyệt."));
    }

    [HttpPost("{id:guid}/review")]
    [Authorize(Policy = "StoreOwnerOnly")]
    public async Task<IActionResult> Review(
        Guid id,
        [FromBody] ReviewStocktakeCommand command,
        CancellationToken cancellationToken)
    {
        var result = await _service.ReviewAsync(id, command, cancellationToken);
        return Ok(ApiResponse<StocktakeDetailDto>.Ok(result, "Đã ghi nhận kết quả phê duyệt phiếu kiểm kê."));
    }

    [HttpPost("{id:guid}/create-return")]
    [Authorize(Policy = "TenantOwnerOrAssignedBranchStaff")]
    public async Task<IActionResult> CreateReturnFromStocktake(
        Guid id,
        [FromBody] CreateReturnFromStocktakeCommand command,
        CancellationToken cancellationToken)
    {
        var existing = await _service.GetByIdAsync(id, cancellationToken);
        var scope = new TenantBranchScope(_tenantContext.CurrentTenantId, existing.BranchId);
        var authResult = await _authorizationService.AuthorizeAsync(User, scope, "TenantOwnerOrAssignedBranchStaff");
        if (!authResult.Succeeded) return Forbid();

        var result = await _service.CreateReturnFromStocktakeAsync(id, command, cancellationToken);
        return Ok(ApiResponse<object>.Ok(result, "Đã tạo phiếu yêu cầu trả hàng nhà cung cấp từ phiếu kiểm kê."));
    }
}

