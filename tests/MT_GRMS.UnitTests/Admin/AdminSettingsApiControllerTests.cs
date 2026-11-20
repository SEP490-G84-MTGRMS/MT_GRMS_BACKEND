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

public class MockPlatformSettingService : IPlatformSettingService
{
    public PlatformSettingDto CurrentSettings { get; set; } = new(
        Id: 1,
        TrialDays: 3,
        GracePeriodDays: 7,
        BranchAddonPriceMonthly: 80000m,
        StorageAddonPricePerGbMonthly: 15000m,
        MasterBankCode: "MBBank",
        MasterBankName: "MB Bank (Ngân hàng Quân Đội)",
        MasterAccountNumber: "190065228888",
        MasterAccountName: "CONG TY TNHH PHAN MEM MT-GRMS",
        SupportHotline: "1900 6522",
        SupportEmail: "support@thviet.vn",
        SupportZalo: "0912345678",
        IsMaintenanceMode: false,
        MaintenanceMessage: "Hệ thống đang bảo trì",
        MaintenanceExpectedEndTime: null,
        UpdatedAt: DateTime.UtcNow,
        UpdatedBy: "Admin"
    );

    public Task<PlatformSettingDto> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(CurrentSettings);
    }

    public Task<PlatformSettingDto> UpdateSettingsAsync(UpdatePlatformSettingRequest request, string? updatedBy, CancellationToken cancellationToken = default)
    {
        CurrentSettings = new PlatformSettingDto(
            Id: 1,
            TrialDays: request.TrialDays,
            GracePeriodDays: request.GracePeriodDays,
            BranchAddonPriceMonthly: request.BranchAddonPriceMonthly,
            StorageAddonPricePerGbMonthly: request.StorageAddonPricePerGbMonthly,
            MasterBankCode: request.MasterBankCode,
            MasterBankName: request.MasterBankName,
            MasterAccountNumber: request.MasterAccountNumber,
            MasterAccountName: request.MasterAccountName,
            SupportHotline: request.SupportHotline,
            SupportEmail: request.SupportEmail,
            SupportZalo: request.SupportZalo,
            IsMaintenanceMode: request.IsMaintenanceMode,
            MaintenanceMessage: request.MaintenanceMessage,
            MaintenanceExpectedEndTime: request.MaintenanceExpectedEndTime,
            UpdatedAt: DateTime.UtcNow,
            UpdatedBy: updatedBy
        );
        return Task.FromResult(CurrentSettings);
    }

    public Task<PlatformPublicConfigDto> GetPublicConfigAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new PlatformPublicConfigDto(
            DefaultTrialDays: CurrentSettings.TrialDays,
            MasterBankCode: CurrentSettings.MasterBankCode,
            MasterBankName: CurrentSettings.MasterBankName,
            MasterAccountNumber: CurrentSettings.MasterAccountNumber,
            MasterAccountName: CurrentSettings.MasterAccountName,
            SupportHotline: CurrentSettings.SupportHotline,
            SupportEmail: CurrentSettings.SupportEmail,
            SupportZalo: CurrentSettings.SupportZalo,
            IsMaintenanceMode: CurrentSettings.IsMaintenanceMode,
            MaintenanceMessage: CurrentSettings.MaintenanceMessage,
            MaintenanceExpectedEndTime: CurrentSettings.MaintenanceExpectedEndTime
        ));
    }
}

public class AdminSettingsApiControllerTests
{
    private readonly MockPlatformSettingService _service;
    private readonly IValidator<UpdatePlatformSettingRequest> _validator;
    private readonly AdminSettingsController _controller;
    private readonly PlatformPublicConfigController _publicController;

    public AdminSettingsApiControllerTests()
    {
        _service = new MockPlatformSettingService();
        _validator = new UpdatePlatformSettingValidator();
        _controller = new AdminSettingsController(_service, _validator);
        _publicController = new PlatformPublicConfigController(_service);

        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, "AdminUser"),
            new Claim(ClaimTypes.Role, "ADMIN"),
        }, "TestAuth"));

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };
    }

    [Fact]
    public async Task GetSettings_ReturnsOkWithPlatformSettings()
    {
        var result = await _controller.GetSettings(CancellationToken.None);
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<ApiResponse<PlatformSettingDto>>(okResult.Value);

        Assert.True(response.Success);
        Assert.Equal(3, response.Data.TrialDays);
        Assert.Equal(7, response.Data.GracePeriodDays);
        Assert.Equal(80000m, response.Data.BranchAddonPriceMonthly);
        Assert.Equal("MBBank", response.Data.MasterBankCode);
    }

    [Fact]
    public async Task UpdateSettings_ValidRequest_ReturnsOkWithUpdatedSettings()
    {
        var request = new UpdatePlatformSettingRequest(
            TrialDays: 5,
            GracePeriodDays: 10,
            BranchAddonPriceMonthly: 100000m,
            StorageAddonPricePerGbMonthly: 20000m,
            MasterBankCode: "Vietcombank",
            MasterBankName: "Ngân hàng Ngoại Thương",
            MasterAccountNumber: "9988776655",
            MasterAccountName: "CONG TY TNHH THVIET",
            SupportHotline: "1900 8888",
            SupportEmail: "cskh@thviet.vn",
            SupportZalo: "0988888888",
            IsMaintenanceMode: true,
            MaintenanceMessage: "Hệ thống chuẩn bị nâng cấp v2.0",
            MaintenanceExpectedEndTime: DateTime.UtcNow.AddHours(2)
        );

        var result = await _controller.UpdateSettings(request, CancellationToken.None);
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<ApiResponse<PlatformSettingDto>>(okResult.Value);

        Assert.True(response.Success);
        Assert.Equal(5, response.Data.TrialDays);
        Assert.Equal(10, response.Data.GracePeriodDays);
        Assert.Equal(100000m, response.Data.BranchAddonPriceMonthly);
        Assert.Equal("Vietcombank", response.Data.MasterBankCode);
        Assert.True(response.Data.IsMaintenanceMode);
        Assert.Equal("AdminUser", response.Data.UpdatedBy);
    }

    [Fact]
    public async Task UpdateSettings_InvalidRequest_ReturnsBadRequest()
    {
        var request = new UpdatePlatformSettingRequest(
            TrialDays: 0, // Invalid: must be >= 1
            GracePeriodDays: 7,
            BranchAddonPriceMonthly: -1000m, // Invalid: cannot be negative
            StorageAddonPricePerGbMonthly: 15000m,
            MasterBankCode: "", // Invalid: empty
            MasterBankName: "MB",
            MasterAccountNumber: "",
            MasterAccountName: "",
            SupportHotline: "",
            SupportEmail: "invalid-email", // Invalid email format
            SupportZalo: "",
            IsMaintenanceMode: false,
            MaintenanceMessage: null,
            MaintenanceExpectedEndTime: null
        );

        var result = await _controller.UpdateSettings(request, CancellationToken.None);
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        var response = Assert.IsType<ApiResponse<PlatformSettingDto>>(badRequestResult.Value);

        Assert.False(response.Success);
        Assert.Equal("VALIDATION_ERROR", response.ErrorCode);
    }

    [Fact]
    public async Task GetPublicConfig_ReturnsOkWithPublicData()
    {
        var result = await _publicController.GetPublicConfig(CancellationToken.None);
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<ApiResponse<PlatformPublicConfigDto>>(okResult.Value);

        Assert.True(response.Success);
        Assert.Equal(3, response.Data.DefaultTrialDays);
        Assert.Equal("MBBank", response.Data.MasterBankCode);
        Assert.Equal("1900 6522", response.Data.SupportHotline);
    }
}
