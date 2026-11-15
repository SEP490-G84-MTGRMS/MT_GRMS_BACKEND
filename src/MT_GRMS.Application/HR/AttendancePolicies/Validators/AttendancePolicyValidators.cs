using System.Text.Json;
using FluentValidation;
using MT_GRMS.Application.HR.AttendancePolicies.Dtos;

namespace MT_GRMS.Application.HR.AttendancePolicies.Validators;

public class UpsertAttendancePolicyRequestValidator : AbstractValidator<UpsertAttendancePolicyRequest>
{
    public UpsertAttendancePolicyRequestValidator()
    {
        RuleFor(x => x.StandardDayHours)
            .GreaterThan(0).WithMessage("Số giờ của 1 ngày công chuẩn phải lớn hơn 0.")
            .LessThanOrEqualTo(24).WithMessage("Số giờ của 1 ngày công chuẩn không được vượt quá 24 giờ.");

        RuleFor(x => x.LateThresholdMinutes)
            .GreaterThanOrEqualTo(0).WithMessage("Số phút tính đi muộn không được âm.")
            .LessThanOrEqualTo(240).WithMessage("Số phút tính đi muộn tối đa là 240 phút.");

        RuleFor(x => x.EarlyLeaveThresholdMinutes)
            .GreaterThanOrEqualTo(0).WithMessage("Số phút tính về sớm không được âm.")
            .LessThanOrEqualTo(240).WithMessage("Số phút tính về sớm tối đa là 240 phút.");

        When(x => x.IsConvertHoursToDays && !string.IsNullOrWhiteSpace(x.ConversionRulesJson), () =>
        {
            RuleFor(x => x.ConversionRulesJson!)
                .Must(BeValidConversionRulesJson)
                .WithMessage("Quy tắc quy đổi công không hợp lệ: mốc giờ kết thúc phải lớn hơn mốc giờ bắt đầu và số công phải nằm trong khoảng (0, 5].");
        });
    }

    private static bool BeValidConversionRulesJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }

                var type = item.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : "range";
                var fromHours = item.TryGetProperty("fromHours", out var fh) && fh.TryGetInt32(out var fhVal) ? fhVal : 0;
                var fromMinutes = item.TryGetProperty("fromMinutes", out var fm) && fm.TryGetInt32(out var fmVal) ? fmVal : 0;
                var workUnits = item.TryGetProperty("workUnits", out var wu) && wu.TryGetDecimal(out var wuVal) ? wuVal : 0m;

                if (fromHours < 0 || fromHours > 24 || fromMinutes < 0 || fromMinutes > 59 || workUnits <= 0 || workUnits > 5)
                {
                    return false;
                }

                if (string.Equals(type, "range", StringComparison.OrdinalIgnoreCase))
                {
                    var toHours = item.TryGetProperty("toHours", out var th) && th.TryGetInt32(out var thVal) ? thVal : 0;
                    var toMinutes = item.TryGetProperty("toMinutes", out var tm) && tm.TryGetInt32(out var tmVal) ? tmVal : 0;
                    if (toHours < 0 || toHours > 24 || toMinutes < 0 || toMinutes > 59)
                    {
                        return false;
                    }

                    var fromTotal = fromHours * 60 + fromMinutes;
                    var toTotal = toHours * 60 + toMinutes;
                    if (toTotal <= fromTotal)
                    {
                        return false;
                    }
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}

public class CopyAttendancePolicyRequestValidator : AbstractValidator<CopyAttendancePolicyRequest>
{
    public CopyAttendancePolicyRequestValidator()
    {
        RuleFor(x => x.SourceBranchId)
            .NotEmpty().WithMessage("Chi nhánh nguồn (SourceBranchId) không được để trống.");

        RuleFor(x => x.TargetBranchIds)
            .NotNull().WithMessage("Danh sách chi nhánh đích không được null.")
            .Must(t => t != null && t.Count > 0).WithMessage("Phải chọn ít nhất 1 chi nhánh đích để sao chép.")
            .Must((req, targetIds) => targetIds == null || !targetIds.Contains(req.SourceBranchId))
            .WithMessage("Danh sách chi nhánh đích không được bao gồm chi nhánh nguồn.");
    }
}

