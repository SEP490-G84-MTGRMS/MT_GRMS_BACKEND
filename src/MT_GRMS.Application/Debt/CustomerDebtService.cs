using MT_GRMS.Application.Cashbook;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Debt.Dtos;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Entities.Sales;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Domain.Exceptions;

namespace MT_GRMS.Application.Debt;

public class CustomerDebtService : ICustomerDebtService
{
    private readonly ICustomerDebtRepository _debtRepo;
    private readonly IRepository<CustomerDebtInvoice> _invoiceRepo;
    private readonly IRepository<CustomerDebtEntry> _entryRepo;
    private readonly IRepository<CustomerDebtAllocation> _allocationRepo;
    private readonly ICashbookService _cashbookService;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitOfWork _unitOfWork;

    public CustomerDebtService(
        ICustomerDebtRepository debtRepo,
        IRepository<CustomerDebtInvoice> invoiceRepo,
        IRepository<CustomerDebtEntry> entryRepo,
        IRepository<CustomerDebtAllocation> allocationRepo,
        ICashbookService cashbookService,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork)
    {
        _debtRepo = debtRepo;
        _invoiceRepo = invoiceRepo;
        _entryRepo = entryRepo;
        _allocationRepo = allocationRepo;
        _cashbookService = cashbookService;
        _tenantContext = tenantContext;
        _unitOfWork = unitOfWork;
    }

    public async Task<CustomerDebtInvoiceResponse> RecordSaleDebtAsync(RecordSaleDebtRequest request, CancellationToken ct = default)
    {
        if (request.DebtAmount <= 0)
        {
            throw new BusinessException("INVALID_AMOUNT", "Số tiền ghi nợ phải lớn hơn 0.");
        }

        var customer = await GetCustomerWithLockAsync(request.CustomerId, ct);
        var now = DateTime.UtcNow;

        var hasOverdue = await _debtRepo.HasOverdueInvoicesAsync(request.CustomerId, now, ct);

        // DEBT-01 & CONC-02: Kiểm tra quyền quản lý động (StoreOwner, Manager, ADMIN) thay vì hardcode bypass code
        var isManagerRole = !string.IsNullOrWhiteSpace(_tenantContext.CurrentRole) &&
            (_tenantContext.CurrentRole.Equals("StoreOwner", StringComparison.OrdinalIgnoreCase)
             || _tenantContext.CurrentRole.Equals("STORE_OWNER", StringComparison.OrdinalIgnoreCase)
             || _tenantContext.CurrentRole.Equals("Manager", StringComparison.OrdinalIgnoreCase)
             || _tenantContext.CurrentRole.Equals("ADMIN", StringComparison.OrdinalIgnoreCase));

        var isBypassRequested = !string.IsNullOrWhiteSpace(request.ManagerBypassCode);
        if (isBypassRequested && !isManagerRole)
        {
            throw new BusinessException("UNAUTHORIZED_BYPASS", "Chỉ người dùng có vai trò Quản lý (StoreOwner, Manager, ADMIN) mới có quyền duyệt bypass hạn mức hoặc nợ quá hạn.");
        }

        var isManagerBypass = isBypassRequested && isManagerRole;

        if (hasOverdue && !isManagerBypass)
        {
            customer.DebtStatus = CustomerDebtStatus.OverdueBlocked;
            customer.IsBlockedForDebt = true;
            await _unitOfWork.SaveChangesAsync(ct);
            throw new BusinessException("DEBT_OVERDUE_BLOCKED", "Khách hàng có hóa đơn nợ đã quá hạn. Bắt buộc thanh toán nợ cũ hoặc cần mã duyệt của Quản lý.");
        }

        var projectedDebt = customer.TotalDebt + request.DebtAmount;
        if (customer.DebtLimit > 0 && projectedDebt > customer.DebtLimit && !isManagerBypass)
        {
            customer.DebtStatus = CustomerDebtStatus.LimitExceededBlocked;
            customer.IsBlockedForDebt = true;
            await _unitOfWork.SaveChangesAsync(ct);
            throw new BusinessException("CREDIT_LIMIT_EXCEEDED", $"Đơn nợ vượt hạn mức tín dụng ({projectedDebt:N0}đ > {customer.DebtLimit:N0}đ). Cần mã duyệt của Quản lý.");
        }

        // Tự động cấn trừ tiền khách gửi trước nếu có
        var actualDebtAmount = request.DebtAmount;
        if (customer.PrepaymentBalance > 0)
        {
            var offsetFromPrepayment = Math.Min(customer.PrepaymentBalance, actualDebtAmount);
            customer.PrepaymentBalance -= offsetFromPrepayment;
            actualDebtAmount -= offsetFromPrepayment;
        }

        string? auditNote = isManagerBypass
            ? $"[ManagerBypass Approved by UserId: {_tenantContext.CurrentUserId}, Role: {_tenantContext.CurrentRole} at {now:yyyy-MM-dd HH:mm:ss} UTC, Code: {request.ManagerBypassCode}]"
            : null;

        var combinedNotes = string.IsNullOrWhiteSpace(request.Notes)
            ? auditNote
            : string.IsNullOrWhiteSpace(auditNote) ? request.Notes : $"{request.Notes} | {auditNote}";

        var invoice = new CustomerDebtInvoice
        {
            CustomerId = request.CustomerId,
            OrderId = request.OrderId,
            OrderCode = request.OrderCode,
            BranchId = request.BranchId,
            TotalDebtAmount = request.DebtAmount,
            RemainingDebtAmount = actualDebtAmount,
            DueDate = now.AddDays(customer.MaxDebtDays > 0 ? customer.MaxDebtDays : 30),
            Status = actualDebtAmount == 0 ? DebtInvoiceStatus.FullyPaid : DebtInvoiceStatus.Unpaid,
            FullyPaidAtUtc = actualDebtAmount == 0 ? now : null,
            Notes = combinedNotes
        };

        await _invoiceRepo.AddAsync(invoice, ct);

        customer.TotalDebt += actualDebtAmount;
        customer.LastDebtIncurredAtUtc = now;
        customer.DebtStatus = CustomerDebtStatus.GoodStanding;
        customer.IsBlockedForDebt = false;

        // Bút toán sổ cái nợ bất biến
        var entry = new CustomerDebtEntry
        {
            EntryCode = "CDE-" + now.ToString("yyyyMMddHHmmssfff"),
            CustomerId = request.CustomerId,
            BranchId = request.BranchId,
            EntryType = CustomerDebtType.DebtIncurred,
            Amount = request.DebtAmount,
            BalanceAfter = customer.TotalDebt,
            PrepaymentAfter = customer.PrepaymentBalance,
            ReferenceCode = request.OrderCode,
            SourceId = request.OrderId,
            CreatedByUserId = _tenantContext.CurrentUserId,
            OccurredAtUtc = now,
            Description = $"Bán chịu đơn hàng {request.OrderCode}" + (isManagerBypass ? $" (Bypass bởi Quản lý: {_tenantContext.CurrentUserId} [{_tenantContext.CurrentRole}] lúc {now:yyyy-MM-dd HH:mm:ss} UTC)" : "")
        };

        await _entryRepo.AddAsync(entry, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return MapInvoiceResponse(invoice);
    }

    public async Task<RepaymentResultResponse> RepayDebtAsync(RepayDebtRequest request, CancellationToken ct = default)
    {
        if (request.Amount <= 0)
        {
            throw new BusinessException("INVALID_AMOUNT", "Số tiền trả nợ phải lớn hơn 0.");
        }

        var customer = await GetCustomerWithLockAsync(request.CustomerId, ct);
        var openInvoices = await _debtRepo.GetOpenInvoicesAsync(request.CustomerId, ct);
        var now = DateTime.UtcNow;

        var remainingCash = request.Amount;
        var allocations = new List<AllocationDetailResponse>();

        var entry = new CustomerDebtEntry
        {
            EntryCode = "CDE-" + now.ToString("yyyyMMddHHmmssfff"),
            CustomerId = request.CustomerId,
            EntryType = CustomerDebtType.DebtRepayment,
            Amount = request.Amount,
            ReferenceCode = request.ReferenceCode ?? "PAY-" + now.ToString("yyyyMMddHHmmss"),
            CreatedByUserId = _tenantContext.CurrentUserId,
            OccurredAtUtc = now,
            Description = !string.IsNullOrWhiteSpace(request.Notes)
                ? $"{request.Notes.Trim()} ({request.PaymentSourceType})"
                : $"Thu nợ khách hàng ({request.PaymentSourceType})"
        };

        await _entryRepo.AddAsync(entry, ct);

        // Thuật toán phân bổ thanh toán FIFO
        foreach (var inv in openInvoices)
        {
            if (remainingCash <= 0) break;

            var payAmount = Math.Min(remainingCash, inv.RemainingDebtAmount);
            inv.RemainingDebtAmount -= payAmount;
            remainingCash -= payAmount;

            if (inv.RemainingDebtAmount == 0)
            {
                inv.Status = DebtInvoiceStatus.FullyPaid;
                inv.FullyPaidAtUtc = now;
            }
            else
            {
                inv.Status = DebtInvoiceStatus.PartiallyPaid;
            }

            var allocation = new CustomerDebtAllocation
            {
                DebtEntryId = entry.Id,
                DebtInvoiceId = inv.Id,
                AllocatedAmount = payAmount,
                Notes = $"Phân bổ thanh toán đơn {inv.OrderCode}"
            };

            await _allocationRepo.AddAsync(allocation, ct);
            allocations.Add(new AllocationDetailResponse(inv.Id, inv.OrderCode, payAmount, inv.RemainingDebtAmount));
        }

        var actualRepaidDebt = request.Amount - remainingCash;
        customer.TotalDebt = Math.Max(0, customer.TotalDebt - actualRepaidDebt);
        if (remainingCash > 0)
        {
            customer.PrepaymentBalance += remainingCash;
        }

        customer.LastRepaymentAtUtc = now;
        if (customer.TotalDebt == 0)
        {
            customer.DebtStatus = CustomerDebtStatus.GoodStanding;
            customer.IsBlockedForDebt = false;
        }

        entry.BalanceAfter = customer.TotalDebt;
        entry.PrepaymentAfter = customer.PrepaymentBalance;

        // Tích hợp đồng bộ Sổ Quỹ
        var cashbookDesc = !string.IsNullOrWhiteSpace(customer.Name)
            ? $"Thu nợ khách hàng {customer.Name}"
            : $"Thu nợ khách hàng {request.CustomerId}";

        await _cashbookService.ReceiveCashAsync(
            request.PaymentSourceAccountId,
            request.PaymentSourceType,
            request.Amount,
            "DEBT_REPAYMENT",
            entry.Id,
            entry.ReferenceCode,
            cashbookDesc,
            ct
        );

        await _unitOfWork.SaveChangesAsync(ct);

        return new RepaymentResultResponse(entry.Id, entry.EntryCode, actualRepaidDebt, customer.TotalDebt, customer.PrepaymentBalance, allocations);
    }

    public async Task<CustomerDebtEntryResponse> OffsetGoodsReturnDebtAsync(OffsetGoodsReturnDebtRequest request, CancellationToken ct = default)
    {
        if (request.ReturnAmount <= 0)
        {
            throw new BusinessException("INVALID_AMOUNT", "Số tiền hàng trả phải lớn hơn 0.");
        }

        var customer = await GetCustomerWithLockAsync(request.CustomerId, ct);
        var now = DateTime.UtcNow;
        var unallocatedReturnAmount = request.ReturnAmount;

        var entry = new CustomerDebtEntry
        {
            EntryCode = "CDE-" + now.ToString("yyyyMMddHHmmssfff"),
            CustomerId = request.CustomerId,
            EntryType = CustomerDebtType.GoodsReturnOffset,
            Amount = request.ReturnAmount,
            ReferenceCode = "RET-" + request.ReturnOrderId.ToString()[..8],
            SourceId = request.ReturnOrderId,
            CreatedByUserId = _tenantContext.CurrentUserId,
            OccurredAtUtc = now,
            Description = "Cấn trừ nợ do trả lại hàng đơn mua chịu"
        };

        await _entryRepo.AddAsync(entry, ct);

        // Bước 1: Tìm hóa đơn nợ của chính đơn hàng gốc
        var originalInvoice = await _debtRepo.GetInvoiceByOrderIdAsync(request.OriginalOrderId, ct);
        if (originalInvoice != null && originalInvoice.RemainingDebtAmount > 0)
        {
            var offsetOriginal = Math.Min(unallocatedReturnAmount, originalInvoice.RemainingDebtAmount);
            originalInvoice.RemainingDebtAmount -= offsetOriginal;
            unallocatedReturnAmount -= offsetOriginal;

            if (originalInvoice.RemainingDebtAmount == 0)
            {
                originalInvoice.Status = DebtInvoiceStatus.FullyPaid;
                originalInvoice.FullyPaidAtUtc = now;
            }
            else
            {
                originalInvoice.Status = DebtInvoiceStatus.PartiallyPaid;
            }

            await _allocationRepo.AddAsync(new CustomerDebtAllocation
            {
                DebtEntryId = entry.Id,
                DebtInvoiceId = originalInvoice.Id,
                AllocatedAmount = offsetOriginal,
                Notes = "Cấn trừ trực tiếp vào hóa đơn nợ gốc"
            }, ct);
        }

        // Bước 2: Cấn trừ FIFO vào các hóa đơn khác nếu còn tiền trả hàng
        if (unallocatedReturnAmount > 0)
        {
            var otherOpenInvoices = await _debtRepo.GetOpenInvoicesAsync(request.CustomerId, ct);
            foreach (var inv in otherOpenInvoices.Where(i => i.Id != originalInvoice?.Id))
            {
                if (unallocatedReturnAmount <= 0) break;

                var offsetAmount = Math.Min(unallocatedReturnAmount, inv.RemainingDebtAmount);
                inv.RemainingDebtAmount -= offsetAmount;
                unallocatedReturnAmount -= offsetAmount;

                if (inv.RemainingDebtAmount == 0)
                {
                    inv.Status = DebtInvoiceStatus.FullyPaid;
                    inv.FullyPaidAtUtc = now;
                }
                else
                {
                    inv.Status = DebtInvoiceStatus.PartiallyPaid;
                }

                await _allocationRepo.AddAsync(new CustomerDebtAllocation
                {
                    DebtEntryId = entry.Id,
                    DebtInvoiceId = inv.Id,
                    AllocatedAmount = offsetAmount,
                    Notes = $"Cấn trừ FIFO vào hóa đơn {inv.OrderCode}"
                }, ct);
            }
        }

        var totalDebtOffset = request.ReturnAmount - unallocatedReturnAmount;
        customer.TotalDebt = Math.Max(0, customer.TotalDebt - totalDebtOffset);

        // Bước 3: Xử lý tiền dư khi khách đã sạch 100% nợ
        if (unallocatedReturnAmount > 0)
        {
            if (request.RefundExcessInCash && request.RefundAccountId.HasValue)
            {
                // Hoàn tiền mặt từ két quầy cho khách
                await _cashbookService.DisburseCashAsync(
                    request.RefundAccountId.Value,
                    FinanceAccountType.Till,
                    unallocatedReturnAmount,
                    "GOODS_RETURN_REFUND",
                    entry.Id,
                    entry.ReferenceCode,
                    "Hoàn tiền mặt phần chênh lệch trả hàng sau khi đã cấn trừ hết nợ",
                    ct
                );
            }
            else
            {
                // Cộng vào số tiền đặt cọc/dư của khách
                customer.PrepaymentBalance += unallocatedReturnAmount;
            }
        }

        if (customer.TotalDebt == 0)
        {
            customer.DebtStatus = CustomerDebtStatus.GoodStanding;
            customer.IsBlockedForDebt = false;
        }

        entry.BalanceAfter = customer.TotalDebt;
        entry.PrepaymentAfter = customer.PrepaymentBalance;

        await _unitOfWork.SaveChangesAsync(ct);

        return MapEntryResponse(entry);
    }

    public async Task<CustomerDebtEntryResponse> AdjustDebtAsync(AdjustDebtRequest request, CancellationToken ct = default)
    {
        if (request.AdjustmentAmount <= 0)
        {
            throw new BusinessException("INVALID_AMOUNT", "Số tiền điều chỉnh nợ phải lớn hơn 0.");
        }

        if (string.IsNullOrWhiteSpace(request.ManagerApprovalCode) || 
            (!request.ManagerApprovalCode.StartsWith("MGR-", StringComparison.OrdinalIgnoreCase) && 
             !request.ManagerApprovalCode.StartsWith("ADJUST-", StringComparison.OrdinalIgnoreCase)))
        {
            throw new BusinessException("UNAUTHORIZED_ADJUSTMENT", "Mã phê duyệt điều chỉnh nợ của Quản lý không hợp lệ.");
        }

        var customer = await GetCustomerWithLockAsync(request.CustomerId, ct);
        var now = DateTime.UtcNow;

        if (request.IsIncrease)
        {
            customer.TotalDebt += request.AdjustmentAmount;
        }
        else
        {
            customer.TotalDebt = Math.Max(0, customer.TotalDebt - request.AdjustmentAmount);
        }

        var entry = new CustomerDebtEntry
        {
            EntryCode = "CDE-" + now.ToString("yyyyMMddHHmmssfff"),
            CustomerId = request.CustomerId,
            EntryType = CustomerDebtType.DebtAdjustment,
            Amount = request.AdjustmentAmount,
            BalanceAfter = customer.TotalDebt,
            PrepaymentAfter = customer.PrepaymentBalance,
            ReferenceCode = "ADJ-" + now.ToString("yyyyMMddHHmmss"),
            CreatedByUserId = _tenantContext.CurrentUserId,
            OccurredAtUtc = now,
            Description = $"Điều chỉnh {(request.IsIncrease ? "tăng" : "giảm")} nợ: {request.Reason}"
        };

        await _entryRepo.AddAsync(entry, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return MapEntryResponse(entry);
    }

    public async Task<CustomerDebtEntryResponse> WriteOffBadDebtAsync(WriteOffBadDebtRequest request, CancellationToken ct = default)
    {
        if (request.WriteOffAmount <= 0)
        {
            throw new BusinessException("INVALID_AMOUNT", "Số tiền xóa nợ xấu phải lớn hơn 0.");
        }

        var customer = await GetCustomerWithLockAsync(request.CustomerId, ct);
        if (customer.TotalDebt < request.WriteOffAmount)
        {
            throw new BusinessException("INVALID_WRITEOFF", "Số tiền xóa nợ xấu vượt quá số dư nợ hiện tại.");
        }

        var now = DateTime.UtcNow;
        customer.TotalDebt -= request.WriteOffAmount;
        customer.DebtStatus = CustomerDebtStatus.GoodStanding;
        customer.IsBlockedForDebt = false;

        var entry = new CustomerDebtEntry
        {
            EntryCode = "CDE-" + now.ToString("yyyyMMddHHmmssfff"),
            CustomerId = request.CustomerId,
            EntryType = CustomerDebtType.BadDebtWriteOff,
            Amount = request.WriteOffAmount,
            BalanceAfter = customer.TotalDebt,
            PrepaymentAfter = customer.PrepaymentBalance,
            ReferenceCode = "WO-" + now.ToString("yyyyMMddHHmmss"),
            CreatedByUserId = _tenantContext.CurrentUserId,
            OccurredAtUtc = now,
            Description = $"Xóa nợ xấu: {request.Reason}. Biên bản đính kèm: {request.EvidenceDocumentUrl}"
        };

        await _entryRepo.AddAsync(entry, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return MapEntryResponse(entry);
    }

    public async Task<CustomerDebtProfileResponse> GetProfileAsync(Guid customerId, CancellationToken ct = default)
    {
        var customer = await GetCustomerWithLockAsync(customerId, ct);
        var openInvoices = await _debtRepo.GetOpenInvoicesAsync(customerId, ct);

        return new CustomerDebtProfileResponse(
            customer.Id,
            customer.Id,
            customer.DebtLimit,
            customer.MaxDebtDays,
            customer.TotalDebt,
            customer.PrepaymentBalance,
            customer.DebtStatus.ToString(),
            customer.LastDebtIncurredAtUtc,
            customer.LastRepaymentAtUtc,
            openInvoices.Select(MapInvoiceResponse).ToList()
        );
    }

    public async Task<List<CustomerDebtEntryResponse>> GetLedgerAsync(Guid customerId, int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        var entries = await _entryRepo.FindAsync(e => e.CustomerId == customerId, ct);
        return entries
            .OrderByDescending(e => e.OccurredAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(MapEntryResponse)
            .ToList();
    }

    private async Task<Customer> GetCustomerWithLockAsync(Guid customerId, CancellationToken ct)
    {
        var customer = await _debtRepo.GetCustomerWithLockAsync(customerId, ct);
        if (customer != null) return customer;

        // Nếu khách chưa có trong DB, ném NotFoundException
        throw new NotFoundException($"Không tìm thấy khách hàng với Id: {customerId}");
    }

    private static CustomerDebtInvoiceResponse MapInvoiceResponse(CustomerDebtInvoice i) =>
        new(i.Id, i.CustomerId, i.OrderId, i.OrderCode, i.TotalDebtAmount, i.RemainingDebtAmount, i.DueDate, i.Status.ToString(), i.DueDate < DateTime.UtcNow && i.RemainingDebtAmount > 0);

    private static CustomerDebtEntryResponse MapEntryResponse(CustomerDebtEntry e) =>
        new(e.Id, e.EntryCode, e.CustomerId, e.EntryType.ToString(), e.Amount, e.BalanceAfter, e.PrepaymentAfter, e.ReferenceCode, e.Description, e.OccurredAtUtc);
}
