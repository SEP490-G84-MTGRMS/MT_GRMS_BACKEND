using MT_GRMS.Domain.Common;

namespace MT_GRMS.Domain.Entities.Supplier;

/// <summary>
/// Gói chốt đơn + Excel + email cho 1 NCC trong 1 lần Dispatch.
/// </summary>
public class SupplierDispatchPackage : BaseTenantEntity
{
    public Guid DispatchBatchId { get; set; }
    public Guid SupplierId { get; set; }

    public string SupplierCodeSnapshot { get; set; } = string.Empty;
    public string SupplierNameSnapshot { get; set; } = string.Empty;

    /// <summary>To email snapshot tại thời điểm chốt.</summary>
    public string EmailTo { get; set; } = string.Empty;

    /// <summary>JSON array string[] CC emails (đã normalize).</summary>
    public string EmailCcJson { get; set; } = "[]";

    /// <summary>SEND_NOW | SCHEDULED_SEND</summary>
    public string EmailSendMode { get; set; } = "SEND_NOW";

    public DateTime? ScheduledSendAtUtc { get; set; }

    /// <summary>PENDING | SCHEDULED | SENDING | SENT | FAILED | CANCELLED</summary>
    public string EmailStatus { get; set; } = "PENDING";

    /// <summary>PENDING | GENERATING | GENERATED | FAILED</summary>
    public string GenerationStatus { get; set; } = "PENDING";

    public string? FileName { get; set; }
    public string? FileStorageKey { get; set; }
    public long? FileSize { get; set; }
    public string? FileChecksum { get; set; }

    public int AttemptCount { get; set; }
    public string? LastError { get; set; }

    public DateTime? GeneratedAtUtc { get; set; }
    public DateTime? SentAtUtc { get; set; }

    public string? Note { get; set; }
    public string? IdempotencyKey { get; set; }

    /// <summary>Lease chống worker trùng (UTC).</summary>
    public DateTime? ProcessingLeaseUntilUtc { get; set; }
    public string? ProcessingLockedBy { get; set; }

    public Supplier? Supplier { get; set; }
    public ICollection<SupplierDispatchPackageItem> Items { get; set; } = new List<SupplierDispatchPackageItem>();
}

public class SupplierDispatchPackageItem : BaseTenantEntity
{
    public Guid PackageId { get; set; }
    public Guid PurchaseOrderId { get; set; }

    public SupplierDispatchPackage? Package { get; set; }
    public PurchaseOrder? PurchaseOrder { get; set; }
}
