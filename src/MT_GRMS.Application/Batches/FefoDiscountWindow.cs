using MT_GRMS.Domain.Entities.Inventory;

namespace MT_GRMS.Application.Batches;

/// <summary>
/// Cửa sổ hiệu lực giảm giá FEFO — POS đánh giá theo UTC, không dùng worker.
/// </summary>
public static class FefoDiscountWindow
{
    public static bool IsEffective(BranchStock? stock, DateTime utcNow)
    {
        if (stock is not { IsFefoDiscountActive: true, FefoDiscountPercent: > 0m })
            return false;
        if (stock.FefoDiscountStartAtUtc is not DateTime startAt)
            return false;
        if (stock.FefoDiscountEndAtUtc is not DateTime endAt)
            return false;
        return utcNow >= startAt && utcNow < endAt;
    }

    public static string ResolveStatus(BranchStock stock, DateTime utcNow)
    {
        if (!stock.IsFefoDiscountActive || stock.FefoDiscountPercent <= 0m)
            return "None";
        if (stock.FefoDiscountStartAtUtc is not DateTime startAt || stock.FefoDiscountEndAtUtc is not DateTime endAt)
            return "None";
        if (utcNow < startAt) return "Scheduled";
        if (utcNow < endAt) return "Active";
        return "Completed";
    }
}
