using MT_GRMS.Domain.Common;

namespace MT_GRMS.Domain.Entities.Supplier;

/// <summary>
/// Chính sách trả/đổi hàng theo từng nhà cung cấp (1 active / supplier).
/// </summary>
public class SupplierReturnPolicy : BaseTenantEntity
{
    public Guid SupplierId { get; set; }

    public bool AllowReturn { get; set; } = true;

    /// <summary>JSON array: QUALITY_DEFECT, NEAR_EXPIRY, WRONG_ITEM, ...</summary>
    public string AllowedReturnTypesJson { get; set; } = "[\"QUALITY_DEFECT\",\"NEAR_EXPIRY\",\"WRONG_ITEM\"]";

    /// <summary>JSON array: REDUCE_PAYABLE, EXCHANGE, REFUND, ...</summary>
    public string AllowedResolutionTypesJson { get; set; } = "[\"REDUCE_PAYABLE\",\"EXCHANGE\",\"REFUND\"]";

    public string DefaultResolutionType { get; set; } = "REDUCE_PAYABLE";

    public int? NearExpiryMinDaysLeft { get; set; }
    public bool RequireEvidenceImage { get; set; } = true;
    public int? MaxReturnDaysAfterReceipt { get; set; }

    public bool CostShareEnabled { get; set; } = false;
    public decimal? StoreCostSharePercent { get; set; }
    public decimal? SupplierCostSharePercent { get; set; }
    public string CostShareBase { get; set; } = "REPLACEMENT_VALUE";
    public bool RequireStorePaymentBeforeReplacement { get; set; } = true;

    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;

    public Supplier? Supplier { get; set; }
}
