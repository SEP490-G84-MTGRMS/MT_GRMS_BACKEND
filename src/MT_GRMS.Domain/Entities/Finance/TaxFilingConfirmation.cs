namespace MT_GRMS.Domain.Entities.Finance;

public sealed class TaxFilingConfirmation
{
    public Guid SnapshotId { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string ConfirmedBy { get; set; } = string.Empty;
    public DateTime ConfirmedAtUtc { get; set; }
}
