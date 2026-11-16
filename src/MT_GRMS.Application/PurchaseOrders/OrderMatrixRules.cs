using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MT_GRMS.Application.PurchaseOrders.Dtos;

namespace MT_GRMS.Application.PurchaseOrders;

/// <summary>
/// Quy tắc dùng chung cho Order Matrix (FinalQty, Cutoff, DeliveryDateStatus, Scopes).
/// </summary>
public static class OrderMatrixRules
{
    public const string StatusOpen = "OPEN";
    public const string StatusCutoffDue = "CUTOFF_DUE";
    public const string StatusLocked = "LOCKED";
    public const string StatusFinalized = "FINALIZED";
    public const string StatusSupplierOff = "SUPPLIER_OFF";

    public const string ScopeNearest = "NEAREST";
    public const string ScopeWeek = "WEEK";
    public const string ScopeAll = "ALL";
    /// <summary>Rolling window từ hôm nay: 7 / 14 / 21 / 31 ngày.</summary>
    public const string ScopeDays7 = "DAYS_7";
    public const string ScopeDays14 = "DAYS_14";
    public const string ScopeDays21 = "DAYS_21";
    public const string ScopeDays31 = "DAYS_31";

    public static int? TryResolveRollingDurationDays(string? scope)
    {
        var s = (scope ?? string.Empty).Trim().ToUpperInvariant();
        return s switch
        {
            ScopeDays7 => 7,
            ScopeDays14 => 14,
            ScopeDays21 => 21,
            ScopeDays31 => 31,
            _ => null
        };
    }

    public const string ResultSuccess = "SUCCESS";
    public const string ResultFailed = "FAILED";
    public const string ResultSkipped = "SKIPPED";
    public const string ResultAlreadyFinalized = "ALREADY_FINALIZED";
    public const string ResultAmended = "AMENDED";
    public const string ResultNoChanges = "NO_CHANGES";

    private static readonly Lazy<TimeZoneInfo> VietnamTimeZoneLazy = new(() =>
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
        }
        catch
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
            }
            catch
            {
                return TimeZoneInfo.CreateCustomTimeZone(
                    "Asia/Ho_Chi_Minh",
                    TimeSpan.FromHours(7),
                    "Vietnam Standard Time",
                    "Vietnam Standard Time");
            }
        }
    });

    public static TimeZoneInfo GetVietnamTimeZone() => VietnamTimeZoneLazy.Value;

    public static DateTime GetVietnamNow()
    {
        return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, GetVietnamTimeZone());
    }

    /// <summary>
    /// Chuẩn hóa ngày về Thứ Hai (Monday) của tuần chứa ngày đó (Monday..Sunday).
    /// </summary>
    public static DateTime NormalizeToMonday(DateTime date)
    {
        var dow = date.DayOfWeek;
        var diff = (7 + (dow - DayOfWeek.Monday)) % 7;
        return date.Date.AddDays(-diff);
    }

    /// <summary>
    /// Lấy khoảng thời gian tuần (Thứ Hai 00:00:00 -> Chủ Nhật 23:59:59.999).
    /// </summary>
    public static (DateTime Monday, DateTime Sunday) GetWeekRange(DateTime date)
    {
        var monday = NormalizeToMonday(date);
        var sunday = monday.AddDays(6);
        return (monday, sunday);
    }

    public static decimal ComputeFinalQty(decimal employeeQty, decimal? storeQty, bool isOwnerAdjusted)
    {
        if (isOwnerAdjusted || storeQty.HasValue)
        {
            return storeQty ?? 0m;
        }

        return employeeQty;
    }

    public static (int Hour, int Minute) ParseCutoffTime(string? cutoffTime)
    {
        var hour = 15;
        var minute = 0;
        if (!string.IsNullOrWhiteSpace(cutoffTime) && cutoffTime.Contains(':'))
        {
            var parts = cutoffTime.Split(':');
            if (int.TryParse(parts[0], out var h)) hour = Math.Clamp(h, 0, 23);
            if (parts.Length > 1 && int.TryParse(parts[1], out var m)) minute = Math.Clamp(m, 0, 59);
        }

        return (hour, minute);
    }

    /// <summary>Lead time tối thiểu (ngày): không cho đặt giao hôm nay / quá khứ.</summary>
    public const int MinimumLeadTimeDays = 1;

    /// <summary>
    /// Cutoff của ngày giao D = (D − cutoffDayOffset) ở giờ CutoffTime.
    /// offset=1 → chốt ngày trước ngày giao; offset=0 → chốt cùng ngày giao.
    /// </summary>
    public static DateTime GetCutoffDateTime(DateTime deliveryDate, string? cutoffTime, int cutoffDayOffset = 1)
    {
        var (hour, minute) = ParseCutoffTime(cutoffTime);
        var offset = Math.Max(0, cutoffDayOffset);
        var cutoffDay = deliveryDate.Date.AddDays(-offset);
        return new DateTime(cutoffDay.Year, cutoffDay.Month, cutoffDay.Day, hour, minute, 0, DateTimeKind.Unspecified);
    }

    /// <summary>Overload cũ: tham số giữa là cutoffDayOffset (không còn là leadTimeDays).</summary>
    public static DateTime GetCutoffDateTime(DateTime deliveryDate, int cutoffDayOffset, string? cutoffTime)
        => GetCutoffDateTime(deliveryDate, cutoffTime, cutoffDayOffset);

    /// <summary>
    /// Ngày giao sớm nhất được phép đặt = hôm nay + MinimumLeadTimeDays (theo lịch VN).
    /// </summary>
    public static DateTime GetEarliestAllowedDeliveryDate(DateTime now)
        => now.Date.AddDays(MinimumLeadTimeDays);

    public static bool IsSupplierDeliveryDay(OrderMatrixSupplierDeliveryConfigDto config, DateTime deliveryDate)
    {
        if (!config.IsActive || !config.IsAssignedToBranch)
        {
            return false;
        }

        var dow = (int)deliveryDate.DayOfWeek; // 0=CN .. 6=T7
        if (config.ActiveDeliveryDays != null && config.ActiveDeliveryDays.Count > 0
            && !config.ActiveDeliveryDays.Contains(dow))
        {
            return false;
        }

        var key = deliveryDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (config.BlackoutDates != null && config.BlackoutDates.Contains(key))
        {
            return false;
        }

        return true;
    }

    public static OrderMatrixDayStatusDto EvaluateDayStatus(
        Guid supplierId,
        DateTime deliveryDate,
        OrderMatrixSupplierDeliveryConfigDto config,
        DateTime now,
        string? committedPoCode = null,
        Guid? committedPoId = null)
    {
        var cutoff = GetCutoffDateTime(deliveryDate, config.CutoffTime, config.CutoffDayOffset);

        if (!config.IsActive)
        {
            return Off(supplierId, deliveryDate, cutoff, "SUPPLIER_INACTIVE");
        }

        if (!config.IsAssignedToBranch)
        {
            return Off(supplierId, deliveryDate, cutoff, "SUPPLIER_BRANCH_ASSIGNMENT_INACTIVE");
        }

        var dow = (int)deliveryDate.DayOfWeek;
        if (config.ActiveDeliveryDays != null && config.ActiveDeliveryDays.Count > 0
            && !config.ActiveDeliveryDays.Contains(dow))
        {
            return Off(supplierId, deliveryDate, cutoff, "NOT_DELIVERY_DAY");
        }

        var key = deliveryDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (config.BlackoutDates != null && config.BlackoutDates.Contains(key))
        {
            return Off(supplierId, deliveryDate, cutoff, "HOLIDAY_BLACKOUT");
        }

        var hasCommittedPo = !string.IsNullOrWhiteSpace(committedPoCode) || committedPoId.HasValue;

        // PO đã tồn tại nhưng đã qua cutoff → khóa cứng (FINALIZED).
        if (hasCommittedPo && now >= cutoff)
        {
            return new OrderMatrixDayStatusDto
            {
                SupplierId = supplierId,
                DeliveryDate = deliveryDate.Date,
                DeliveryDateStatus = StatusFinalized,
                CutoffDateTime = cutoff,
                PrimaryReasonCode = "PO_COMMITTED_CUTOFF_PASSED",
                PoCode = committedPoCode,
                PoId = committedPoId
            };
        }

        // Không cho đặt giao ngày quá khứ
        if (deliveryDate.Date < now.Date)
        {
            return Off(supplierId, deliveryDate, cutoff, "PAST_DELIVERY_DATE");
        }

        // Lead time ≥ 1 ngày: không cho đặt giao hôm nay (ngày giao sớm nhất = ngày mai)
        if (deliveryDate.Date < GetEarliestAllowedDeliveryDate(now))
        {
            return Off(supplierId, deliveryDate, cutoff, "LEAD_TIME_VIOLATION");
        }

        if (now >= cutoff)
        {
            return new OrderMatrixDayStatusDto
            {
                SupplierId = supplierId,
                DeliveryDate = deliveryDate.Date,
                DeliveryDateStatus = StatusLocked,
                CutoffDateTime = cutoff,
                PrimaryReasonCode = "CUTOFF_PASSED"
            };
        }

        // Trước cutoff: vẫn OPEN/CUTOFF_DUE kể cả khi đã có PO (cho phép amend).
        var status = (cutoff - now) <= TimeSpan.FromHours(24) ? StatusCutoffDue : StatusOpen;
        return new OrderMatrixDayStatusDto
        {
            SupplierId = supplierId,
            DeliveryDate = deliveryDate.Date,
            DeliveryDateStatus = status,
            CutoffDateTime = cutoff,
            PrimaryReasonCode = hasCommittedPo ? "PO_EXISTED_EDITABLE" : null,
            PoCode = committedPoCode,
            PoId = committedPoId
        };
    }

    public static bool IsEditableStatus(string? deliveryDateStatus)
        => deliveryDateStatus is StatusOpen or StatusCutoffDue;

    public static bool ViolatesMoq(decimal finalQty, int moq)
        => finalQty > 0 && moq > 0 && finalQty < moq;

    /// <summary>MOQ tổng đơn theo NCC × ngày giao: có đặt hàng nhưng tổng SL &lt; MinOrderQuantity.</summary>
    public static bool ViolatesSupplierOrderMoq(decimal dayTotalQty, int minOrderQuantity)
        => dayTotalQty > 0 && minOrderQuantity > 0 && dayTotalQty < minOrderQuantity;

    public static bool ViolatesOrderMultiple(decimal finalQty, int orderMultiple)
    {
        if (finalQty <= 0) return false;
        var multiple = orderMultiple <= 0 ? 1 : orderMultiple;
        return finalQty % multiple != 0;
    }

    public static IReadOnlyList<OrderMatrixDayStatusDto> BuildDayStatuses(
        IEnumerable<OrderMatrixSupplierDeliveryConfigDto> configs,
        DateTime viewFrom,
        int durationDays,
        DateTime now,
        IReadOnlyDictionary<(Guid SupplierId, DateTime Date), (Guid PoId, string Code)> committed)
    {
        var days = Math.Clamp(durationDays, 1, 31);
        var list = new List<OrderMatrixDayStatusDto>();
        foreach (var config in configs)
        {
            for (var i = 0; i < days; i++)
            {
                var date = viewFrom.Date.AddDays(i);
                committed.TryGetValue((config.SupplierId, date), out var po);
                list.Add(EvaluateDayStatus(
                    config.SupplierId,
                    date,
                    config,
                    now,
                    po.Code,
                    po.PoId == Guid.Empty ? null : po.PoId));
            }
        }

        return list;
    }

    private static OrderMatrixDayStatusDto Off(
        Guid supplierId,
        DateTime deliveryDate,
        DateTime cutoff,
        string reason)
        => new()
        {
            SupplierId = supplierId,
            DeliveryDate = deliveryDate.Date,
            DeliveryDateStatus = StatusSupplierOff,
            CutoffDateTime = cutoff,
            PrimaryReasonCode = reason
        };
}
