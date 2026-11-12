using System;
using System.Collections.Generic;

namespace MT_GRMS.Application.PurchaseOrders.Dtos;

public class OrderMatrixQuery
{
    public Guid BranchId { get; set; }
    public Guid? SupplierId { get; set; }
    public DateTime ViewFrom { get; set; }
    public int DurationDays { get; set; } = 7;
}

public class OrderMatrixProductDto
{
    public Guid Id { get; set; }
    public Guid SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public string Barcode { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Moq { get; set; }
    /// <summary>Bội số đặt hàng; mặc định 1 nếu NCC chưa cấu hình.</summary>
    public int OrderMultiple { get; set; } = 1;
    public decimal Soh { get; set; }
    public decimal SalesPerDay { get; set; }
    public decimal SafetyStock { get; set; }

    // Trạng thái nghiệp vụ Active / Inactive
    public bool IsProductActive { get; set; } = true;
    public string? ProductInactiveReason { get; set; }

    public bool IsSupplierActive { get; set; } = true;
    public string? SupplierInactiveReason { get; set; }

    public bool IsSupplierProductActive { get; set; } = true;
    public string? SupplierProductInactiveReason { get; set; }

    public bool IsSupplierBranchActive { get; set; } = true;
    public string? SupplierBranchInactiveReason { get; set; }

    public bool IsBranchProductActive { get; set; } = true;
    public string? BranchProductInactiveReason { get; set; }

    public bool IsRowDisabled => !IsProductActive || !IsSupplierActive || !IsSupplierProductActive || !IsSupplierBranchActive || !IsBranchProductActive;
    public string? RowDisabledReason { get; set; }
}

public class OrderMatrixSupplierDeliveryConfigDto
{
    public Guid SupplierId { get; set; }
    public string SupplierCode { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public bool IsAssignedToBranch { get; set; } = true;
    public string? InactiveReason { get; set; }

    public string CutoffTime { get; set; } = "15:00";
    /// <summary>Số ngày trước ngày giao để chốt đơn (0 = cùng ngày giao). Mặc định 1.</summary>
    public int CutoffDayOffset { get; set; } = 1;
    public List<int> ActiveDeliveryDays { get; set; } = new() { 1, 2, 3, 4, 5, 6 };
    public List<string> BlackoutDates { get; set; } = new();
}

public class OrderMatrixCellDto
{
    public Guid ProductId { get; set; }
    /// <summary>NCC của dòng nguồn nhập — bắt buộc khi Matrix ALL có nhiều NCC cùng SP.</summary>
    public Guid SupplierId { get; set; }
    public DateTime DeliveryDate { get; set; }
    public decimal EmployeeQty { get; set; }
    public decimal? StoreQty { get; set; }
    public bool IsOwnerAdjusted { get; set; }
    public decimal FinalQty { get; set; }
}

public class OrderMatrixDayStatusDto
{
    public Guid SupplierId { get; set; }
    public DateTime DeliveryDate { get; set; }
    /// <summary>OPEN | CUTOFF_DUE | LOCKED | FINALIZED | SUPPLIER_OFF</summary>
    public string DeliveryDateStatus { get; set; } = "OPEN";
    public DateTime CutoffDateTime { get; set; }
    public string? PrimaryReasonCode { get; set; }
    public string? PoCode { get; set; }
    public Guid? PoId { get; set; }
}

public class OrderMatrixResponse
{
    public Guid BranchId { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public bool BranchIsActive { get; set; } = true;
    public Guid? SupplierId { get; set; }
    public DateTime ViewFrom { get; set; }
    public int DurationDays { get; set; }
    public DateTime ServerNow { get; set; }
    public IReadOnlyList<OrderMatrixProductDto> Products { get; set; } = new List<OrderMatrixProductDto>();
    public IReadOnlyList<OrderMatrixCellDto> Cells { get; set; } = new List<OrderMatrixCellDto>();
    public IReadOnlyList<OrderMatrixSupplierDeliveryConfigDto> SupplierConfigs { get; set; } = new List<OrderMatrixSupplierDeliveryConfigDto>();
    public IReadOnlyList<OrderMatrixDayStatusDto> DayStatuses { get; set; } = new List<OrderMatrixDayStatusDto>();
    public Guid? DraftPoId { get; set; }
    public string Status { get; set; } = "DRAFT";
}

public class SaveMatrixDraftCommand
{
    public Guid BranchId { get; set; }
    public Guid? SupplierId { get; set; }
    public DateTime ViewFrom { get; set; }
    public DateTime ViewTo { get; set; }
    public string? Note { get; set; }
    public List<OrderMatrixCellDto> Cells { get; set; } = new();
}

public class SplitMatrixPoItemCommand
{
    public Guid ProductId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public string Unit { get; set; } = string.Empty;
}

public class SplitMatrixPoGroupCommand
{
    public Guid SupplierId { get; set; }
    public DateTime DeliveryDate { get; set; }
    public List<SplitMatrixPoItemCommand> Items { get; set; } = new();
}

public class SplitMatrixPoCommand
{
    public Guid BranchId { get; set; }
    /// <summary>Khi có giá trị: chỉ chốt NCC này. Nếu BranchId = Guid.Empty → chốt trên mọi cửa hàng đang liên kết NCC.</summary>
    public Guid? SupplierId { get; set; }
    public Guid? ParentDraftId { get; set; }
    public string? Note { get; set; }

    /// <summary>NEAREST | WEEK | DAYS_7 | DAYS_14 | DAYS_21 | DAYS_31 | ALL</summary>
    public string? Scope { get; set; }

    /// <summary>Ngày bắt đầu tuần nếu Scope = WEEK (định dạng yyyy-MM-dd); tự động chuẩn hóa về Thứ Hai</summary>
    public string? WeekStartDate { get; set; }

    /// <summary>Số ngày rolling khi Scope = ALL (hoặc fallback). DAYS_* tự resolve theo Scope.</summary>
    public int DurationDays { get; set; }

    /// <summary>Danh sách groups chỉ định thủ công (hỗ trợ backward compatibility)</summary>
    public List<SplitMatrixPoGroupCommand> Groups { get; set; } = new();

    /// <summary>SEND_NOW | SCHEDULED_SEND — null = không tạo package/email (preview hoặc legacy).</summary>
    public string? EmailSendMode { get; set; }

    /// <summary>Thời điểm gửi (ISO / local VN). Bắt buộc khi SCHEDULED_SEND.</summary>
    public DateTime? ScheduledSendAt { get; set; }

    /// <summary>CC emails do Store Owner nhập.</summary>
    public List<string> CcEmails { get; set; } = new();
}

public class SplitGroupResultDto
{
    public Guid BranchId { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public Guid SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public string SupplierCode { get; set; } = string.Empty;
    public DateTime DeliveryDate { get; set; }
    /// <summary>SUCCESS | AMENDED | NO_CHANGES | FAILED | SKIPPED | ALREADY_FINALIZED</summary>
    public string Status { get; set; } = "SUCCESS";
    public Guid? PurchaseOrderId { get; set; }
    public string? PurchaseOrderCode { get; set; }
    public int? RevisionNumber { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public int ValidLineCount { get; set; }
    public int InvalidLineCount { get; set; }
    public decimal TotalQuantity { get; set; }
    public decimal TotalAmount { get; set; }
    public bool HasMissingPrice { get; set; }
    public List<SplitGroupLineDto> Lines { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public class SplitGroupLineDto
{
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineAmount { get; set; }
    public bool HasMissingPrice { get; set; }
}

public class SplitMatrixBatchResultDto
{
    public Guid BranchId { get; set; }
    public string? Scope { get; set; }
    public DateTime ServerNow { get; set; }
    public int TotalGroups { get; set; }
    public int SuccessCount { get; set; }
    public int AmendedCount { get; set; }
    public int NoChangesCount { get; set; }
    public int FailedCount { get; set; }
    public int SkippedCount { get; set; }
    public int AlreadyFinalizedCount { get; set; }
    public IReadOnlyList<SplitGroupResultDto> Results { get; set; } = new List<SplitGroupResultDto>();
    public IReadOnlyList<PurchaseOrderDto> CreatedPurchaseOrders { get; set; } = new List<PurchaseOrderDto>();
    public SupplierDispatchPackageDto? DispatchPackage { get; set; }
}

public class SupplierDispatchPackageDto
{
    public Guid Id { get; set; }
    public Guid DispatchBatchId { get; set; }
    public Guid SupplierId { get; set; }
    public string SupplierCode { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public string EmailTo { get; set; } = string.Empty;
    public IReadOnlyList<string> EmailCc { get; set; } = Array.Empty<string>();
    public string EmailSendMode { get; set; } = string.Empty;
    public DateTime? ScheduledSendAtUtc { get; set; }
    public string EmailStatus { get; set; } = string.Empty;
    public string GenerationStatus { get; set; } = string.Empty;
    public int PackageRevisionNumber { get; set; } = 1;
    public string? FileName { get; set; }
    public long? FileSize { get; set; }
    public int AttemptCount { get; set; }
    public string? LastError { get; set; }
    public DateTime? GeneratedAtUtc { get; set; }
    public DateTime? SentAtUtc { get; set; }
    public DateTime CreatedAt { get; set; }
    public int PurchaseOrderCount { get; set; }
    public IReadOnlyList<string> PurchaseOrderCodes { get; set; } = Array.Empty<string>();
    public bool CanDownload { get; set; }
    public bool CanCancelSchedule { get; set; }
    public bool CanRetryGeneration { get; set; }
    public bool CanRetryEmail { get; set; }
}

public class PurchaseOrderDetailDto
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal? EmployeeQty { get; set; }
    public decimal? StoreQty { get; set; }
    public bool IsOwnerAdjusted { get; set; }
    public decimal? FinalQty { get; set; }
    public decimal OrderedQty { get; set; }
    public decimal ReceivedQty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineAmount { get; set; }
    public DateTime? DeliveryDate { get; set; }
}

public class PurchaseOrderDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public Guid BranchId { get; set; }
    public string BranchCode { get; set; } = string.Empty;
    public string BranchName { get; set; } = string.Empty;
    public Guid SupplierId { get; set; }
    public string SupplierCode { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public DateTime? DeliveryDate { get; set; }
    public string Status { get; set; } = "DRAFT";
    public int RevisionNumber { get; set; } = 1;
    public DateTime? LastAmendedAtUtc { get; set; }
    public decimal TotalQty { get; set; }
    public decimal TotalAmount { get; set; }
    public string? Note { get; set; }
    public string? CreatedByRole { get; set; }
    public Guid? ParentDraftId { get; set; }
    public DateTime CreatedAt { get; set; }
    /// <summary>Phiếu nhập kho DRAFT/COMPLETED đang gắn PO (nếu có).</summary>
    public Guid? ActiveGoodsReceiptId { get; set; }
    public string? ActiveGoodsReceiptCode { get; set; }
    public string? ActiveGoodsReceiptStatus { get; set; }
    public IReadOnlyList<PurchaseOrderDetailDto> Details { get; set; } = new List<PurchaseOrderDetailDto>();
}

public class UpdatePurchaseOrderStatusRequest
{
    public string Status { get; set; } = string.Empty;
    public string? Reason { get; set; }
}

public class PurchaseOrderFilterQuery
{
    public Guid? BranchId { get; set; }
    public Guid? SupplierId { get; set; }
    public string? Status { get; set; }
    public string? Keyword { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

/// <summary>Hub chốt đơn NCC — gom từ PurchaseOrders(DRAFT) + PurchaseOrderDetails + DayStatuses.</summary>
public class DispatchHubQuery
{
    public Guid BranchId { get; set; }
    public DateTime? ViewFrom { get; set; }
    public int DurationDays { get; set; } = 14;
    public string? Keyword { get; set; }
    /// <summary>all | critical | today | later | completed</summary>
    public string? Urgency { get; set; }
    /// <summary>NEAREST | WEEK | ALL (mặc định nếu null là ALL / ViewFrom + DurationDays)</summary>
    public string? Scope { get; set; }
    /// <summary>Ngày bắt đầu tuần khi Scope = WEEK (định dạng yyyy-MM-dd)</summary>
    public string? WeekStartDate { get; set; }
}

public class DispatchHubItemDto
{
    public string Id { get; set; } = string.Empty;
    public Guid ProductId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal Price { get; set; }
    /// <summary>SL trên ma trận (draft).</summary>
    public decimal Qty { get; set; }
    /// <summary>SL đã chốt trên PO (0 nếu chưa có PO).</summary>
    public decimal CommittedQty { get; set; }
    public DateTime DeliveryDate { get; set; }
    public string CutoffSchedule { get; set; } = string.Empty;
}

/// <summary>1 dòng draft SL khác PO đã chốt (product × ngày giao × cửa hàng).</summary>
public class DispatchPendingChangeLineDto
{
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public DateTime DeliveryDate { get; set; }
    public decimal DraftQty { get; set; }
    public decimal CommittedQty { get; set; }
    public string? PoCode { get; set; }
    public Guid? BranchId { get; set; }
    public string? BranchCode { get; set; }
    public string? BranchName { get; set; }
    /// <summary>EDITABLE = còn amend được; LOCKED_CUTOFF = đã qua cutoff, chỉ xem.</summary>
    public string UpdateStatus { get; set; } = "EDITABLE";
}

public class DispatchHubSupplierDto
{
    public Guid SupplierId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<string> CategoryTags { get; set; } = new();
    public string CutoffTimeStr { get; set; } = string.Empty;
    public string CountdownDisplay { get; set; } = string.Empty;
    public int? RemainingMinutes { get; set; }
    /// <summary>critical | today | later</summary>
    public string Urgency { get; set; } = "later";
    public string ExpectedDelivery { get; set; } = string.Empty;
    public decimal MinOrderValue { get; set; }
    public List<DispatchHubItemDto> Items { get; set; } = new();
    public string Note { get; set; } = string.Empty;
    /// <summary>draft | dispatched — dispatched chỉ khi đã FINALIZED (qua cutoff).</summary>
    public string Status { get; set; } = "draft";
    public string? PoCode { get; set; }
    /// <summary>True khi đã có PO nhưng còn trước cutoff — cho phép chốt lại (amend).</summary>
    public bool CanAmend { get; set; }
    /// <summary>True khi draft SL khác PO đã chốt — cần cập nhật PO và gửi lại email NCC.</summary>
    public bool HasPendingChanges { get; set; }
    /// <summary>Số dòng (product×ngày) draft khác PO.</summary>
    public int PendingChangeLineCount { get; set; }
    /// <summary>Chi tiết từng dòng SL khác PO (để popup / filter).</summary>
    public List<DispatchPendingChangeLineDto> PendingChanges { get; set; } = new();
    public string? DispatchedAt { get; set; }
    public Guid? DraftPoId { get; set; }
}

public class DispatchHubResponse
{
    public Guid BranchId { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public DateTime ServerNow { get; set; }
    public DateTime ViewFrom { get; set; }
    public int DurationDays { get; set; }
    public Guid? DraftPoId { get; set; }
    public string? Scope { get; set; }
    public DateTime? WeekStartDate { get; set; }
    public DateTime? WeekEndDate { get; set; }
    public int ExpectedPoCount { get; set; }
    public decimal TotalDraftAmount { get; set; }
    public int TotalDraftLines { get; set; }
    public IReadOnlyList<DispatchHubSupplierDto> Suppliers { get; set; } = new List<DispatchHubSupplierDto>();
    public DispatchHubCountsDto Counts { get; set; } = new();
}

public class DispatchHubCountsDto
{
    public int All { get; set; }
    public int Critical { get; set; }
    public int Today { get; set; }
    public int Later { get; set; }
    public int Completed { get; set; }
}

/// <summary>Hub theo NCC (multi-branch): danh sách toàn bộ NCC có liên kết cửa hàng.</summary>
public class DispatchSupplierHubQuery
{
    public string? Keyword { get; set; }
    /// <summary>all | critical | today | later | completed</summary>
    public string? Urgency { get; set; }
    public int DurationDays { get; set; } = 14;
}

public class DispatchSupplierBranchSummaryDto
{
    public Guid BranchId { get; set; }
    public string BranchCode { get; set; } = string.Empty;
    public string BranchName { get; set; } = string.Empty;
    public int SkuCount { get; set; }
    public decimal TotalQty { get; set; }
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = "draft"; // draft | dispatched | mixed
    public bool HasPendingChanges { get; set; }
}

public class DispatchSupplierHubItemDto
{
    public Guid SupplierId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string CutoffTimeStr { get; set; } = string.Empty;
    public string CountdownDisplay { get; set; } = string.Empty;
    public int? RemainingMinutes { get; set; }
    public string Urgency { get; set; } = "later";
    public string ExpectedDelivery { get; set; } = string.Empty;
    public string Status { get; set; } = "draft";
    public string? PoCode { get; set; }
    public bool CanAmend { get; set; }
    public bool HasPendingChanges { get; set; }
    public int PendingChangeLineCount { get; set; }
    public List<DispatchPendingChangeLineDto> PendingChanges { get; set; } = new();
    public int LinkedBranchCount { get; set; }
    public int BranchesWithQty { get; set; }
    public int SkuCount { get; set; }
    public decimal TotalQty { get; set; }
    public decimal TotalAmount { get; set; }
    public List<DispatchSupplierBranchSummaryDto> Branches { get; set; } = new();
}

public class DispatchSupplierHubResponse
{
    public DateTime ServerNow { get; set; }
    public DateTime ViewFrom { get; set; }
    public int DurationDays { get; set; }
    public IReadOnlyList<DispatchSupplierHubItemDto> Suppliers { get; set; } = new List<DispatchSupplierHubItemDto>();
    public DispatchHubCountsDto Counts { get; set; } = new();
}

public class DispatchSupplierDetailQuery
{
    public Guid SupplierId { get; set; }
    public int DurationDays { get; set; } = 14;
}

public class DispatchSupplierDetailLineDto
{
    public string Id { get; set; } = string.Empty;
    public Guid ProductId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal Price { get; set; }
    /// <summary>SL trên ma trận (draft).</summary>
    public decimal Qty { get; set; }
    /// <summary>SL đã chốt trên PO.</summary>
    public decimal CommittedQty { get; set; }
    public DateTime DeliveryDate { get; set; }
    public string CutoffSchedule { get; set; } = string.Empty;
    public string DayStatus { get; set; } = "OPEN";
}

public class DispatchSupplierDetailBranchDto
{
    public Guid BranchId { get; set; }
    public string BranchCode { get; set; } = string.Empty;
    public string BranchName { get; set; } = string.Empty;
    public string Status { get; set; } = "draft";
    public List<DispatchSupplierDetailLineDto> Items { get; set; } = new();
}

public class DispatchSupplierDetailResponse
{
    public Guid SupplierId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
    public DateTime ServerNow { get; set; }
    public DateTime ViewFrom { get; set; }
    public int DurationDays { get; set; }
    public string CutoffTimeStr { get; set; } = string.Empty;
    public string CountdownDisplay { get; set; } = string.Empty;
    public int? RemainingMinutes { get; set; }
    public string Urgency { get; set; } = "later";
    public string Status { get; set; } = "draft";
    public IReadOnlyList<DispatchSupplierDetailBranchDto> Branches { get; set; } = new List<DispatchSupplierDetailBranchDto>();
}

// ── Matrix Excel Import / Preview ──────────────────────────────────────────

public class MatrixExcelAllowedProductDto
{
    public Guid ProductId { get; set; }
    public Guid SupplierId { get; set; }
}

public class MatrixExcelPreviewQuery
{
    public Guid BranchId { get; set; }
    public Guid? SupplierId { get; set; }
    public DateTime ViewFrom { get; set; }
    public int DurationDays { get; set; } = 7;
    public List<MatrixExcelAllowedProductDto> AllowedProducts { get; set; } = new();
    public string? FileName { get; set; }
    public long FileSize { get; set; }
}

public class MatrixExcelIssueDto
{
    public string Type { get; set; } = "ERROR"; // ERROR | WARNING
    public int Row { get; set; }
    public string ProductCode { get; set; } = "-";
    public string ProductName { get; set; } = "-";
    public string DeliveryDate { get; set; } = "-";
    public string DateLabel { get; set; } = "-";
    public string InputValue { get; set; } = "-";
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public class MatrixExcelCellApplyDto
{
    public int ExcelRow { get; set; }
    public Guid ProductId { get; set; }
    public Guid SupplierId { get; set; }
    public DateTime DeliveryDate { get; set; }
    public string DateLabel { get; set; } = string.Empty;
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public decimal CurrentQty { get; set; }
    public decimal Quantity { get; set; }
}

public class MatrixExcelScopeStatusDto
{
    public bool BranchMatched { get; set; }
    public string BranchExpected { get; set; } = string.Empty;
    public string? BranchActual { get; set; }
    public bool SupplierMatched { get; set; }
    public string SupplierExpected { get; set; } = string.Empty;
    public string? SupplierActual { get; set; }
    public bool PeriodMatched { get; set; }
    public string PeriodExpected { get; set; } = string.Empty;
    public int ProductsInFilterCount { get; set; }
}

public class MatrixExcelValidationSummaryDto
{
    public int TotalCells { get; set; }
    public int ValidCells { get; set; }
    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }
    public int IgnoredCells { get; set; }
    public int SkippedOwnerLocked { get; set; }
    public int OutOfFilterCount { get; set; }
}

public class MatrixExcelValidationResultDto
{
    public bool Valid { get; set; }
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public MatrixExcelScopeStatusDto ScopeStatus { get; set; } = new();
    public MatrixExcelValidationSummaryDto Summary { get; set; } = new();
    public List<MatrixExcelIssueDto> Errors { get; set; } = new();
    public List<MatrixExcelIssueDto> Warnings { get; set; } = new();
    public List<MatrixExcelCellApplyDto> CellsToApply { get; set; } = new();
}

public class ImportMatrixExcelCellDto
{
    public Guid ProductId { get; set; }
    public Guid SupplierId { get; set; }
    public DateTime DeliveryDate { get; set; }
    public decimal Quantity { get; set; }
    public int? ExcelRow { get; set; }
    public string? ProductCode { get; set; }
    public string? ProductName { get; set; }
    public string? DateLabel { get; set; }
}

public class ImportMatrixExcelCommand
{
    public Guid BranchId { get; set; }
    public Guid? SupplierId { get; set; }
    public DateTime ViewFrom { get; set; }
    public DateTime ViewTo { get; set; }
    public string? FileName { get; set; }
    public List<MatrixExcelAllowedProductDto> AllowedProducts { get; set; } = new();
    public List<ImportMatrixExcelCellDto> Cells { get; set; } = new();
}

public class MatrixExcelSuccessDayDto
{
    public DateTime DeliveryDate { get; set; }
    public string DateLabel { get; set; } = string.Empty;
    public int AppliedCells { get; set; }
    public int ProductCount { get; set; }
}

public class MatrixExcelImportSummaryDto
{
    public int AppliedCells { get; set; }
    public int SkippedOwnerLocked { get; set; }
    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }
    public int SuccessDayCount { get; set; }
}

public class MatrixExcelImportResultDto
{
    public bool Success { get; set; }
    public string FileName { get; set; } = string.Empty;
    public MatrixExcelImportSummaryDto Summary { get; set; } = new();
    public List<MatrixExcelSuccessDayDto> SuccessDays { get; set; } = new();
    public List<MatrixExcelIssueDto> Errors { get; set; } = new();
    public List<MatrixExcelIssueDto> Warnings { get; set; } = new();
    public Guid? DraftPoId { get; set; }
}
