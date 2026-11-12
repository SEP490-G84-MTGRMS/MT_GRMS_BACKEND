using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Domain.Entities.Finance;

public class BusinessDayClosing : BaseTenantEntity
{
    public Guid BranchId { get; set; }
    public DateOnly BusinessDate { get; set; }
    public TimeSpan BusinessDayStartTime { get; set; }
    public BusinessDayClosingStatus Status { get; set; } = BusinessDayClosingStatus.Closing;
    public int TotalShifts { get; set; }
    public int TotalOrders { get; set; }
    public decimal TotalCashSales { get; set; }
    public decimal TotalBankSales { get; set; }
    public decimal OwnerShiftlessCashSales { get; set; }
    public decimal OwnerShiftlessBankSales { get; set; }
    public decimal TotalCashIn { get; set; }
    public decimal TotalCashOut { get; set; }
    public decimal TotalRetainedFloat { get; set; }
    public decimal TotalDroppedToSafe { get; set; }
    public decimal TotalDiscrepancy { get; set; }
    public Guid ClosedByUserId { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
    public string? Note { get; set; }
    public string ReferenceCode { get; set; } = string.Empty;
}
