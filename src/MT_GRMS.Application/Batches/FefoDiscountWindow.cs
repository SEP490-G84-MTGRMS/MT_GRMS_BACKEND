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
        if (stock.FefoDiscountStartAtUtc is null && stock.FefoDiscountEndAtUtc is null)
            return true;
        if (stock.FefoDiscountStartAtUtc is DateTime startAt && utcNow < startAt)
            return false;
        if (stock.FefoDiscountEndAtUtc is DateTime endAt && utcNow >= endAt)
            return false;
        return true;
    }

    public static string ResolveStatus(BranchStock stock, DateTime utcNow)
    {
        if (!stock.IsFefoDiscountActive || stock.FefoDiscountPercent <= 0m)
            return "None";
        if (stock.FefoDiscountStartAtUtc is null && stock.FefoDiscountEndAtUtc is null)
            return "Active";
        if (stock.FefoDiscountStartAtUtc is DateTime startAt && utcNow < startAt)
            return "Scheduled";
        if (stock.FefoDiscountEndAtUtc is DateTime endAt && utcNow >= endAt)
            return "Completed";
        return "Active";
    }
}
