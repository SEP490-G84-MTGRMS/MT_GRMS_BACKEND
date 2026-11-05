using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using MT_GRMS.Api.Controllers;
using MT_GRMS.Application.Admin.Dtos;
using MT_GRMS.Application.Admin.Services;
using MT_GRMS.Application.Admin.Validators;
using MT_GRMS.Application.Common;
using Xunit;

namespace MT_GRMS.UnitTests.Admin;

public class MockPlanService : ISubscriptionPlanService
{
    public List<PlanSummaryResponse> Plans { get; } = new();
    public bool ShouldThrowDuplicateCode { get; set; }
    public bool ShouldThrowNotFound { get; set; }
    public bool ShouldThrowInUse { get; set; }

    public Task<IReadOnlyList<PlanSummaryResponse>> GetPlansAsync(PlanFilterRequest filter, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<PlanSummaryResponse>>(Plans);
    }

    public Task<PlanKpiBannerResponse> GetKpisAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new PlanKpiBannerResponse(Plans.Count, Plans.Count, 15, 75000000));
    }

    public Task<PlanDetailResponse> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        if (ShouldThrowNotFound || id == "non-existent")
        {
            throw new KeyNotFoundException($"Không tìm thấy gói dịch vụ với mã định danh '{id}'.");
        }

        return Task.FromResult(new PlanDetailResponse(
            Id: id,
            Code: "PRO",
            Name: "Gói Chuyên Nghiệp",
            Description: "Dành cho chuỗi",
            MonthlyPrice: 990000,
            YearlyPrice: 9900000,
            Status: "ACTIVE",
            ActiveTenantCount: 5,
            Quotas: new PlanQuotaDto(),
            Features: new PlanFeatureDto(),
            Tenants: new List<PlanTenantSummaryDto>(),
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: null
        ));
    }

    public Task<PlanDetailResponse> CreatePlanAsync(CreatePlanRequest request, CancellationToken cancellationToken = default)
    {
        if (ShouldThrowDuplicateCode || request.Code == "DUPLICATE")
        {
            throw new InvalidOperationException($"Mã gói cước '{request.Code}' đã được sử dụng.");
        }

        return Task.FromResult(new PlanDetailResponse(
            Id: "new-plan-id",
            Code: request.Code,
            Name: request.Name,
            Description: request.Description ?? "",
            MonthlyPrice: request.MonthlyPrice,
            YearlyPrice: request.YearlyPrice,
            Status: "ACTIVE",
            ActiveTenantCount: 0,
            Quotas: request.Quotas ?? new PlanQuotaDto(),
            Features: request.Features ?? new PlanFeatureDto(),
            Tenants: new List<PlanTenantSummaryDto>(),
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: null
        ));
    }

    public Task<PlanDetailResponse> UpdatePlanAsync(string id, UpdatePlanRequest request, CancellationToken cancellationToken = default)
    {
        if (ShouldThrowNotFound || id == "non-existent")
        {
            throw new KeyNotFoundException($"Không tìm thấy gói dịch vụ với mã định danh '{id}'.");
        }

        return Task.FromResult(new PlanDetailResponse(
            Id: id,
            Code: "PRO",
            Name: request.Name,
            Description: request.Description ?? "",
            MonthlyPrice: request.MonthlyPrice,
            YearlyPrice: request.YearlyPrice,
            Status: "ACTIVE",
            ActiveTenantCount: 5,
            Quotas: request.Quotas ?? new PlanQuotaDto(),
            Features: request.Features ?? new PlanFeatureDto(),
            Tenants: new List<PlanTenantSummaryDto>(),
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: DateTime.UtcNow
        ));
    }

    public Task<bool> ToggleStatusAsync(string id, CancellationToken cancellationToken = default)
    {
        if (ShouldThrowNotFound || id == "non-existent")
        {
            throw new KeyNotFoundException($"Không tìm thấy gói dịch vụ với mã định danh '{id}'.");
        }

        return Task.FromResult(false);
    }

    public Task DeletePlanAsync(string id, CancellationToken cancellationToken = default)
    {
        if (ShouldThrowNotFound || id == "non-existent")
        {
            throw new KeyNotFoundException($"Không tìm thấy gói dịch vụ với mã định danh '{id}'.");
        }

        if (ShouldThrowInUse || id == "in-use")
        {
            throw new InvalidOperationException("Không thể xóa gói dịch vụ đang có 3 cửa hàng sử dụng.");
        }

        return Task.CompletedTask;
    }
}

public class AdminPlansApiControllerTests
{
    private readonly MockPlanService _service;
    private readonly CreatePlanRequestValidator _createValidator;
    private readonly UpdatePlanRequestValidator _updateValidator;
    private readonly AdminPlansController _controller;

    public AdminPlansApiControllerTests()
    {
        _service = new MockPlanService();
        _createValidator = new CreatePlanRequestValidator();
        _updateValidator = new UpdatePlanRequestValidator();
        _controller = new AdminPlansController(_service, _createValidator, _updateValidator);

        _service.Plans.Add(new PlanSummaryResponse(
            Id: "plan-growth",
            Code: "GROWTH",
            Name: "Gói Tăng Trưởng",
            Description: "Mô tả gói",
            MonthlyPrice: 490000,
            YearlyPrice: 4900000,
            Status: "ACTIVE",
            ActiveTenantCount: 8,
            Quotas: new PlanQuotaDto(),
            Features: new PlanFeatureDto(),
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: null
        ));
    }

    [Fact]
    public async Task GetPlans_Returns200Ok_WithList()
    {
        var result = await _controller.GetPlans(new PlanFilterRequest(), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<IReadOnlyList<PlanSummaryResponse>>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Single(resp.Data!);
    }

    [Fact]
    public async Task GetKpis_Returns200Ok_WithBanner()
    {
        var result = await _controller.GetKpis(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<PlanKpiBannerResponse>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal(1, resp.Data!.TotalPlans);
        Assert.Equal(75000000, resp.Data!.EstimatedMrr);
    }

    [Fact]
    public async Task GetById_Existing_Returns200Ok()
    {
        var result = await _controller.GetById("plan-growth", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<PlanDetailResponse>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal("PRO", resp.Data!.Code);
    }

    [Fact]
    public async Task GetById_NotFound_Returns404NotFound()
    {
        var result = await _controller.GetById("non-existent", CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<PlanDetailResponse>>(notFound.Value);
        Assert.False(resp.Success);
        Assert.Equal("PLAN_NOT_FOUND", resp.ErrorCode);
    }

    [Fact]
    public async Task CreatePlan_ValidInput_Returns201CreatedAtAction()
    {
        var req = new CreatePlanRequest(
            Code: "STARTER_PRO",
            Name: "Gói Khởi Nghiệp Mới",
            Description: "Dành cho shop lẻ",
            MonthlyPrice: 199000,
            YearlyPrice: 1990000,
            Quotas: new PlanQuotaDto(1, 3, 5, "Weekly"),
            Features: new PlanFeatureDto()
        );

        var result = await _controller.CreatePlan(req, CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        var resp = Assert.IsType<ApiResponse<PlanDetailResponse>>(created.Value);
        Assert.True(resp.Success);
        Assert.Equal("STARTER_PRO", resp.Data!.Code);
    }

    [Fact]
    public async Task CreatePlan_InvalidInput_Returns400BadRequest()
    {
        var badReq = new CreatePlanRequest(
            Code: "invalid code!", // ký tự đặc biệt
            Name: "",              // rỗng
            Description: null,
            MonthlyPrice: -500,    // âm
            YearlyPrice: 100,
            Quotas: null,
            Features: null
        );

        var result = await _controller.CreatePlan(badReq, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<PlanDetailResponse>>(badRequest.Value);
        Assert.False(resp.Success);
        Assert.Equal("VALIDATION_ERROR", resp.ErrorCode);
    }

    [Fact]
    public async Task CreatePlan_DuplicateCode_Returns400BadRequest()
    {
        var dupReq = new CreatePlanRequest(
            Code: "DUPLICATE",
            Name: "Gói Trùng Mã",
            Description: null,
            MonthlyPrice: 500000,
            YearlyPrice: 5000000,
            Quotas: null,
            Features: null
        );

        var result = await _controller.CreatePlan(dupReq, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<PlanDetailResponse>>(badRequest.Value);
        Assert.False(resp.Success);
        Assert.Equal("PLAN_DUPLICATE_CODE", resp.ErrorCode);
    }

    [Fact]
    public async Task UpdatePlan_ValidInput_Returns200Ok()
    {
        var req = new UpdatePlanRequest(
            Name: "Gói Đã Đổi Tên",
            Description: "Mô tả mới",
            MonthlyPrice: 590000,
            YearlyPrice: 5900000,
            Quotas: null,
            Features: null
        );

        var result = await _controller.UpdatePlan("plan-growth", req, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<PlanDetailResponse>>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal("Gói Đã Đổi Tên", resp.Data!.Name);
    }

    [Fact]
    public async Task UpdatePlan_InvalidInput_Returns400BadRequest()
    {
        var badReq = new UpdatePlanRequest(
            Name: "", // rỗng
            Description: null,
            MonthlyPrice: -10,
            YearlyPrice: 0,
            Quotas: null,
            Features: null
        );

        var result = await _controller.UpdatePlan("plan-growth", badReq, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<PlanDetailResponse>>(badRequest.Value);
        Assert.False(resp.Success);
        Assert.Equal("VALIDATION_ERROR", resp.ErrorCode);
    }

    [Fact]
    public async Task UpdatePlan_NotFound_Returns404NotFound()
    {
        var req = new UpdatePlanRequest("Gói Mới", null, 100, 1000, null, null);
        var result = await _controller.UpdatePlan("non-existent", req, CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<PlanDetailResponse>>(notFound.Value);
        Assert.False(resp.Success);
        Assert.Equal("PLAN_NOT_FOUND", resp.ErrorCode);
    }

    [Fact]
    public async Task ToggleStatus_Success_Returns200Ok()
    {
        var result = await _controller.ToggleStatus("plan-growth", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<bool>>(ok.Value);
        Assert.True(resp.Success);
        Assert.False(resp.Data);
    }

    [Fact]
    public async Task ToggleStatus_NotFound_Returns404NotFound()
    {
        var result = await _controller.ToggleStatus("non-existent", CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<bool>>(notFound.Value);
        Assert.False(resp.Success);
        Assert.Equal("PLAN_NOT_FOUND", resp.ErrorCode);
    }

    [Fact]
    public async Task DeletePlan_Success_Returns200Ok()
    {
        var result = await _controller.DeletePlan("plan-growth", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<bool>>(ok.Value);
        Assert.True(resp.Success);
    }

    [Fact]
    public async Task DeletePlan_PlanInUse_Returns400BadRequest()
    {
        var result = await _controller.DeletePlan("in-use", CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<bool>>(badRequest.Value);
        Assert.False(resp.Success);
        Assert.Equal("PLAN_IN_USE", resp.ErrorCode);
    }

    [Fact]
    public async Task DeletePlan_NotFound_Returns404NotFound()
    {
        var result = await _controller.DeletePlan("non-existent", CancellationToken.None);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        var resp = Assert.IsType<ApiResponse<bool>>(notFound.Value);
        Assert.False(resp.Success);
        Assert.Equal("PLAN_NOT_FOUND", resp.ErrorCode);
    }
}

