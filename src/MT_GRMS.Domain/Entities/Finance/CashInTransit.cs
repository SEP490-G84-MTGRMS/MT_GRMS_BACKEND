using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Domain.Entities.Finance;

/// <summary>
/// Intermediate transit record for 2-phase bank deposits and transfers.
/// </summary>
public class CashInTransit : BaseTenantEntity
{
    public string TransitCode { get; set; } = string.Empty;
    public Guid SourceAccountId { get; set; }
    public FinanceAccountType SourceAccountType { get; set; }
    public Guid TargetBankAccountId { get; set; }
    public decimal AmountSent { get; set; }
    public decimal? AmountReceived { get; set; }
    public decimal DiscrepancyAmount { get; set; } = 0;
    public Guid CarrierUserId { get; set; } // Người mang tiền đi nộp
    public CashInTransitStatus Status { get; set; } = CashInTransitStatus.PendingBankConfirmation;
    public string? Note { get; set; }
    public DateTime? ConfirmedAtUtc { get; set; }
    public Guid? ConfirmedByUserId { get; set; }
}