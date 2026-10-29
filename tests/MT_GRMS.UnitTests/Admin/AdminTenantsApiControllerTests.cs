using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MT_GRMS.Api.Controllers;
using MT_GRMS.Application.Admin.Dtos;
using MT_GRMS.Application.Admin.Services;
using MT_GRMS.Application.Admin.Validators;
using MT_GRMS.Application.Common;
using Xunit;

namespace MT_GRMS.UnitTests.Admin;

public class MockTenantService : ITenantService
{
    public bool ShouldThrowNotFound { get; set; }
    public bool ShouldThrowInvalidLock { get; set; }
    public bool ShouldThrowInvalidGrace { get; set; }

    public Task<PagedResult<TenantSummaryResponse>> GetTenantsAsync(TenantFilterRequest filter, CancellationToken cancellationToken = default)
    {
        var item = new TenantSummaryResponse(
            Id: "tenant-01",
            Subdomain: "hanoi-mart",
            Name: "Hà Nội Mart",
            TaxCode: "0101234567",
            City: "Hà Nội",
            OwnerName: "Nguyễn Văn Chủ",
            OwnerEmail: "owner@hnmart.vn",
            OwnerPhone: "0988111222",
            Plan: "Gói Pro",
            BranchCount: 3,
            MaxBranches: 5,
            StaffCount: 12,
            MaxStaff: 20,
            ExpiryDate: DateTime.UtcNow.AddMonths(2),
            Status: "ACTIVE",
            LockType: null,
            GraceDaysRemaining: null,
            DaysUntilExpiry: 60
        );

        var result = new PagedResult<TenantSummaryResponse>
        {
            Items = new List<TenantSummaryResponse> { item },
            TotalItems = 1,
            Page = 1,
            PageSize = 10
        };

        return Task.FromResult(result);
    }

    public Task<TenantDetailResponse> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        if (ShouldThrowNotFound || id == "non-existent")
        {
            throw new KeyNotFoundException($"Không tìm thấy khách thuê với mã định danh '{id}'.");
        }

        return Task.FromResult(new TenantDetailResponse(
            Id: id,
            Subdomain: "hanoi-mart",
            Name: "Hà Nội Mart",
            Code: "HN-MART",
            TaxCode: "0101234567",
            Hotline: "0243888999",
            Address: "123 Cầu Giấy",
            City: "Hà Nội",
            Status: "ACTIVE",
            CurrentPlan: "PRO",
            PlanName: "Gói Pro",
            SubscriptionExpiresAt: DateTime.UtcNow.AddMonths(2),
            GracePeriodExpiresAt: null,
            LockType: null,
            LockReason: null,
            LockedAt: null,
            LockedBy: null,
            BranchCount: 3,
            MaxBranches: 5,
            StaffCount: 12,
            MaxStaff: 20,
            Branches: new List<TenantDetailBranchSummaryDto>(),
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: null
        ));
    }

    public Task<TenantDetailResponse> UpdateTenantAsync(string id, UpdateTenantRequest request, CancellationToken cancellationToken = default)
    {
        if (ShouldThrowNotFound || id == "non-existent")
        {
            throw new KeyNotFoundException($"Không tìm thấy khách thuê với mã định danh '{id}'.");
        }

        return Task.FromResult(new TenantDetailResponse(
            Id: id,
            Subdomain: "hanoi-mart",
            Name: request.Name,
            Code: "HN-MART",
            TaxCode: request.TaxCode,
            Hotline: request.Hotline,
            Address: request.Address,
            City: request.City,
            Status: "ACTIVE",
            CurrentPlan: "PRO",
            PlanName: "Gói Pro",
            SubscriptionExpiresAt: DateTime.UtcNow.AddMonths(2),
            GracePeriodExpiresAt: null,
            LockType: null,
            LockReason: null,
            LockedAt: null,
            LockedBy: null,
            BranchCount: 3,
            MaxBranches: 5,
            StaffCount: 12,
            MaxStaff: 20,
            Branches: new List<TenantDetailBranchSummaryDto>(),
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: DateTime.UtcNow
        ));
    }

    public Task LockTenantAsync(string id, LockTenantRequest request, string lockedBy, CancellationToken cancellationToken = default)
    {
        if (ShouldThrowNotFound || id == "non-existent")
        {
            throw new KeyNotFoundException($"Không tìm thấy khách thuê với mã định danh '{id}'.");
        }

        if (ShouldThrowInvalidLock || request.LockType == "INVALID")
        {
            throw new ArgumentException("Loại khóa chỉ chấp nhận SOFT hoặc HARD.");
        }

        return Task.CompletedTask;
    }

    public Task UnlockTenantAsync(string id, CancellationToken cancellationToken = default)
    {
        if (ShouldThrowNotFound || id == "non-existent")
        {
            throw new KeyNotFoundException($"Không tìm thấy khách thuê với mã định danh '{id}'.");
        }

        return Task.CompletedTask;
    }

    public Task ExtendGracePeriodAsync(string id, ExtendGraceRequest request, CancellationToken cancellationToken = default)
    {
        if (ShouldThrowNotFound || id == "non-existent")
        {
            throw new KeyNotFoundException($"Không tìm thấy khách thuê với mã định danh '{id}'.");
        }

        if (ShouldThrowInvalidGrace || request.AdditionalDays < 0)
        {
            throw new ArgumentException("Số ngày gia hạn không hợp lệ.");
        }

        return Task.CompletedTask;
    }
}

public class AdminTenantsApiControllerTests
{
    private readonly MockTenantService _service;
    private readonly LockTenantRequestValidator _lockValidator;
    private readonly ExtendGraceRequestValidator _graceValidator;
    private readonly AdminTenantsController _controller;

    public AdminTenantsApiControllerTests()
    {
        _service = new MockTenantService();
        _lockValidator = new LockTenantRequestValidator();
        _graceValidator = new ExtendGraceRequestValidator();
        _controller = new AdminTenantsController(_service, _lockValidator, _graceValidator);

        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, "AdminUser"),
            new Claim(ClaimTypes.Role, "ADMIN")
        }, "mock_auth"));

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };
    }

    [Fact]
    public async Task GetTenants_Returns200Ok_WithPagedResult()
    {
        var result = await _controller.GetTenants(new TenantFilterRequest(), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<PagedResult<TenantSummaryResponse>>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Single(resp.Data!.Items);
        Assert.Equal("hanoi-mart", resp.Data!.Items[0].Subdomain);
    }

    [Fact]
    public async Task GetById_Existing_Returns200Ok()
    {
        var result = await _controller.GetById("tenant-01", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<TenantDetailResponse>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal("HN-MART", resp.Data!.Code);
    }

    [Fact]
    public async Task GetById_NotFound_Returns404NotFound()
    {
        var result = await _controller.GetById("non-existent", CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<TenantDetailResponse>>(notFound.Value);
        Assert.False(resp.Success);
        Assert.Equal("TENANT_NOT_FOUND", resp.ErrorCode);
    }

    [Fact]
    public async Task UpdateTenant_Success_Returns200Ok()
    {
        var req = new UpdateTenantRequest("Hà Nội Mart Mới", "0101234567", "0243888999", "Đống Đa", "Hà Nội");
        var result = await _controller.UpdateTenant("tenant-01", req, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<TenantDetailResponse>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal("Hà Nội Mart Mới", resp.Data!.Name);
    }

    [Fact]
    public async Task UpdateTenant_NotFound_Returns404NotFound()
    {
        var req = new UpdateTenantRequest("Tên Mới", null, null, null, null);
        var result = await _controller.UpdateTenant("non-existent", req, CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<TenantDetailResponse>>(notFound.Value);
        Assert.False(resp.Success);
        Assert.Equal("TENANT_NOT_FOUND", resp.ErrorCode);
    }

    [Fact]
    public async Task LockTenant_ValidSoftLock_Returns200Ok()
    {
        var req = new LockTenantRequest("SOFT", "Chậm thanh toán kỳ cước");
        var result = await _controller.LockTenant("tenant-01", req, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<bool>>(ok.Value);
        Assert.True(resp.Success);
        Assert.True(resp.Data);
    }

    [Fact]
    public async Task LockTenant_InvalidValidation_Returns400BadRequest()
    {
        var badReq = new LockTenantRequest("INVALID_TYPE", ""); // rỗng lý do và sai type
        var result = await _controller.LockTenant("tenant-01", badReq, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<bool>>(badRequest.Value);
        Assert.False(resp.Success);
        Assert.Equal("VALIDATION_ERROR", resp.ErrorCode);
    }

    [Fact]
    public async Task LockTenant_NotFound_Returns404NotFound()
    {
        var req = new LockTenantRequest("HARD", "Vi phạm chính sách");
        var result = await _controller.LockTenant("non-existent", req, CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<bool>>(notFound.Value);
        Assert.False(resp.Success);
        Assert.Equal("TENANT_NOT_FOUND", resp.ErrorCode);
    }

    [Fact]
    public async Task UnlockTenant_Success_Returns200Ok()
    {
        var result = await _controller.UnlockTenant("tenant-01", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<bool>>(ok.Value);
        Assert.True(resp.Success);
    }

    [Fact]
    public async Task UnlockTenant_NotFound_Returns404NotFound()
    {
        var result = await _controller.UnlockTenant("non-existent", CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<bool>>(notFound.Value);
        Assert.False(resp.Success);
        Assert.Equal("TENANT_NOT_FOUND", resp.ErrorCode);
    }

    [Fact]
    public async Task ExtendGracePeriod_ValidDays_Returns200Ok()
    {
        var req = new ExtendGraceRequest(7, "Chờ chuyển khoản ngân hàng");
        var result = await _controller.ExtendGracePeriod("tenant-01", req, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<bool>>(ok.Value);
        Assert.True(resp.Success);
    }

    [Fact]
    public async Task ExtendGracePeriod_InvalidValidation_Returns400BadRequest()
    {
        var badReq = new ExtendGraceRequest(0, ""); // ngày = 0 và lý do rỗng
        var result = await _controller.ExtendGracePeriod("tenant-01", badReq, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<bool>>(badRequest.Value);
        Assert.False(resp.Success);
        Assert.Equal("VALIDATION_ERROR", resp.ErrorCode);
    }

    [Fact]
    public async Task ExtendGracePeriod_NotFound_Returns404NotFound()
    {
        var req = new ExtendGraceRequest(5, "Chờ xác nhận");
        var result = await _controller.ExtendGracePeriod("non-existent", req, CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<bool>>(notFound.Value);
        Assert.False(resp.Success);
        Assert.Equal("TENANT_NOT_FOUND", resp.ErrorCode);
    }
}

