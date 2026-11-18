using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using MT_GRMS.Api.Controllers;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Common.DTOs;
using MT_GRMS.Application.Common.Interfaces;
using MT_GRMS.Application.HR.Schedules;
using MT_GRMS.Application.HR.Schedules.Dtos;
using MT_GRMS.Domain.Constants;
using MT_GRMS.Domain.Enums;
using Xunit;

namespace MT_GRMS.UnitTests.Controllers;

public class ScheduleApprovalRequestsControllerTests
{
    private readonly Mock<IScheduleApprovalService> _mockService;
    private readonly ScheduleApprovalRequestsController _controller;

    public ScheduleApprovalRequestsControllerTests()
    {
        _mockService = new Mock<IScheduleApprovalService>();
        _controller = new ScheduleApprovalRequestsController(_mockService.Object);

        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "usr-01"),
            new Claim(ClaimTypes.Role, SystemRoles.StoreOwner)
        }, "TestAuth"));

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };
    }

    private static ScheduleApprovalRequestDto CreateTestDto(Guid id, Guid scheduleId)
    {
        return new ScheduleApprovalRequestDto(
            Id: id,
            ScheduleId: scheduleId,
            EmployeeId: Guid.NewGuid(),
            EmployeeName: "Nguyen Van A",
            EmployeeCode: "NV01",
            BranchId: Guid.NewGuid(),
            BranchName: "Branch 1",
            RequestType: ScheduleRequestType.LeaveRequest,
            Status: ScheduleApprovalStatus.Pending,
            Reason: "Nghi phep",
            EvidenceImageUrls: new List<string>(),
            LeaveType: null,
            IsPaidLeave: false,
            RequestedCheckInTime: null,
            RequestedCheckOutTime: null,
            ApprovedCheckInTime: null,
            ApprovedCheckOutTime: null,
            RequestedOvertimeMinutes: null,
            ApprovedOvertimeMinutes: null,
            ReviewNote: null,
            ApprovedBy: null,
            ApprovedAt: null,
            CreatedAt: DateTime.UtcNow,
            WorkDate: DateTime.UtcNow.Date,
            ShiftName: "Ca sang",
            PlannedStartTime: TimeSpan.FromHours(8),
            PlannedEndTime: TimeSpan.FromHours(17));
    }

    [Fact]
    public async Task CreateRequest_ShouldReturnCreated()
    {
        var schedId = Guid.NewGuid();
        var dto = new CreateScheduleApprovalRequestDto(
            RequestType: ScheduleRequestType.LeaveRequest,
            Reason: "Nghi benh",
            ScheduleId: schedId);

        var response = CreateTestDto(Guid.NewGuid(), schedId);

        _mockService
            .Setup(s => s.CreateRequestAsync(schedId, "usr-01", dto, It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var result = await _controller.CreateRequest(dto, null, CancellationToken.None);

        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status201Created, objResult.StatusCode);
        var apiRes = Assert.IsType<ApiResponse<ScheduleApprovalRequestDto>>(objResult.Value);
        Assert.True(apiRes.Success);
    }

    [Fact]
    public async Task UploadEvidenceImages_ShouldHandleValidationAndUpload()
    {
        var mockCloudinary = new Mock<ICloudinaryService>();

        // Empty files
        var emptyRes = await _controller.UploadEvidenceImages(new List<IFormFile>(), mockCloudinary.Object, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(emptyRes);

        // Upload success
        var mockFile = new Mock<IFormFile>();
        var content = "fake image"u8.ToArray();
        var ms = new MemoryStream(content);
        mockFile.Setup(f => f.OpenReadStream()).Returns(ms);
        mockFile.Setup(f => f.FileName).Returns("evidence.jpg");
        mockFile.Setup(f => f.Length).Returns(content.Length);

        mockCloudinary
            .Setup(c => c.UploadImageAsync(It.IsAny<Stream>(), "evidence.jpg", "mtgrms/schedule-requests", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImageUploadResponseDto { Url = "https://cloudinary.com/evidence.jpg", PublicId = "public-id" });

        var uploadRes = await _controller.UploadEvidenceImages(new List<IFormFile> { mockFile.Object }, mockCloudinary.Object, CancellationToken.None);
        var okResult = Assert.IsType<OkObjectResult>(uploadRes);
        var apiRes = Assert.IsType<ApiResponse<IReadOnlyList<string>>>(okResult.Value);
        Assert.True(apiRes.Success);
        Assert.Single(apiRes.Data!);
    }

    [Fact]
    public async Task GetAllRequests_ShouldReturnOk()
    {
        var pending = new PendingApprovalRequestsResponse(0, new List<ScheduleApprovalRequestDto>());
        _mockService.Setup(s => s.GetAllRequestsAsync(null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pending);

        var result = await _controller.GetAllRequests(null, null, null, CancellationToken.None);
        var okResult = Assert.IsType<OkObjectResult>(result);
        var apiRes = Assert.IsType<ApiResponse<PendingApprovalRequestsResponse>>(okResult.Value);
        Assert.True(apiRes.Success);
    }

    [Fact]
    public async Task GetPendingRequests_ShouldReturnOk()
    {
        var pending = new PendingApprovalRequestsResponse(0, new List<ScheduleApprovalRequestDto>());
        _mockService.Setup(s => s.GetPendingRequestsAsync(null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(pending);

        var result = await _controller.GetPendingRequests(null, null, CancellationToken.None);
        var okResult = Assert.IsType<OkObjectResult>(result);
        var apiRes = Assert.IsType<ApiResponse<PendingApprovalRequestsResponse>>(okResult.Value);
        Assert.True(apiRes.Success);
    }

    [Fact]
    public async Task PeerConfirmShiftSwap_ShouldReturnOk()
    {
        var reqId = Guid.NewGuid();
        var dto = new PeerConfirmShiftSwapDto(true, null);

        _mockService.Setup(s => s.PeerConfirmShiftSwapAsync(reqId, "usr-01", dto, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _controller.PeerConfirmShiftSwap(reqId, dto, CancellationToken.None);
        var okResult = Assert.IsType<OkObjectResult>(result);
        var apiRes = Assert.IsType<ApiResponse<bool>>(okResult.Value);
        Assert.True(apiRes.Success);
        Assert.True(apiRes.Data);
    }

    [Fact]
    public async Task ReviewRequest_ShouldReturnOk()
    {
        var reqId = Guid.NewGuid();
        var dto = new ReviewScheduleApprovalRequestDto(true, "Duyet");

        _mockService.Setup(s => s.ReviewRequestAsync(reqId, "usr-01", dto, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _controller.ReviewRequest(reqId, dto, CancellationToken.None);
        var okResult = Assert.IsType<OkObjectResult>(result);
        var apiRes = Assert.IsType<ApiResponse<bool>>(okResult.Value);
        Assert.True(apiRes.Success);
        Assert.True(apiRes.Data);
    }

    [Fact]
    public async Task UpdateRequest_ShouldReturnOk()
    {
        var reqId = Guid.NewGuid();
        var schedId = Guid.NewGuid();
        var dto = new CreateScheduleApprovalRequestDto(ScheduleRequestType.LeaveRequest, "Sua ly do", ScheduleId: schedId);
        var resDto = CreateTestDto(reqId, schedId);

        _mockService.Setup(s => s.UpdateRequestAsync(reqId, "usr-01", true, dto, It.IsAny<CancellationToken>()))
            .ReturnsAsync(resDto);

        var result = await _controller.UpdateRequest(reqId, dto, CancellationToken.None);
        var okResult = Assert.IsType<OkObjectResult>(result);
        var apiRes = Assert.IsType<ApiResponse<ScheduleApprovalRequestDto>>(okResult.Value);
        Assert.True(apiRes.Success);
        Assert.Equal(reqId, apiRes.Data?.Id);
    }

    [Fact]
    public async Task DeleteRequest_ShouldReturnOk()
    {
        var reqId = Guid.NewGuid();
        _mockService.Setup(s => s.DeleteRequestAsync(reqId, "usr-01", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _controller.DeleteRequest(reqId, CancellationToken.None);
        var okResult = Assert.IsType<OkObjectResult>(result);
        var apiRes = Assert.IsType<ApiResponse<bool>>(okResult.Value);
        Assert.True(apiRes.Success);
        Assert.True(apiRes.Data);
    }
}
