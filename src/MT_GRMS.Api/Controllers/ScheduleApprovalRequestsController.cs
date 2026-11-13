using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Common.Interfaces;
using MT_GRMS.Application.HR.Schedules;
using MT_GRMS.Application.HR.Schedules.Dtos;
using MT_GRMS.Domain.Constants;
using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Api.Controllers;

[Authorize(Roles = SystemRoles.AllStaffAndOwner)]
[ApiController]
[Route("api/schedule-approval-requests")]
public class ScheduleApprovalRequestsController : BaseApiController
{
    private readonly IScheduleApprovalService _approvalService;

    public ScheduleApprovalRequestsController(IScheduleApprovalService approvalService)
    {
        _approvalService = approvalService;
    }

    /// <summary>
    /// Tạo mới phiếu yêu cầu (Xin nghỉ, Đổi ca, Sự cố chấm công, Làm thêm OT)
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreateRequest(
        [FromBody] CreateScheduleApprovalRequestDto dto,
        [FromQuery] Guid? scheduleId,
        CancellationToken cancellationToken)
    {
        var targetScheduleId = dto.ScheduleId ?? scheduleId
            ?? throw new ArgumentException("Vui lòng cung cấp mã ca làm việc (ScheduleId).");

        var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? throw new UnauthorizedAccessException("Không tìm thấy thông tin định danh người dùng.");

        var result = await _approvalService.CreateRequestAsync(targetScheduleId, currentUserId, dto, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, ApiResponse<ScheduleApprovalRequestDto>.Ok(result, "Gửi phiếu yêu cầu thành công."));
    }

    /// <summary>
    /// Tải nhiều tệp hình ảnh bằng chứng / minh chứng lên Cloudinary
    /// </summary>
    [HttpPost("upload-evidences")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadEvidenceImages(
        [FromForm] List<IFormFile> files,
        [FromServices] ICloudinaryService cloudinaryService,
        CancellationToken cancellationToken)
    {
        if (files == null || files.Count == 0)
        {
            return BadRequest(ApiResponse<object>.Fail("NO_FILES", "Vui lòng chọn ít nhất một tệp ảnh."));
        }

        if (files.Count > 10)
        {
            return BadRequest(ApiResponse<object>.Fail("TOO_MANY_FILES", "Chỉ được phép tải lên tối đa 10 ảnh trong một lần gửi."));
        }

        var allowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp" };
        var urls = new List<string>();

        foreach (var file in files)
        {
            if (file.Length == 0) continue;
            if (file.Length > 5 * 1024 * 1024)
            {
                return BadRequest(ApiResponse<object>.Fail("FILE_TOO_LARGE", $"Tệp {file.FileName} vượt quá dung lượng tối đa 5MB."));
            }

            var ext = Path.GetExtension(file.FileName);
            if (string.IsNullOrWhiteSpace(ext) || !allowedExtensions.Contains(ext))
            {
                return BadRequest(ApiResponse<object>.Fail("INVALID_FORMAT", $"Tệp {file.FileName} có định dạng không được hỗ trợ (.jpg, .jpeg, .png, .webp)."));
            }

            await using var stream = file.OpenReadStream();
            var uploadResult = await cloudinaryService.UploadImageAsync(stream, file.FileName, "mtgrms/schedule-requests", cancellationToken);
            urls.Add(uploadResult.Url);
        }

        return Ok(ApiResponse<IReadOnlyList<string>>.Ok(urls, $"Đã tải lên {urls.Count} ảnh thành công."));
    }

    /// <summary>
    /// Lấy danh sách các yêu cầu có bộ lọc (Chi nhánh, Loại đơn, Trạng thái) cho Quản lý / Chủ cửa hàng
    /// </summary>
    [Authorize(Roles = SystemRoles.OwnerOrAdmin)]
    [HttpGet]
    public async Task<IActionResult> GetAllRequests(
        [FromQuery] Guid? branchId,
        [FromQuery] ScheduleRequestType? requestType,
        [FromQuery] ScheduleApprovalStatus? status,
        CancellationToken cancellationToken)
    {
        var result = await _approvalService.GetAllRequestsAsync(branchId, requestType, status, cancellationToken);
        return Ok(ApiResponse<PendingApprovalRequestsResponse>.Ok(result));
    }

    /// <summary>
    /// Lấy danh sách các yêu cầu đang chờ phê duyệt (Pending) cho Quản lý / Chủ cửa hàng
    /// </summary>
    [Authorize(Roles = SystemRoles.OwnerOrAdmin)]
    [HttpGet("pending")]
    public async Task<IActionResult> GetPendingRequests(
        [FromQuery] Guid? branchId,
        [FromQuery] ScheduleRequestType? requestType,
        CancellationToken cancellationToken)
    {
        var result = await _approvalService.GetPendingRequestsAsync(branchId, requestType, cancellationToken);
        return Ok(ApiResponse<PendingApprovalRequestsResponse>.Ok(result));
    }

    /// <summary>
    /// Đồng nghiệp xác nhận đồng ý hoặc từ chối đổi ca làm việc (Peer Confirmation)
    /// </summary>
    [HttpPost("{requestId:guid}/peer-confirm")]
    public async Task<IActionResult> PeerConfirmShiftSwap(
        [FromRoute] Guid requestId,
        [FromBody] PeerConfirmShiftSwapDto dto,
        CancellationToken cancellationToken)
    {
        var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? throw new UnauthorizedAccessException("Không tìm thấy thông tin định danh người dùng.");

        var success = await _approvalService.PeerConfirmShiftSwapAsync(requestId, currentUserId, dto, cancellationToken);
        return Ok(ApiResponse<bool>.Ok(success, dto.IsAgreed ? "Đã đồng ý đổi ca làm việc." : "Đã từ chối đổi ca."));
    }

    /// <summary>
    /// Quản lý / Chủ cửa hàng phê duyệt hoặc từ chối phiếu yêu cầu
    /// </summary>
    [Authorize(Roles = SystemRoles.OwnerOrAdmin)]
    [HttpPost("{requestId:guid}/review")]
    public async Task<IActionResult> ReviewRequest(
        [FromRoute] Guid requestId,
        [FromBody] ReviewScheduleApprovalRequestDto dto,
        CancellationToken cancellationToken)
    {
        var reviewerUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst(ClaimTypes.Name)?.Value
            ?? "Manager";

        var success = await _approvalService.ReviewRequestAsync(requestId, reviewerUserId, dto, cancellationToken);
        var message = dto.IsApproved ? "Phê duyệt yêu cầu thành công." : "Đã từ chối yêu cầu.";
        return Ok(ApiResponse<bool>.Ok(success, message));
    }

    /// <summary>
    /// Cập nhật phiếu yêu cầu ca làm việc (chỉ áp dụng khi trạng thái đang chờ duyệt - Pending)
    /// </summary>
    [HttpPut("{requestId:guid}")]
    public async Task<IActionResult> UpdateRequest(
        [FromRoute] Guid requestId,
        [FromBody] CreateScheduleApprovalRequestDto dto,
        CancellationToken cancellationToken)
    {
        var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? throw new UnauthorizedAccessException("Không tìm thấy thông tin định danh người dùng.");

        var isOwnerOrAdmin = User.IsInRole(SystemRoles.StoreOwner) || User.IsInRole(SystemRoles.Admin);
        var result = await _approvalService.UpdateRequestAsync(requestId, currentUserId, isOwnerOrAdmin, dto, cancellationToken);
        return Ok(ApiResponse<ScheduleApprovalRequestDto>.Ok(result, "Cập nhật phiếu yêu cầu thành công."));
    }

    /// <summary>
    /// Xóa phiếu yêu cầu ca làm việc (chỉ áp dụng khi trạng thái đang chờ duyệt - Pending)
    /// </summary>
    [HttpDelete("{requestId:guid}")]
    public async Task<IActionResult> DeleteRequest(
        [FromRoute] Guid requestId,
        CancellationToken cancellationToken)
    {
        var currentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? throw new UnauthorizedAccessException("Không tìm thấy thông tin định danh người dùng.");

        var isOwnerOrAdmin = User.IsInRole(SystemRoles.StoreOwner) || User.IsInRole(SystemRoles.Admin);
        var success = await _approvalService.DeleteRequestAsync(requestId, currentUserId, isOwnerOrAdmin, cancellationToken);
        return Ok(ApiResponse<bool>.Ok(success, "Xóa phiếu yêu cầu thành công."));
    }
}
