using System;
using System.Collections.Generic;

namespace MT_GRMS.Application.Inventory;

/// <summary>
/// Đang đặt (onOrder) = remaining trên dòng PO mở, theo ĐVT đặt.
/// </summary>
public static class InventoryOnOrder
{
    private static readonly HashSet<string> OpenStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "APPROVED",
        "CONFIRMED",
        "DISPATCHED",
        "SENT"
    };

    public static bool IsOpenPoStatus(string? status)
        => !string.IsNullOrWhiteSpace(status) && OpenStatuses.Contains(status.Trim());

    public static decimal LineRemaining(
        decimal? orderedQty,
        decimal? finalQty,
        decimal receivedQty,
        bool isClosed)
    {
        if (isClosed)
            return 0m;

        var ordered = orderedQty ?? finalQty ?? 0m;
        return Math.Max(0m, ordered - receivedQty);
    }
}
