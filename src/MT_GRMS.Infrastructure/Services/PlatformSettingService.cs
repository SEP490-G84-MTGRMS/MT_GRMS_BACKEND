using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Admin.Dtos;
using MT_GRMS.Application.Admin.Services;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.Infrastructure.Services;

public class PlatformSettingService : IPlatformSettingService
{
    private readonly AppDbContext _context;

    public PlatformSettingService(AppDbContext context)
    {
        _context = context;
    }

    private async Task<PlatformSetting> GetOrCreateEntityAsync(CancellationToken cancellationToken)
    {
        var setting = await _context.PlatformSettings.FirstOrDefaultAsync(cancellationToken);
        if (setting == null)
        {
            setting = new PlatformSetting
            {
                Id = 1,
                TrialDays = 3,
                GracePeriodDays = 7,
                BranchAddonPriceMonthly = 80000m,
                StorageAddonPricePerGbMonthly = 15000m,
                MasterBankCode = "MBBank",
                MasterBankName = "MB Bank (Ngân hàng Quân Đội)",
                MasterAccountNumber = "190065228888",
                MasterAccountName = "CONG TY TNHH PHAN MEM MT-GRMS",
                SupportHotline = "1900 6522",
                SupportEmail = "support@thviet.vn",
                SupportZalo = "0912345678",
                IsMaintenanceMode = false,
                MaintenanceMessage = "Hệ thống đang bảo trì nâng cấp. Vui lòng quay lại sau ít phút.",
                UpdatedAt = DateTime.UtcNow
            };
            _context.PlatformSettings.Add(setting);
            await _context.SaveChangesAsync(cancellationToken);
        }
        return setting;
    }

    public async Task<PlatformSettingDto> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        var entity = await GetOrCreateEntityAsync(cancellationToken);
        return MapToDto(entity);
    }

    public async Task<PlatformSettingDto> UpdateSettingsAsync(UpdatePlatformSettingRequest request, string? updatedBy, CancellationToken cancellationToken = default)
    {
        var entity = await GetOrCreateEntityAsync(cancellationToken);

        entity.TrialDays = request.TrialDays;
        entity.GracePeriodDays = request.GracePeriodDays;
        entity.BranchAddonPriceMonthly = request.BranchAddonPriceMonthly;
        entity.StorageAddonPricePerGbMonthly = request.StorageAddonPricePerGbMonthly;

        entity.MasterBankCode = request.MasterBankCode.Trim();
        entity.MasterBankName = request.MasterBankName.Trim();
        entity.MasterAccountNumber = request.MasterAccountNumber.Trim();
        entity.MasterAccountName = request.MasterAccountName.Trim();

        entity.SupportHotline = request.SupportHotline.Trim();
        entity.SupportEmail = request.SupportEmail.Trim();
        entity.SupportZalo = request.SupportZalo.Trim();

        entity.IsMaintenanceMode = request.IsMaintenanceMode;
        entity.MaintenanceMessage = request.MaintenanceMessage?.Trim();
        entity.MaintenanceExpectedEndTime = request.MaintenanceExpectedEndTime;

        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedBy = updatedBy;

        await _context.SaveChangesAsync(cancellationToken);
        return MapToDto(entity);
    }

    public async Task<PlatformPublicConfigDto> GetPublicConfigAsync(CancellationToken cancellationToken = default)
    {
        var entity = await GetOrCreateEntityAsync(cancellationToken);
        return new PlatformPublicConfigDto(
            DefaultTrialDays: entity.TrialDays,
            MasterBankCode: entity.MasterBankCode,
            MasterBankName: entity.MasterBankName,
            MasterAccountNumber: entity.MasterAccountNumber,
            MasterAccountName: entity.MasterAccountName,
            SupportHotline: entity.SupportHotline,
            SupportEmail: entity.SupportEmail,
            SupportZalo: entity.SupportZalo,
            IsMaintenanceMode: entity.IsMaintenanceMode,
            MaintenanceMessage: entity.MaintenanceMessage,
            MaintenanceExpectedEndTime: entity.MaintenanceExpectedEndTime
        );
    }

    private static PlatformSettingDto MapToDto(PlatformSetting entity)
    {
        return new PlatformSettingDto(
            Id: entity.Id,
            TrialDays: entity.TrialDays,
            GracePeriodDays: entity.GracePeriodDays,
            BranchAddonPriceMonthly: entity.BranchAddonPriceMonthly,
            StorageAddonPricePerGbMonthly: entity.StorageAddonPricePerGbMonthly,
            MasterBankCode: entity.MasterBankCode,
            MasterBankName: entity.MasterBankName,
            MasterAccountNumber: entity.MasterAccountNumber,
            MasterAccountName: entity.MasterAccountName,
            SupportHotline: entity.SupportHotline,
            SupportEmail: entity.SupportEmail,
            SupportZalo: entity.SupportZalo,
            IsMaintenanceMode: entity.IsMaintenanceMode,
            MaintenanceMessage: entity.MaintenanceMessage,
            MaintenanceExpectedEndTime: entity.MaintenanceExpectedEndTime,
            UpdatedAt: entity.UpdatedAt ?? entity.CreatedAt,
            UpdatedBy: entity.UpdatedBy
        );
    }
}
