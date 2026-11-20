using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Moq;
using MT_GRMS.Api.Controllers;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.HR.AttendancePolicies;
using MT_GRMS.Application.HR.AttendancePolicies.Dtos;
using MT_GRMS.Domain.Exceptions;
using Xunit;

namespace MT_GRMS.UnitTests.Controllers;

public class BranchAttendancePoliciesControllerTests
{
    private readonly Mock<IBranchAttendancePolicyService> _mockService;
    private readonly BranchAttendancePoliciesController _controller;

    public BranchAttendancePoliciesControllerTests()
    {
        _mockService = new Mock<IBranchAttendancePolicyService>();
        _controller = new BranchAttendancePoliciesController(_mockService.Object);
    }

    private static AttendancePolicyResponse CreateTestPolicy(Guid id, Guid branchId)
    {
        return new AttendancePolicyResponse(
            Id: id,
            BranchId: branchId,
            BranchName: "Branch 1",
            StandardDayHours: 8.0m,
            IsConvertHoursToDays: true,
            ConversionRulesJson: null,
            EnableLateThreshold: true,
            LateThresholdMinutes: 15,
            EnableEarlyLeaveThreshold: true,
            EarlyLeaveThresholdMinutes: 15,
            IsCustomized: true);
    }

    [Fact]
    public async Task GetByBranch_ShouldHandleSuccess_AndNotFound()
    {
        var branchId = Guid.NewGuid();
        var policy = CreateTestPolicy(Guid.NewGuid(), branchId);

        _mockService.Setup(s => s.GetByBranchIdAsync(branchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(policy);

        var res = await _controller.GetByBranch(branchId, CancellationToken.None);
        var okResult = Assert.IsType<OkObjectResult>(res);
        var apiRes = Assert.IsType<ApiResponse<AttendancePolicyResponse>>(okResult.Value);
        Assert.True(apiRes.Success);
        Assert.Equal(branchId, apiRes.Data?.BranchId);

        // NotFound
        _mockService.Setup(s => s.GetByBranchIdAsync(branchId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotFoundException("Branch", branchId));

        var resNotFound = await _controller.GetByBranch(branchId, CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(resNotFound);
    }

    [Fact]
    public async Task UpsertPolicy_ShouldHandleSuccess_NotFound_AndBusinessException()
    {
        var branchId = Guid.NewGuid();
        var request = new UpsertAttendancePolicyRequest();
        var policy = CreateTestPolicy(Guid.NewGuid(), branchId);

        _mockService.Setup(s => s.UpsertPolicyAsync(branchId, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(policy);

        var res = await _controller.UpsertPolicy(branchId, request, CancellationToken.None);
        var okResult = Assert.IsType<OkObjectResult>(res);
        var apiRes = Assert.IsType<ApiResponse<AttendancePolicyResponse>>(okResult.Value);
        Assert.True(apiRes.Success);

        // NotFound
        _mockService.Setup(s => s.UpsertPolicyAsync(branchId, request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotFoundException("Branch", branchId));
        var resNotFound = await _controller.UpsertPolicy(branchId, request, CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(resNotFound);

        // BusinessException
        _mockService.Setup(s => s.UpsertPolicyAsync(branchId, request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BusinessException("INVALID_POLICY", "Loi gio lam"));
        var resBad = await _controller.UpsertPolicy(branchId, request, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(resBad);
    }

    [Fact]
    public async Task CopyPolicy_ShouldHandleSuccess_NotFound_AndBusinessException()
    {
        var sourceBranchId = Guid.NewGuid();
        var request = new CopyAttendancePolicyRequest(sourceBranchId, new List<Guid> { Guid.NewGuid() });
        var resultDto = new CopyAttendancePolicyResultDto(sourceBranchId, 1, new List<Guid>(), "Sao chep thanh cong");

        _mockService.Setup(s => s.CopyPolicyAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(resultDto);

        var res = await _controller.CopyPolicy(request, CancellationToken.None);
        var okResult = Assert.IsType<OkObjectResult>(res);
        var apiRes = Assert.IsType<ApiResponse<CopyAttendancePolicyResultDto>>(okResult.Value);
        Assert.True(apiRes.Success);

        // NotFound
        _mockService.Setup(s => s.CopyPolicyAsync(request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotFoundException("Branch", sourceBranchId));
        var resNotFound = await _controller.CopyPolicy(request, CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(resNotFound);

        // BusinessException
        _mockService.Setup(s => s.CopyPolicyAsync(request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BusinessException("COPY_FAILED", "Khong the copy"));
        var resBad = await _controller.CopyPolicy(request, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(resBad);
    }
}
