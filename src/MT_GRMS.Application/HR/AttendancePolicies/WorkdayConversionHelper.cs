using System.Text.Json;
using MT_GRMS.Domain.Entities.SaaS;

namespace MT_GRMS.Application.HR.AttendancePolicies;

/// <summary>
/// Quy đổi số giờ làm việc thực tế sang số ngày công dựa trên cấu hình ConversionRulesJson của chi nhánh.
/// </summary>
public static class WorkdayConversionHelper
{
    public const string DefaultConversionRulesJson =
        "[{\"id\":\"rule-1\",\"type\":\"range\",\"fromHours\":1,\"fromMinutes\":0,\"toHours\":4,\"toMinutes\":30,\"workUnits\":0.5,\"workUnitsLabel\":\"nửa công\"},{\"id\":\"rule-2\",\"type\":\"gte\",\"fromHours\":4,\"fromMinutes\":30,\"workUnits\":1,\"workUnitsLabel\":\"1 công\"}]";

    public static decimal CalculateWorkUnits(decimal workHours, Branch? policy)
    {
        if (workHours <= 0m)
        {
            return 0m;
        }

        var standardDayHours = policy?.StandardDayHours ?? 8.00m;
        if (standardDayHours <= 0m)
        {
            standardDayHours = 8.00m;
        }

        var shouldConvert = policy == null || policy.IsConvertHoursToDays;
        var rulesJson = (policy != null && !string.IsNullOrWhiteSpace(policy.ConversionRulesJson))
            ? policy.ConversionRulesJson
            : DefaultConversionRulesJson;

        if (shouldConvert && !string.IsNullOrWhiteSpace(rulesJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(rulesJson);
                if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
                {
                    var rules = new List<(string Type, int FromMinutes, int ToMinutes, decimal WorkUnits)>();
                    foreach (var item in doc.RootElement.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.Object) continue;
                        var type = item.TryGetProperty("type", out var tProp) ? (tProp.GetString() ?? "range") : "range";
                        var fh = item.TryGetProperty("fromHours", out var fhProp) && fhProp.TryGetInt32(out var fhv) ? fhv : 0;
                        var fm = item.TryGetProperty("fromMinutes", out var fmProp) && fmProp.TryGetInt32(out var fmv) ? fmv : 0;
                        var th = item.TryGetProperty("toHours", out var thProp) && thProp.TryGetInt32(out var thv) ? thv : 0;
                        var tm = item.TryGetProperty("toMinutes", out var tmProp) && tmProp.TryGetInt32(out var tmv) ? tmv : 0;
                        var wu = item.TryGetProperty("workUnits", out var wuProp) && wuProp.TryGetDecimal(out var wuv) ? wuv : 0m;

                        if (wu > 0m)
                        {
                            rules.Add((type, fh * 60 + fm, th * 60 + tm, wu));
                        }
                    }

                    if (rules.Count > 0)
                    {
                        var totalMinutes = (int)Math.Round(workHours * 60m, MidpointRounding.AwayFromZero);
                        decimal matchedUnits = 0m;
                        var matched = false;

                        foreach (var rule in rules)
                        {
                            if (string.Equals(rule.Type, "range", StringComparison.OrdinalIgnoreCase))
                            {
                                if (totalMinutes >= rule.FromMinutes && totalMinutes < rule.ToMinutes)
                                {
                                    matchedUnits = Math.Max(matchedUnits, rule.WorkUnits);
                                    matched = true;
                                }
                            }
                            else if (string.Equals(rule.Type, "gte", StringComparison.OrdinalIgnoreCase))
                            {
                                if (totalMinutes >= rule.FromMinutes)
                                {
                                    matchedUnits = Math.Max(matchedUnits, rule.WorkUnits);
                                    matched = true;
                                }
                            }
                        }

                        return matched ? Math.Round(matchedUnits, 2) : 0m;
                    }
                }
            }
            catch
            {
                // Fallback sang quy đổi tuyến tính nếu JSON không hợp lệ
            }
        }

        return Math.Round(workHours / standardDayHours, 2);
    }
}
