using System.Text.Json.Serialization;

namespace MT_GRMS.Application.Admin.Dtos;

public record PlatformSettingDto(
    int Id,
    int TrialDays,
    int GracePeriodDays,
    decimal BranchAddonPriceMonthly,
    decimal StorageAddonPricePerGbMonthly,
    string MasterBankCode,
    string MasterBankName,
    string MasterAccountNumber,
    string MasterAccountName,
    string SupportHotline,
    string SupportEmail,
    string SupportZalo,
    bool IsMaintenanceMode,
    string? MaintenanceMessage,
    DateTime? MaintenanceExpectedEndTime,
    DateTime UpdatedAt,
    string? UpdatedBy
);

public record UpdatePlatformSettingRequest(
    int TrialDays,
    int GracePeriodDays,
    [property: JsonRequired] decimal BranchAddonPriceMonthly,
    [property: JsonRequired] decimal StorageAddonPricePerGbMonthly,
    string MasterBankCode,
    string MasterBankName,
    string MasterAccountNumber,
    string MasterAccountName,
    string SupportHotline,
    string SupportEmail,
    string SupportZalo,
    [property: JsonRequired] bool IsMaintenanceMode,
    string? MaintenanceMessage,
    DateTime? MaintenanceExpectedEndTime
);

public record PlatformPublicConfigDto(
    int DefaultTrialDays,
    string MasterBankCode,
    string MasterBankName,
    string MasterAccountNumber,
    string MasterAccountName,
    string SupportHotline,
    string SupportEmail,
    string SupportZalo,
    bool IsMaintenanceMode,
    string? MaintenanceMessage,
    DateTime? MaintenanceExpectedEndTime
);
