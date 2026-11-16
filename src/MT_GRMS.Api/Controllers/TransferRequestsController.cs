using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Transfers;
using MT_GRMS.Domain.Constants;

namespace MT_GRMS.Api.Controllers;

[Route("api/transfer-requests")]
[Authorize(Roles = SystemRoles.StoreOwner + "," + SystemRoles.Staff)]
public sealed class TransferRequestsController(ITransferRequestService service) : BaseApiController
{
    [HttpPost("allocation-preview")]
    public async Task<ActionResult<ApiResponse<TransferAllocationPreviewDto>>> Preview(
        [FromBody] CreateTransferRequestGroupRequest request, CancellationToken ct)
        => Ok(ApiResponse<TransferAllocationPreviewDto>.Ok(await service.PreviewAllocationAsync(request, ct)));

    [HttpPost]
    public async Task<ActionResult<ApiResponse<TransferRequestGroupDto>>> Create(
        [FromBody] CreateTransferRequestGroupRequest request, CancellationToken ct)
        => Ok(ApiResponse<TransferRequestGroupDto>.Ok(await service.CreateAndAllocateAsync(request, ct),
            "Đã tạo yêu cầu và phân bổ phiếu chuyển theo tồn khả dụng thực tế."));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<TransferRequestGroupDto>>> Get(Guid id, CancellationToken ct)
        => Ok(ApiResponse<TransferRequestGroupDto>.Ok(await service.GetAsync(id, ct)));
}
