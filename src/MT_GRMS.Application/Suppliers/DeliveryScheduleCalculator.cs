using MT_GRMS.Domain.Entities.Supplier;

namespace MT_GRMS.Application.Suppliers;

public static class DeliveryScheduleCalculator
{
    public static int DayOfWeekToMask(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => 1,
        DayOfWeek.Tuesday => 2,
        DayOfWeek.Wednesday => 4,
        DayOfWeek.Thursday => 8,
        DayOfWeek.Friday => 16,
        DayOfWeek.Saturday => 32,
        DayOfWeek.Sunday => 64,
        _ => 0
    };

    public static int DayNamesToMask(IEnumerable<string>? days)
    {
        if (days == null) return 127; // Mặc định tất cả các ngày 1+2+4+8+16+32+64 = 127
        int mask = 0;
        foreach (var d in days)
        {
            var clean = d.Trim().ToLowerInvariant();
            if (clean.StartsWith("mon") || clean == "2" || clean == "t2") mask |= 1;
            else if (clean.StartsWith("tue") || clean == "3" || clean == "t3") mask |= 2;
            else if (clean.StartsWith("wed") || clean == "4" || clean == "t4") mask |= 4;
            else if (clean.StartsWith("thu") || clean == "5" || clean == "t5") mask |= 8;
            else if (clean.StartsWith("fri") || clean == "6" || clean == "t6") mask |= 16;
            else if (clean.StartsWith("sat") || clean == "7" || clean == "t7") mask |= 32;
            else if (clean.StartsWith("sun") || clean == "cn" || clean == "8") mask |= 64;
        }
        return mask == 0 ? 127 : mask;
    }

    public static List<string> MaskToDayNames(int mask)
    {
        var list = new List<string>();
        if ((mask & 1) != 0) list.Add("Mon");
        if ((mask & 2) != 0) list.Add("Tue");
        if ((mask & 4) != 0) list.Add("Wed");
        if ((mask & 8) != 0) list.Add("Thu");
        if ((mask & 16) != 0) list.Add("Fri");
        if ((mask & 32) != 0) list.Add("Sat");
        if ((mask & 64) != 0) list.Add("Sun");
        return list;
    }

    /// <summary>
    /// Tính toán ngày giao hàng sớm nhất (Earliest Delivery Date) theo BR-S12 & BR-S13:
    /// - Cutoff Time: nếu vượt giờ chốt trong ngày, dời mốc tính sang ngày hôm sau.
    /// - LeadTimeHours: cộng số giờ chuẩn bị đơn.
    /// - Delivery Weekdays: chỉ chọn ngày NCC chấp nhận giao hàng.
    /// - Blackout Dates: bỏ qua các ngày nghỉ lễ/ngưng giao (hỗ trợ cả lặp lại hàng năm).
    /// </summary>
    public static DateTime CalculateEarliestDeliveryDate(
        DateTime orderTime,
        int leadTimeHours,
        string cutoffTimeStr,
        int deliveryWeekdaysMask,
        IEnumerable<SupplierBlackoutDate>? blackoutDates)
    {
        // Phân tích Cutoff Time (HH:mm)
        var cutoffHour = 17;
        var cutoffMinute = 0;
        if (!string.IsNullOrWhiteSpace(cutoffTimeStr) && cutoffTimeStr.Contains(':'))
        {
            var parts = cutoffTimeStr.Split(':');
            if (int.TryParse(parts[0], out var h)) cutoffHour = h;
            if (parts.Length > 1 && int.TryParse(parts[1], out var m)) cutoffMinute = m;
        }

        var cutoffToday = new DateTime(orderTime.Year, orderTime.Month, orderTime.Day, cutoffHour, cutoffMinute, 0, orderTime.Kind);
        DateTime effectiveStart;

        if (orderTime > cutoffToday)
        {
            // Quá giờ chốt: tính bắt đầu từ 00:00 ngày kế tiếp
            effectiveStart = orderTime.Date.AddDays(1);
        }
        else
        {
            effectiveStart = orderTime;
        }

        // Thời điểm khả dụng sau Lead Time
        var candidateDateTime = effectiveStart.AddHours(Math.Max(leadTimeHours, 0));
        var candidateDate = candidateDateTime.Date;

        var blackoutList = blackoutDates?.ToList() ?? new List<SupplierBlackoutDate>();

        // Duyệt tìm ngày giao hợp lệ đầu tiên (tối đa tìm trong 60 ngày để tránh infinite loop)
        for (int i = 0; i < 60; i++)
        {
            var checkDate = candidateDate.AddDays(i);
            var dayMask = DayOfWeekToMask(checkDate.DayOfWeek);

            // 1. Kiểm tra có nằm trong ngày nhận giao trong tuần không
            if ((deliveryWeekdaysMask & dayMask) == 0)
            {
                continue;
            }

            // 2. Kiểm tra có rơi vào Blackout Date (nghỉ lễ / tạm ngưng) không
            bool isBlackout = false;
            foreach (var bo in blackoutList)
            {
                if (bo.IsRecurringAnnual)
                {
                    // So sánh theo Tháng/Ngày
                    var startMonthDay = (bo.StartDate.Month, bo.StartDate.Day);
                    var endMonthDay = (bo.EndDate.Month, bo.EndDate.Day);
                    var currentMonthDay = (checkDate.Month, checkDate.Day);

                    if (startMonthDay.CompareTo(endMonthDay) <= 0)
                    {
                        if (currentMonthDay.CompareTo(startMonthDay) >= 0 && currentMonthDay.CompareTo(endMonthDay) <= 0)
                        {
                            isBlackout = true;
                            break;
                        }
                    }
                    else
                    {
                        // Vượt qua năm mới (ví dụ từ 31/12 đến 02/01)
                        if (currentMonthDay.CompareTo(startMonthDay) >= 0 || currentMonthDay.CompareTo(endMonthDay) <= 0)
                        {
                            isBlackout = true;
                            break;
                        }
                    }
                }
                else
                {
                    if (checkDate >= bo.StartDate.Date && checkDate <= bo.EndDate.Date)
                    {
                        isBlackout = true;
                        break;
                    }
                }
            }

            if (!isBlackout)
            {
                // Tìm thấy ngày hợp lệ đầu tiên!
                return checkDate;
            }
        }

        return candidateDate;
    }
}
