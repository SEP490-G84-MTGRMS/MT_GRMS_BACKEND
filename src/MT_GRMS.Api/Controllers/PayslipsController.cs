using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.HR.Payslips;
using MT_GRMS.Application.HR.Payslips.Dtos;
using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Api.Controllers;

[Authorize]
[Route("api/payslips")]
public class PayslipsController : BaseApiController
{
    private readonly IPayslipService _payslipService;

    public PayslipsController(IPayslipService payslipService)
    {
        _payslipService = payslipService;
    }

    /// <summary>
    /// GET /api/v1/payslips
    /// Lấy danh sách phiếu lương có phân trang và bộ lọc (kỳ tháng, chi nhánh, trạng thái, từ khóa).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<PayslipSummaryDto>>>> GetPayslipsPaged(
        [FromQuery] string? keyword,
        [FromQuery] string? periodMonth,
        [FromQuery] Guid? branchId,
        [FromQuery] PayslipStatus? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var filter = new PayslipQueryFilter(
            Keyword: keyword,
            PeriodMonth: periodMonth,
            BranchId: branchId,
            Status: status,
            Page: page,
            PageSize: pageSize
        );

        var result = await _payslipService.GetPayslipsPagedAsync(filter, ct);
        return Ok(ApiResponse<PagedResult<PayslipSummaryDto>>.Ok(result));
    }

    /// <summary>
    /// GET /api/v1/payslips/{id}
    /// Lấy thông tin chi tiết một phiếu lương.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<PayslipResponse>>> GetPayslipById(
        Guid id,
        CancellationToken ct = default)
    {
        var result = await _payslipService.GetPayslipByIdAsync(id, ct);
        return Ok(ApiResponse<PayslipResponse>.Ok(result));
    }

    /// <summary>
    /// POST /api/v1/payslips/generate-batch
    /// Tự động tổng hợp dữ liệu chấm công và khởi tạo/cập nhật bảng lương tháng theo lô.
    /// </summary>
    [HttpPost("generate-batch")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<PayslipResponse>>>> GeneratePayrollBatch(
        [FromBody] GeneratePayrollBatchRequest request,
        CancellationToken ct = default)
    {
        var result = await _payslipService.GenerateMonthlyPayrollAsync(request, ct);
        return Ok(ApiResponse<IReadOnlyList<PayslipResponse>>.Ok(result, "Khởi tạo bảng lương thành công."));
    }

    /// <summary>
    /// POST /api/payslips/recalculate
    /// Tính toán lại bảng lương cho kỳ tháng (bảo lưu các phiếu đã chi trả, chỉ tính lại các phiếu chưa thanh toán).
    /// </summary>
    [HttpPost("recalculate")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<PayslipResponse>>>> RecalculatePayroll(
        [FromBody] GeneratePayrollBatchRequest request,
        CancellationToken ct = default)
    {
        var result = await _payslipService.GenerateMonthlyPayrollAsync(request, ct);
        return Ok(ApiResponse<IReadOnlyList<PayslipResponse>>.Ok(result, "Tính toán lại bảng lương thành công."));
    }

    /// <summary>
    /// POST /api/v1/payslips/{id}/pay
    /// Thực hiện chi trả một phiếu lương đơn lẻ, tự động sinh phiếu chi Sổ quỹ.
    /// </summary>
    [HttpPost("{id:guid}/pay")]
    public async Task<ActionResult<ApiResponse<PayslipResponse>>> PayPayslip(
        Guid id,
        [FromBody] PayPayslipRequest request,
        CancellationToken ct = default)
    {
        var result = await _payslipService.PayPayslipAsync(id, request, ct);
        return Ok(ApiResponse<PayslipResponse>.Ok(result, "Thanh toán phiếu lương thành công."));
    }

    /// <summary>
    /// POST /api/v1/payslips/batch-pay
    /// Chi trả lương hàng loạt cho danh sách phiếu lương chưa thanh toán.
    /// </summary>
    [HttpPost("batch-pay")]
    public async Task<ActionResult<ApiResponse<BatchPayResultDto>>> BatchPayPayslips(
        [FromBody] BatchPayPayslipsRequest request,
        CancellationToken ct = default)
    {
        var result = await _payslipService.BatchPayPayslipsAsync(request, ct);
        return Ok(ApiResponse<BatchPayResultDto>.Ok(result, "Xử lý chi trả hàng loạt thành công."));
    }

    /// <summary>
    /// DELETE /api/v1/payslips/{id}
    /// Xóa phiếu lương (chỉ cho phép xóa khi phiếu chưa thanh toán).
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<ApiResponse<bool>>> DeletePayslip(
        Guid id,
        CancellationToken ct = default)
    {
        var result = await _payslipService.DeletePayslipAsync(id, ct);
        return Ok(ApiResponse<bool>.Ok(result, "Xóa phiếu lương thành công."));
    }
}
