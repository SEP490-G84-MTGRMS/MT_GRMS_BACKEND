using Microsoft.AspNetCore.Mvc;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.PurchaseOrders;
using MT_GRMS.Application.PurchaseOrders.Dispatch;
using MT_GRMS.Application.PurchaseOrders.Dtos;

namespace MT_GRMS.Api.Controllers;

[ApiController]
[Route("api/purchase-orders")]
public class PurchaseOrdersController : BaseApiController
{
    private readonly IPurchaseOrderService _poService;
    private readonly ISupplierDispatchPackageService _dispatchPackageService;

    public PurchaseOrdersController(
        IPurchaseOrderService poService,
        ISupplierDispatchPackageService dispatchPackageService)
    {
        _poService = poService;
        _dispatchPackageService = dispatchPackageService;
    }

    /// <summary>
    /// Lấy danh sách các đơn đặt hàng nhập kho PO có phân trang & bộ lọc (REQ-PO-01)
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetPurchaseOrders([FromQuery] PurchaseOrderFilterQuery query, CancellationToken cancellationToken)
    {
        var result = await _poService.GetPagedPurchaseOrdersAsync(query, cancellationToken);
        return Ok(ApiResponse<PagedResult<PurchaseOrderDto>>.Ok(result));
    }

    /// <summary>
    /// Lấy chi tiết một đơn đặt hàng PO theo ID (kèm danh sách mặt hàng đặt mua)
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _poService.GetPurchaseOrderByIdAsync(id, cancellationToken);
        return Ok(ApiResponse<PurchaseOrderDto>.Ok(result));
    }

    /// <summary>
    /// Lấy dữ liệu lưới ma trận đặt hàng đa ngày (Order Matrix Grid Data)
    /// </summary>
    [HttpGet("matrix")]
    public async Task<IActionResult> GetOrderMatrix([FromQuery] OrderMatrixQuery query, CancellationToken cancellationToken)
    {
        var result = await _poService.GetOrderMatrixAsync(query, cancellationToken);
        return Ok(ApiResponse<OrderMatrixResponse>.Ok(result));
    }

    /// <summary>
    /// Hub chốt đơn với nhà cung cấp — gom từ PurchaseOrders + PurchaseOrderDetails theo Scope (NEAREST / WEEK / ALL)
    /// </summary>
    [HttpGet("dispatch-hub")]
    public async Task<IActionResult> GetDispatchHub([FromQuery] DispatchHubQuery query, CancellationToken cancellationToken)
    {
        var result = await _poService.GetDispatchHubAsync(query, cancellationToken);
        return Ok(ApiResponse<DispatchHubResponse>.Ok(result));
    }

    /// <summary>
    /// Hub theo NCC (multi-branch): list toàn bộ NCC + tóm tắt theo cửa hàng.
    /// </summary>
    [HttpGet("dispatch-hub/suppliers")]
    public async Task<IActionResult> GetDispatchSupplierHub(
        [FromQuery] DispatchSupplierHubQuery query,
        CancellationToken cancellationToken)
    {
        var result = await _poService.GetDispatchSupplierHubAsync(query, cancellationToken);
        return Ok(ApiResponse<DispatchSupplierHubResponse>.Ok(result));
    }

    /// <summary>
    /// Chi tiết chốt đơn 1 NCC — dòng hàng nhóm theo cửa hàng.
    /// </summary>
    [HttpGet("dispatch-hub/suppliers/{supplierId:guid}")]
    public async Task<IActionResult> GetDispatchSupplierDetail(
        Guid supplierId,
        [FromQuery] int durationDays = 14,
        CancellationToken cancellationToken = default)
    {
        var result = await _poService.GetDispatchSupplierDetailAsync(
            new DispatchSupplierDetailQuery
            {
                SupplierId = supplierId,
                DurationDays = durationDays
            },
            cancellationToken);
        return Ok(ApiResponse<DispatchSupplierDetailResponse>.Ok(result));
    }

    /// <summary>
    /// Lưu bản thảo kế hoạch đặt hàng ma trận (DRAFT)
    /// </summary>
    [HttpPost("matrix/draft")]
    public async Task<IActionResult> SaveMatrixDraft([FromBody] SaveMatrixDraftCommand command, CancellationToken cancellationToken)
    {
        var result = await _poService.SaveMatrixDraftAsync(command, cancellationToken);
        return Ok(ApiResponse<PurchaseOrderDto>.Ok(result, "Lưu bản thảo ma trận đặt hàng thành công."));
    }

    /// <summary>
    /// Xem trước kết quả chốt PO theo phạm vi (NEAREST / WEEK / ALL) không ghi DB (Dùng cho Modal Preview)
    /// </summary>
    [HttpPost("matrix/split/preview")]
    public async Task<IActionResult> PreviewSplitMatrixPo([FromBody] SplitMatrixPoCommand command, CancellationToken cancellationToken)
    {
        var result = await _poService.PreviewSplitMatrixPoAsync(command, cancellationToken);
        return Ok(ApiResponse<SplitMatrixBatchResultDto>.Ok(result, "Xem trước kết quả chốt PO thành công."));
    }

    /// <summary>
    /// Chốt ma trận và tự động phát hành các đơn PO độc lập theo từng Ngày giao hàng và NCC (FE-05 / P0 Engine)
    /// </summary>
    [HttpPost("matrix/split")]
    public async Task<IActionResult> SplitMatrixPo([FromBody] SplitMatrixPoCommand command, CancellationToken cancellationToken)
    {
        var result = await _poService.SplitMatrixPoBatchAsync(command, cancellationToken);
        var msg =
            result.AmendedCount > 0 && result.SuccessCount == 0
                ? $"Đã cập nhật PO thành công: {result.AmendedCount} đơn (Rev+), {result.NoChangesCount} không đổi SL."
                : $"Đã xử lý chốt đơn: {result.SuccessCount} tạo mới, {result.AmendedCount} cập nhật, {result.FailedCount} lỗi, {result.SkippedCount} bỏ qua.";
        return Ok(ApiResponse<SplitMatrixBatchResultDto>.Ok(result, msg));
    }

    /// <summary>
    /// Cập nhật trạng thái đơn PO (APPROVED, REJECTED, DISPATCHED, CANCELLED)
    /// </summary>
    [HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> UpdateStatus(
        Guid id,
        [FromQuery] string status,
        [FromQuery] string? reason,
        CancellationToken cancellationToken)
    {
        var result = await _poService.UpdatePurchaseOrderStatusAsync(id, status, reason, cancellationToken);
        return Ok(ApiResponse<PurchaseOrderDto>.Ok(result, $"Cập nhật trạng thái đơn PO sang {status} thành công."));
    }

    [HttpGet("dispatch-packages")]
    public async Task<IActionResult> ListDispatchPackages(
        [FromQuery] Guid? supplierId,
        [FromQuery] int take = 50,
        CancellationToken cancellationToken = default)
    {
        var result = await _dispatchPackageService.ListAsync(supplierId, take, cancellationToken);
        return Ok(ApiResponse<IReadOnlyList<SupplierDispatchPackageDto>>.Ok(result));
    }

    [HttpGet("dispatch-packages/{packageId:guid}")]
    public async Task<IActionResult> GetDispatchPackage(Guid packageId, CancellationToken cancellationToken)
    {
        var result = await _dispatchPackageService.GetByIdAsync(packageId, cancellationToken);
        if (result == null) return NotFound(ApiResponse<object>.Fail("NOT_FOUND", "Không tìm thấy package."));
        return Ok(ApiResponse<SupplierDispatchPackageDto>.Ok(result));
    }

    [HttpGet("dispatch-packages/{packageId:guid}/download")]
    public async Task<IActionResult> DownloadDispatchPackage(Guid packageId, CancellationToken cancellationToken)
    {
        var (content, fileName, contentType) = await _dispatchPackageService.DownloadAsync(packageId, cancellationToken);
        return File(content, contentType, fileName);
    }

    [HttpPost("dispatch-packages/{packageId:guid}/retry-generation")]
    public async Task<IActionResult> RetryPackageGeneration(Guid packageId, CancellationToken cancellationToken)
    {
        await _dispatchPackageService.RetryGenerationAsync(packageId, cancellationToken);
        var result = await _dispatchPackageService.GetByIdAsync(packageId, cancellationToken);
        return Ok(ApiResponse<SupplierDispatchPackageDto?>.Ok(result, "Đã xếp hàng tạo lại file Excel."));
    }

    [HttpPost("dispatch-packages/{packageId:guid}/retry-email")]
    public async Task<IActionResult> RetryPackageEmail(Guid packageId, CancellationToken cancellationToken)
    {
        await _dispatchPackageService.RetryEmailAsync(packageId, cancellationToken);
        var result = await _dispatchPackageService.GetByIdAsync(packageId, cancellationToken);
        return Ok(ApiResponse<SupplierDispatchPackageDto?>.Ok(result, "Đã xếp hàng gửi lại email."));
    }

    [HttpPost("dispatch-packages/{packageId:guid}/cancel-schedule")]
    public async Task<IActionResult> CancelPackageSchedule(Guid packageId, CancellationToken cancellationToken)
    {
        await _dispatchPackageService.CancelScheduledAsync(packageId, cancellationToken);
        var result = await _dispatchPackageService.GetByIdAsync(packageId, cancellationToken);
        return Ok(ApiResponse<SupplierDispatchPackageDto?>.Ok(result, "Đã hủy lịch gửi email."));
    }
}
