using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Application.Suppliers.Dtos;

public class SupplierSummaryDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public SupplierType SupplierType { get; set; }
    public string SupplierTypeName => SupplierType switch
    {
        SupplierType.Goods => "Hàng hóa",
        SupplierType.Service => "Dịch vụ",
        SupplierType.Both => "Cả hai",
        _ => "Khác"
    };
    public string Phone { get; set; } = string.Empty;
    public string ContactPerson { get; set; } = string.Empty;
    public string? TaxCode { get; set; }
    public string? Email { get; set; }
    public Guid? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public SupplierStatus Status { get; set; }
    public string StatusName => Status switch
    {
        SupplierStatus.Active => "Đang hoạt động",
        SupplierStatus.Inactive => "Ngừng giao dịch",
        _ => "Không xác định"
    };
    public int PaymentTermDays { get; set; }
    public decimal CreditLimit { get; set; }
    public decimal CurrentDebt { get; set; }
    public int OpenPurchaseOrderCount { get; set; }
    public Guid? LinkedCustomerId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class SupplierDetailDto : SupplierSummaryDto
{
    public string? Address { get; set; }
    public string? Ward { get; set; }
    public string? District { get; set; }
    public string? Province { get; set; }
    public string? ContactPhone { get; set; }
    public string? BankName { get; set; }
    public string? BankAccountNumber { get; set; }
    public string? BankAccountHolder { get; set; }
    public string? Notes { get; set; }

    public SupplierDeliveryConfigDto? DeliveryConfig { get; set; }
    public List<SupplierBlackoutDateDto> BlackoutDates { get; set; } = [];
}

public class CreateSupplierCommand
{
    public string? Code { get; set; }
    public string Name { get; set; } = string.Empty;
    public SupplierType SupplierType { get; set; } = SupplierType.Goods;
    public Guid? CategoryId { get; set; }
    public string? TaxCode { get; set; }
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? Ward { get; set; }
    public string? District { get; set; }
    public string? Province { get; set; }
    public string? ContactPerson { get; set; }
    public string? ContactPhone { get; set; }
    public int PaymentTermDays { get; set; } = 30;
    public decimal CreditLimit { get; set; } = 0;
    public string? BankName { get; set; }
    public string? BankAccountNumber { get; set; }
    public string? BankAccountHolder { get; set; }
    public string? Notes { get; set; }
    public Guid? LinkedCustomerId { get; set; }
}

public class UpdateSupplierCommand
{
    public string Name { get; set; } = string.Empty;
    public SupplierType SupplierType { get; set; }
    public Guid? CategoryId { get; set; }
    public string? TaxCode { get; set; }
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? Ward { get; set; }
    public string? District { get; set; }
    public string? Province { get; set; }
    public string? ContactPerson { get; set; }
    public string? ContactPhone { get; set; }
    public int PaymentTermDays { get; set; }
    public decimal CreditLimit { get; set; }
    public string? BankName { get; set; }
    public string? BankAccountNumber { get; set; }
    public string? BankAccountHolder { get; set; }
    public string? Notes { get; set; }
    public Guid? LinkedCustomerId { get; set; }
}

public class UpdateSupplierStatusRequest
{
    public SupplierStatus Status { get; set; }
    public string? Reason { get; set; }
}

public class LinkCustomerRequest
{
    public Guid CustomerId { get; set; }
}

public class SupplierApSummaryDto
{
    public Guid SupplierId { get; set; }
    public decimal CurrentDebt { get; set; }
    public decimal OverdueDebt { get; set; }
    public decimal CreditLimit { get; set; }
    public int OpenPurchaseOrderCount { get; set; }
}

public class ReplaceSupplierBranchesCommand
{
    public List<Guid> BranchIds { get; set; } = [];
}

public class SupplierBranchAssignmentDto
{
    public Guid BranchId { get; set; }
}

public class SupplierAPLedgerDto
{
    public Guid Id { get; set; }
    public Guid SupplierId { get; set; }
    public DateTime TransactionDate { get; set; }
    public APDocType DocType { get; set; }
    public string DocTypeName => DocType switch
    {
        APDocType.PurchaseInvoice => "Hóa đơn mua hàng",
        APDocType.ServiceInvoice => "Hóa đơn dịch vụ",
        APDocType.PaymentVoucher => "Phiếu chi",
        APDocType.ReturnAdjustment => "Điều chỉnh công nợ",
        _ => "Khác"
    };
    public string DocNo { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public int Direction { get; set; }
    public decimal BalanceAfter { get; set; }
    public string CreatedByName { get; set; } = string.Empty;
    public string? IdempotencyKey { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class RecordAPTransactionCommand
{
    public Guid SupplierId { get; set; }
    public DateTime? TransactionDate { get; set; }
    public APDocType DocType { get; set; } = APDocType.PurchaseInvoice;
    public string DocNo { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public int Direction { get; set; } = 1; // +1 increase, -1 decrease
    public string CreatedByName { get; set; } = "Hệ thống";
    public string? IdempotencyKey { get; set; }
}

public class SupplierDeliveryConfigDto
{
    public Guid Id { get; set; }
    public Guid SupplierId { get; set; }
    public int LeadTimeHours { get; set; }
    public int LeadTimeDays { get; set; } = 1;
    public int CutoffDayOffset { get; set; } = 1;
    public string CutoffTime { get; set; } = "17:00";
    public string OrderCutoffTime { get; set; } = "17:00";
    public int DeliveryWeekdaysMask { get; set; }
    public List<string> DeliveryDays { get; set; } = [];
    public decimal MinOrderValue { get; set; }
    public int MinOrderQuantity { get; set; }
    public bool AllowUrgent { get; set; }
    public int UrgentLeadTimeHours { get; set; }
    public decimal UrgentFeePercent { get; set; }
    public DateTime? EarliestDeliveryDate { get; set; }
    public List<SupplierBlackoutDateDto> BlackoutDates { get; set; } = [];
}

public class UpdateSupplierDeliveryConfigCommand
{
    public int LeadTimeHours { get; set; } = 24;
    public int LeadTimeDays { get; set; } = 1;
    public int CutoffDayOffset { get; set; } = 1;
    public string CutoffTime { get; set; } = "17:00";
    public string OrderCutoffTime { get; set; } = "17:00";
    public List<string> DeliveryDays { get; set; } = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];
    public decimal MinOrderValue { get; set; } = 0;
    public int MinOrderQuantity { get; set; } = 0;
    public bool AllowUrgent { get; set; } = false;
    public int UrgentLeadTimeHours { get; set; } = 0;
    public decimal UrgentFeePercent { get; set; } = 0;
    public List<SupplierBlackoutDateDto>? BlackoutDates { get; set; }
}

public class SupplierBlackoutDateDto
{
    public Guid? Id { get; set; }
    public Guid SupplierId { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string Reason { get; set; } = string.Empty;
    public bool IsRecurringAnnual { get; set; }
}

public class SupplierQueryFilter
{
    public string? SearchTerm { get; set; }
    public string? Code { get; set; }
    public string? Name { get; set; }
    public string? ContactPerson { get; set; }
    public string? Phone { get; set; }
    public SupplierType? SupplierType { get; set; }
    public SupplierStatus? Status { get; set; }
    public Guid? CategoryId { get; set; }
    public List<Guid>? SupplierIds { get; set; }
    public List<Guid>? ProductIds { get; set; }
    public List<string>? ContactPersons { get; set; }
    public List<string>? Phones { get; set; }
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class SupplierSearchSuggestionsDto
{
    public List<SupplierSuggestionItemDto> Suppliers { get; set; } = new();
    public List<ProductSuggestionItemDto> Products { get; set; } = new();
    public List<ContactPersonSuggestionItemDto> ContactPersons { get; set; } = new();
    public List<PhoneSuggestionItemDto> Phones { get; set; } = new();
}

public class SupplierSuggestionItemDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public class ProductSuggestionItemDto
{
    public Guid Id { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Guid SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
}

public class ContactPersonSuggestionItemDto
{
    public string Name { get; set; } = string.Empty;
    public Guid SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
}

public class PhoneSuggestionItemDto
{
    public string Phone { get; set; } = string.Empty;
    public Guid SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
}

public class SupplierTransactionDto
{
    public Guid Id { get; set; }
    public string TransactionCode { get; set; } = string.Empty;
    public string TransactionType { get; set; } = string.Empty; // PO / GRN
    public DateTime TransactionDate { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public Guid? BranchId { get; set; }
    public string? BranchName { get; set; }
}

public class SupplierBranchAssignmentDetailDto
{
    public Guid Id { get; set; }
    public Guid SupplierId { get; set; }
    public string SupplierCode { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public SupplierType SupplierType { get; set; }
    public string SupplierTypeName => SupplierType switch
    {
        SupplierType.Goods => "Hàng hóa",
        SupplierType.Service => "Dịch vụ",
        SupplierType.Both => "Cả hai",
        _ => "Khác"
    };
    public Guid BranchId { get; set; }
    public string BranchCode { get; set; } = string.Empty;
    public string BranchName { get; set; } = string.Empty;
    public string BranchAddress { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string Status { get; set; } = "Active";
}

public class SupplierBranchAssignmentQueryFilter
{
    public string? SupplierSearch { get; set; }
    public string? BranchSearch { get; set; }
    public Guid? SupplierId { get; set; }
    public Guid? BranchId { get; set; }
    public string? Status { get; set; }
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 12;
}

public class AssignSupplierToBranchesCommand
{
    public Guid SupplierId { get; set; }
    public List<Guid> BranchIds { get; set; } = [];
}

