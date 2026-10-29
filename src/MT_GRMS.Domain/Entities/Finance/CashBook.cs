using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Entities.SaaS;

namespace MT_GRMS.Domain.Entities.Finance;

/// <summary>
/// Bảng CashBooks (Sổ quỹ tiền mặt / Ngân hàng của Chi nhánh)
/// </summary>
public class CashBook : BaseEntity
{
    public Guid BranchId { get; set; }
    public string TransactionCode { get; set; } = string.Empty;
    public string SourceDocType { get; set; } = string.Empty;
    public string? SourceDocId { get; set; }
    public string TransactionType { get; set; } = string.Empty;
    public decimal Amount { get; set; } = 0.00m;
    public decimal OpeningBalance { get; set; } = 0.00m;
    public decimal CurrentBalance { get; set; } = 0.00m;
    public string? Description { get; set; }

    // Navigation
    public Branch? Branch { get; set; }
}
