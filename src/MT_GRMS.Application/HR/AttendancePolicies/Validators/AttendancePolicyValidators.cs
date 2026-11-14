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
            .LessThanOrEqualTo(24).WithMessage("Số giờ của 1 ngày công chuẩn không được vượt quá 24 giờ.")
            .Must(x => decimal.Round(x, 2) == x).WithMessage("Số giờ chuẩn chỉ được phép tối đa 2 chữ số thập phân.");

        RuleFor(x => x.LateThresholdMinutes)
            .InclusiveBetween(0, 240).WithMessage("Số phút tính đi muộn phải nằm trong khoảng từ 0 đến 240 phút.");

        RuleFor(x => x.EarlyLeaveThresholdMinutes)
            .InclusiveBetween(0, 240).WithMessage("Số phút tính về sớm phải nằm trong khoảng từ 0 đến 240 phút.");

        When(x => x.IsConvertHoursToDays, () =>
        {
            RuleFor(x => x.ConversionRulesJson)
                .NotEmpty().WithMessage("Vui lòng thêm ít nhất một quy tắc quy đổi giờ làm thành ngày công.")
                .Must(BeValidConversionRulesJson!)
                .WithMessage("Quy tắc quy đổi công không hợp lệ: mốc giờ phải hợp lệ (tối đa 24:00), mốc kết thúc > mốc bắt đầu, số công trong (0, 5] và các khoảng giờ không được chồng lấn.");
        });
    }

    private static bool BeValidConversionRulesJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
            {
                return false;
            }

            var ranges = new List<(int FromTotal, int ToTotal)>();
            int? gteFromTotal = null;

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

                var fromTotal = fromHours * 60 + fromMinutes;
                if (fromHours < 0 || fromHours > 24 || fromMinutes < 0 || fromMinutes > 59 || fromTotal > 1440)
                {
                    return false;
                }

                if (workUnits <= 0 || workUnits > 5 || decimal.Round(workUnits, 2) != workUnits)
                {
                    return false;
                }

                if (string.Equals(type, "range", StringComparison.OrdinalIgnoreCase))
                {
                    var toHours = item.TryGetProperty("toHours", out var th) && th.TryGetInt32(out var thVal) ? thVal : 0;
                    var toMinutes = item.TryGetProperty("toMinutes", out var tm) && tm.TryGetInt32(out var tmVal) ? tmVal : 0;
                    var toTotal = toHours * 60 + toMinutes;
                    if (toHours < 0 || toHours > 24 || toMinutes < 0 || toMinutes > 59 || toTotal > 1440 || toTotal <= fromTotal)
                    {
                        return false;
                    }

                    ranges.Add((fromTotal, toTotal));
                }
                else if (string.Equals(type, "gte", StringComparison.OrdinalIgnoreCase))
                {
                    if (fromTotal >= 1440 || gteFromTotal.HasValue)
                    {
                        return false;
                    }

                    gteFromTotal = fromTotal;
                }
                else
                {
                    return false;
                }
            }

            // Kiểm tra không chồng lấn giữa các khoảng range
            for (var i = 0; i < ranges.Count; i++)
            {
                if (gteFromTotal.HasValue && ranges[i].ToTotal > gteFromTotal.Value)
                {
                    return false;
                }

                for (var j = i + 1; j < ranges.Count; j++)
                {
                    if (Math.Max(ranges[i].FromTotal, ranges[j].FromTotal) < Math.Min(ranges[i].ToTotal, ranges[j].ToTotal))
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

