using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using MT_GRMS.Api.Controllers;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.HR.Schedules;
using MT_GRMS.Application.HR.Schedules.Dtos;
using MT_GRMS.Domain.Constants;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Domain.Exceptions;
using Xunit;

namespace MT_GRMS.UnitTests.Controllers;

public class SchedulesControllerTests
{
    private readonly Mock<IScheduleService> _mockService;
    private readonly Mock<IScheduleApprovalService> _mockApprovalService;
    private readonly Mock<ITenantContext> _mockTenant;
    private readonly SchedulesController _controller;

    public SchedulesControllerTests()
    {
        _mockService = new Mock<IScheduleService>();
        _mockApprovalService = new Mock<IScheduleApprovalService>();
        _mockTenant = new Mock<ITenantContext>();
        _mockTenant.SetupGet(t => t.CurrentTenantId).Returns("tenant-01");
        _mockTenant.SetupGet(t => t.CurrentUserId).Returns(Guid.NewGuid());
        _mockTenant.SetupGet(t => t.CurrentRole).Returns(SystemRoles.StoreOwner);

        _controller = new SchedulesController(_mockService.Object, _mockTenant.Object);
        var httpContext = new DefaultHttpContext();
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, _mockTenant.Object.CurrentUserId.ToString()),
            new Claim(ClaimTypes.Role, SystemRoles.StoreOwner)
        };
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
    }

    private static ScheduleResponse CreateSampleSchedule(Guid id, string? userId = null)
    {
        return new ScheduleResponse(
            Id: id,
            EmployeeId: Guid.NewGuid(),
            EmployeeCode: "EMP-01",
            EmployeeName: "Test Staff",
            EmployeeAvatarUrl: null,
            BranchId: Guid.NewGuid(),
            BranchName: "Main Branch",
            ShiftConfigId: null,
            WorkDate: DateTime.UtcNow.Date,
            ShiftCode: "MORNING",
            ShiftName: "Morning Shift",
            PlannedStartTime: TimeSpan.FromHours(8),
            PlannedEndTime: TimeSpan.FromHours(16),
            DurationHours: 8m,
            IsCheckInNextDay: false,
            CheckInTime: null,
            CheckOutTime: null,
            CheckInMethod: null,
            CheckInFaceImageUrl: null,
            FaceMatchScore: null,
            CheckOutMethod: null,
            CheckOutFaceImageUrl: null,
            LateMinutes: 0,
            EarlyLeaveMinutes: 0,
            OvertimeMinutes: 0,
            ActualWorkHours: 0m,
            Status: ScheduleStatus.Published,
            ApprovalType: null,
            ApprovalStatus: ScheduleApprovalStatus.Pending,
            ApprovalReason: null,
            ApprovedBy: null,
            ApprovedAt: null,
            CreatedAt: DateTime.UtcNow,
            UserId: userId ?? Guid.NewGuid().ToString()
        );
    }

    [Fact]
    public async Task GetSchedules_ShouldReturnOk()
    {
        var filter = new ScheduleQueryFilter();
        _mockService.Setup(s => s.GetListAsync(filter, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ScheduleResponse>());

        var result = await _controller.GetSchedules(filter, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
    }

    [Fact]
    public async Task GetMySchedule_Authorized_ShouldReturnOk()
    {
        var userId = _mockTenant.Object.CurrentUserId.ToString();
        _mockService.Setup(s => s.GetMySchedulesAsync(userId, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ScheduleResponse>());

        var result = await _controller.GetMySchedule(null, null, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
    }

    [Fact]
    public async Task GetById_WhenAdminOrOwner_ShouldReturnOk()
    {
        var id = Guid.NewGuid();
        var schedule = CreateSampleSchedule(id);
        _mockService.Setup(s => s.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(schedule);

        var result = await _controller.GetById(id, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
    }

    [Fact]
    public async Task GetById_WhenStaffForbidden_ShouldReturn403()
    {
        var httpContext = new DefaultHttpContext();
        var staffId = Guid.NewGuid().ToString();
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, staffId),
            new Claim(ClaimTypes.Role, SystemRoles.Staff)
        };
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var id = Guid.NewGuid();
        var schedule = CreateSampleSchedule(id, userId: Guid.NewGuid().ToString()); // other user
        _mockService.Setup(s => s.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(schedule);

        var result = await _controller.GetById(id, CancellationToken.None);
        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, status.StatusCode);
    }

    [Fact]
    public async Task GetById_WhenNotFoundException_ShouldReturnNotFound()
    {
        var id = Guid.NewGuid();
        _mockService.Setup(s => s.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotFoundException("Schedule not found"));

        var result = await _controller.GetById(id, CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task Create_ShouldReturnCreatedAtAction()
    {
        var request = new CreateScheduleItemRequest(
            EmployeeId: Guid.NewGuid(),
            BranchId: Guid.NewGuid(),
            WorkDate: DateTime.UtcNow.Date
        );
        var schedule = CreateSampleSchedule(Guid.NewGuid());
        _mockService.Setup(s => s.CreateAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(schedule);

        var result = await _controller.Create(request, CancellationToken.None);
        var created = Assert.IsType<CreatedAtActionResult>(result);
        Assert.NotNull(created.Value);
    }

    [Fact]
    public async Task Create_WhenConflict_ShouldReturnBadRequest()
    {
        var request = new CreateScheduleItemRequest(
            EmployeeId: Guid.NewGuid(),
            BranchId: Guid.NewGuid(),
            WorkDate: DateTime.UtcNow.Date
        );
        _mockService.Setup(s => s.CreateAsync(request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BusinessException("CONFLICT", "Time overlap"));

        var result = await _controller.Create(request, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task BatchCreate_ShouldReturnOk()
    {
        var request = new BatchCreateScheduleRequest(Guid.NewGuid(), new List<CreateScheduleItemRequest>());
        _mockService.Setup(s => s.BatchCreateAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ScheduleResponse>());

        var result = await _controller.BatchCreate(request, CancellationToken.None);
        var created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
    }

    [Fact]
    public async Task CreateRecurring_ShouldReturnOk()
    {
        var request = new CreateRecurringScheduleRequest(
            BranchId: Guid.NewGuid(),
            EmployeeId: Guid.NewGuid(),
            StartDate: DateTime.UtcNow.Date
        );
        _mockService.Setup(s => s.CreateRecurringAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ScheduleResponse>());

        var result = await _controller.CreateRecurring(request, CancellationToken.None);
        var ok = Assert.IsType<ObjectResult>(result);
        Assert.NotNull(ok.Value);
    }

    [Fact]
    public async Task Update_ShouldReturnOk()
    {
        var id = Guid.NewGuid();
        var request = new UpdateScheduleRequest(PlannedStartTime: TimeSpan.FromHours(8), PlannedEndTime: TimeSpan.FromHours(16));
        var schedule = CreateSampleSchedule(id);
        _mockService.Setup(s => s.UpdateAsync(id, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(schedule);

        var result = await _controller.Update(id, request, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
    }

    [Fact]
    public async Task Delete_ShouldReturnNoContent()
    {
        var id = Guid.NewGuid();
        _mockService.Setup(s => s.DeleteAsync(id, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await _controller.Delete(id, CancellationToken.None);
        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task Publish_ShouldReturnOk()
    {
        var request = new PublishSchedulesRequest(Guid.NewGuid(), DateTime.UtcNow.Date, DateTime.UtcNow.Date.AddDays(7));
        _mockService.Setup(s => s.PublishAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PublishSchedulesResponse(5, "Published 5 shifts"));

        var result = await _controller.Publish(request, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
    }

    [Fact]
    public async Task RecordAttendance_ShouldReturnOk()
    {
        var id = Guid.NewGuid();
        var request = new ManualAttendanceRequest(ManualAttendanceType.Working, TimeSpan.FromHours(8), TimeSpan.FromHours(17));
        var schedule = CreateSampleSchedule(id);
        _mockService.Setup(s => s.RecordManualAttendanceAsync(id, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(schedule);

        var result = await _controller.RecordAttendance(id, request, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
    }

    [Fact]
    public async Task ResetAttendance_ShouldReturnOk()
    {
        var id = Guid.NewGuid();
        var request = new ResetAttendanceRequest("Wrong checkin");
        var schedule = CreateSampleSchedule(id);
        _mockService.Setup(s => s.ResetManualAttendanceAsync(id, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResetAttendanceResponse(schedule, false, "Reset ok"));

        var result = await _controller.ResetAttendance(id, request, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
    }

    [Fact]
    public async Task CreateScheduleRequest_ShouldReturnOk()
    {
        var id = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var request = new CreateScheduleApprovalRequestDto
        {
            ScheduleId = id,
            RequestType = ScheduleRequestType.CheckInIncident,
            Reason = "Broken tire"
        };
        var res = new ScheduleApprovalRequestDto(
            Id: Guid.NewGuid(),
            ScheduleId: id,
            EmployeeId: employeeId,
            EmployeeName: "Staff",
            EmployeeCode: "EMP-01",
            BranchId: branchId,
            BranchName: "Main Branch",
            RequestType: ScheduleRequestType.CheckInIncident,
            Status: ScheduleApprovalStatus.Pending,
            Reason: "Broken tire",
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
            ShiftName: "Morning",
            PlannedStartTime: TimeSpan.FromHours(8),
            PlannedEndTime: TimeSpan.FromHours(16)
        );
        _mockApprovalService.Setup(s => s.CreateRequestAsync(id, It.IsAny<string>(), request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(res);

        var result = await _controller.CreateScheduleRequest(id, request, _mockApprovalService.Object, CancellationToken.None);
        var created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
    }

    [Fact]
    public async Task GetScheduleRequests_ShouldReturnOk()
    {
        var id = Guid.NewGuid();
        _mockApprovalService.Setup(s => s.GetRequestsByScheduleIdAsync(id, It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ScheduleApprovalRequestDto>());

        var result = await _controller.GetScheduleRequests(id, _mockApprovalService.Object, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
    }

    [Fact]
    public async Task GetTodayStaffDescriptors_ShouldReturnOk()
    {
        var branchId = Guid.NewGuid();
        _mockService.Setup(s => s.GetTodayStaffDescriptorsAsync(branchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TodayStaffDescriptorDto>());

        var result = await _controller.GetTodayStaffDescriptors(branchId, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
    }

    [Fact]
    public async Task SelfCheckIn_ShouldReturnOk()
    {
        var id = Guid.NewGuid();
        var request = new SelfCheckInRequest("Self checkin");
        var schedule = CreateSampleSchedule(id);
        _mockService.Setup(s => s.SelfCheckInAsync(id, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(schedule);

        var result = await _controller.SelfCheckIn(id, request, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
    }

    [Fact]
    public async Task CheckInFaceId_ShouldReturnOk()
    {
        var request = new FaceIdCheckInRequest(Guid.NewGuid(), Guid.NewGuid(), 0.95m, "http://face.jpg");
        var schedule = CreateSampleSchedule(Guid.NewGuid());
        _mockService.Setup(s => s.CheckInFaceIdAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(schedule);

        var result = await _controller.CheckInFaceId(request, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
    }

    [Fact]
    public async Task CheckOutFaceId_ShouldReturnOk()
    {
        var request = new FaceIdCheckOutRequest(Guid.NewGuid(), Guid.NewGuid(), 0.95m, "http://face.jpg");
        var schedule = CreateSampleSchedule(Guid.NewGuid());
        _mockService.Setup(s => s.CheckOutFaceIdAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(schedule);

        var result = await _controller.CheckOutFaceId(request, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
    }

    [Fact]
    public async Task ScheduleController_ExceptionPaths_ShouldReturnAppropriateStatuses()
    {
        var id = Guid.NewGuid();

        // 1. GetById NotFoundException
        _mockService.Setup(s => s.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotFoundException("Schedule not found"));
        Assert.IsType<NotFoundObjectResult>(await _controller.GetById(id, CancellationToken.None));

        // 2. Update NotFound & BusinessException
        var updateReq = new UpdateScheduleRequest();
        _mockService.Setup(s => s.UpdateAsync(id, updateReq, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotFoundException("Not found"));
        Assert.IsType<NotFoundObjectResult>(await _controller.Update(id, updateReq, CancellationToken.None));

        _mockService.Setup(s => s.UpdateAsync(id, updateReq, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BusinessException("ERR", "Biz error"));
        Assert.IsType<BadRequestObjectResult>(await _controller.Update(id, updateReq, CancellationToken.None));

        // 3. Delete NotFound & BusinessException
        _mockService.Setup(s => s.DeleteAsync(id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotFoundException("Not found"));
        Assert.IsType<NotFoundObjectResult>(await _controller.Delete(id, CancellationToken.None));

        _mockService.Setup(s => s.DeleteAsync(id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BusinessException("ERR", "Cannot delete"));
        Assert.IsType<BadRequestObjectResult>(await _controller.Delete(id, CancellationToken.None));

        // 4. Publish NotFound & BusinessException
        var pubReq = new PublishSchedulesRequest(Guid.NewGuid(), DateTime.UtcNow.Date, DateTime.UtcNow.Date.AddDays(7));
        _mockService.Setup(s => s.PublishAsync(pubReq, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotFoundException("Not found"));
        Assert.IsType<NotFoundObjectResult>(await _controller.Publish(pubReq, CancellationToken.None));

        _mockService.Setup(s => s.PublishAsync(pubReq, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BusinessException("ERR", "Cannot publish"));
        Assert.IsType<BadRequestObjectResult>(await _controller.Publish(pubReq, CancellationToken.None));

        // 5. RecordAttendance NotFound, BusinessException, and generic Exception
        var attReq = new ManualAttendanceRequest(ManualAttendanceType.Working, TimeSpan.FromHours(8), TimeSpan.FromHours(17), "Present");
        _mockService.Setup(s => s.RecordManualAttendanceAsync(id, attReq, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotFoundException("Not found"));
        Assert.IsType<NotFoundObjectResult>(await _controller.RecordAttendance(id, attReq, CancellationToken.None));

        _mockService.Setup(s => s.RecordManualAttendanceAsync(id, attReq, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BusinessException("ERR", "Attendance error"));
        Assert.IsType<BadRequestObjectResult>(await _controller.RecordAttendance(id, attReq, CancellationToken.None));

        _mockService.Setup(s => s.RecordManualAttendanceAsync(id, attReq, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Crash"));
        var att500 = Assert.IsType<ObjectResult>(await _controller.RecordAttendance(id, attReq, CancellationToken.None));
        Assert.Equal(500, att500.StatusCode);

        // 6. ResetAttendance NotFound, BusinessException, and generic Exception
        var resetReq = new ResetAttendanceRequest("Reset");
        _mockService.Setup(s => s.ResetManualAttendanceAsync(id, resetReq, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotFoundException("Not found"));
        Assert.IsType<NotFoundObjectResult>(await _controller.ResetAttendance(id, resetReq, CancellationToken.None));

        _mockService.Setup(s => s.ResetManualAttendanceAsync(id, resetReq, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BusinessException("ERR", "Cannot reset"));
        Assert.IsType<BadRequestObjectResult>(await _controller.ResetAttendance(id, resetReq, CancellationToken.None));

        _mockService.Setup(s => s.ResetManualAttendanceAsync(id, resetReq, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Unknown crash"));
        var reset500 = Assert.IsType<ObjectResult>(await _controller.ResetAttendance(id, resetReq, CancellationToken.None));
        Assert.Equal(500, reset500.StatusCode);

        // 7. SelfCheckIn NotFound, Forbidden, Conflict, and BusinessException
        var selfReq = new SelfCheckInRequest("Self");
        _mockService.Setup(s => s.SelfCheckInAsync(id, selfReq, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotFoundException("Not found"));
        Assert.IsType<NotFoundObjectResult>(await _controller.SelfCheckIn(id, selfReq, CancellationToken.None));

        _mockService.Setup(s => s.SelfCheckInAsync(id, selfReq, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException("Forbidden"));
        var self403 = Assert.IsType<ObjectResult>(await _controller.SelfCheckIn(id, selfReq, CancellationToken.None));
        Assert.Equal(403, self403.StatusCode);

        _mockService.Setup(s => s.SelfCheckInAsync(id, selfReq, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BusinessException("ALREADY_CHECKED_IN", "Already in"));
        Assert.IsType<ConflictObjectResult>(await _controller.SelfCheckIn(id, selfReq, CancellationToken.None));

        _mockService.Setup(s => s.SelfCheckInAsync(id, selfReq, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BusinessException("ERR", "Cannot self checkin"));
        Assert.IsType<BadRequestObjectResult>(await _controller.SelfCheckIn(id, selfReq, CancellationToken.None));

        // 8. CheckInFaceId & CheckOutFaceId exceptions
        var faceInReq = new FaceIdCheckInRequest(Guid.NewGuid(), Guid.NewGuid(), 0.9m, "http://face.jpg");
        _mockService.Setup(s => s.CheckInFaceIdAsync(faceInReq, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotFoundException("Not found"));
        Assert.IsType<NotFoundObjectResult>(await _controller.CheckInFaceId(faceInReq, CancellationToken.None));

        _mockService.Setup(s => s.CheckInFaceIdAsync(faceInReq, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BusinessException("ERR", "Face not recognized"));
        Assert.IsType<BadRequestObjectResult>(await _controller.CheckInFaceId(faceInReq, CancellationToken.None));

        var faceOutReq = new FaceIdCheckOutRequest(Guid.NewGuid(), Guid.NewGuid(), 0.9m, "http://face.jpg");
        _mockService.Setup(s => s.CheckOutFaceIdAsync(faceOutReq, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotFoundException("Not found"));
        Assert.IsType<NotFoundObjectResult>(await _controller.CheckOutFaceId(faceOutReq, CancellationToken.None));

        _mockService.Setup(s => s.CheckOutFaceIdAsync(faceOutReq, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BusinessException("ERR", "Face not recognized"));
        Assert.IsType<BadRequestObjectResult>(await _controller.CheckOutFaceId(faceOutReq, CancellationToken.None));
    }
}
