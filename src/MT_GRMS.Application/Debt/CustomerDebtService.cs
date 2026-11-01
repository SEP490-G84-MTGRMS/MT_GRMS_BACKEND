using MT_GRMS.Application.Cashbook;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Debt.Dtos;
using MT_GRMS.Domain.Entities.Finance;
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

        var profile = await GetOrCreateProfileWithLockAsync(request.CustomerId, ct);
        var now = DateTime.UtcNow;

        var hasOverdue = await _debtRepo.HasOverdueInvoicesAsync(request.CustomerId, now, ct);
        var isManagerBypass = !string.IsNullOrWhiteSpace(request.ManagerBypassCode) && request.ManagerBypassCode == "MGR-BYPASS-2026";

        if (hasOverdue && !isManagerBypass)
        {
            profile.DebtStatus = CustomerDebtStatus.OverdueBlocked;
            await _unitOfWork.SaveChangesAsync(ct);
            throw new BusinessException("DEBT_OVERDUE_BLOCKED", "Khách hàng có hóa đơn nợ đã quá hạn. Bắt buộc thanh toán nợ cũ hoặc cần mã duyệt của Quản lý.");
        }

        var projectedDebt = profile.CurrentDebtBalance + request.DebtAmount;
        if (projectedDebt > profile.CreditLimit && !isManagerBypass)
        {
            profile.DebtStatus = CustomerDebtStatus.LimitExceededBlocked;
            await _unitOfWork.SaveChangesAsync(ct);
            throw new BusinessException("CREDIT_LIMIT_EXCEEDED", $"Đơn nợ vượt hạn mức tín dụng ({projectedDebt:N0}đ > {profile.CreditLimit:N0}đ). Cần mã duyệt của Quản lý.");
        }

        // Tự động cấn trừ tiền khách gửi trước nếu có
        var actualDebtAmount = request.DebtAmount;
        if (profile.PrepaymentBalance > 0)
        {
            var offsetFromPrepayment = Math.Min(profile.PrepaymentBalance, actualDebtAmount);
            profile.PrepaymentBalance -= offsetFromPrepayment;
            actualDebtAmount -= offsetFromPrepayment;
        }

        var invoice = new CustomerDebtInvoice
        {
            CustomerId = request.CustomerId,
            OrderId = request.OrderId,
            OrderCode = request.OrderCode,
            BranchId = request.BranchId,
            TotalDebtAmount = request.DebtAmount,
            RemainingDebtAmount = actualDebtAmount,
            DueDate = now.AddDays(profile.MaxDebtDays),
            Status = actualDebtAmount == 0 ? DebtInvoiceStatus.FullyPaid : DebtInvoiceStatus.Unpaid,
            FullyPaidAtUtc = actualDebtAmount == 0 ? now : null,
            Notes = request.Notes
        };

        await _invoiceRepo.AddAsync(invoice, ct);

        profile.CurrentDebtBalance += actualDebtAmount;
        profile.LastDebtIncurredAtUtc = now;
        profile.DebtStatus = CustomerDebtStatus.GoodStanding;

        // Bút toán sổ cái nợ bất biến
        var entry = new CustomerDebtEntry
        {
            EntryCode = "CDE-" + now.ToString("yyyyMMddHHmmssfff"),
            CustomerId = request.CustomerId,
            BranchId = request.BranchId,
            EntryType = CustomerDebtType.DebtIncurred,
            Amount = request.DebtAmount,
            BalanceAfter = profile.CurrentDebtBalance,
            PrepaymentAfter = profile.PrepaymentBalance,
            ReferenceCode = request.OrderCode,
            SourceId = request.OrderId,
            CreatedByUserId = _tenantContext.CurrentUserId,
            OccurredAtUtc = now,
            Description = $"Bán chịu đơn hàng {request.OrderCode}" + (isManagerBypass ? " (Bypass bởi Quản lý)" : "")
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

        var profile = await GetOrCreateProfileWithLockAsync(request.CustomerId, ct);
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
            Description = $"Thu nợ khách hàng ({request.PaymentSourceType})"
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
        profile.CurrentDebtBalance -= actualRepaidDebt;
        if (remainingCash > 0)
        {
            profile.PrepaymentBalance += remainingCash;
        }

        profile.LastRepaymentAtUtc = now;
        if (profile.CurrentDebtBalance == 0)
        {
            profile.DebtStatus = CustomerDebtStatus.GoodStanding;
        }

        entry.BalanceAfter = profile.CurrentDebtBalance;
        entry.PrepaymentAfter = profile.PrepaymentBalance;

        // Tích hợp đồng bộ Sổ Quỹ
        await _cashbookService.ReceiveCashAsync(
            request.PaymentSourceAccountId,
            request.PaymentSourceType,
            request.Amount,
            "DEBT_REPAYMENT",
            entry.Id,
            entry.ReferenceCode,
            $"Thu nợ khách hàng {request.CustomerId}",
            ct
        );

        await _unitOfWork.SaveChangesAsync(ct);

        return new RepaymentResultResponse(entry.Id, entry.EntryCode, actualRepaidDebt, profile.CurrentDebtBalance, profile.PrepaymentBalance, allocations);
    }

    public async Task<CustomerDebtEntryResponse> OffsetGoodsReturnDebtAsync(OffsetGoodsReturnDebtRequest request, CancellationToken ct = default)
    {
        if (request.ReturnAmount <= 0)
        {
            throw new BusinessException("INVALID_AMOUNT", "Số tiền hàng trả phải lớn hơn 0.");
        }

        var profile = await GetOrCreateProfileWithLockAsync(request.CustomerId, ct);
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
        profile.CurrentDebtBalance -= totalDebtOffset;

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
                profile.PrepaymentBalance += unallocatedReturnAmount;
            }
        }

        if (profile.CurrentDebtBalance == 0)
        {
            profile.DebtStatus = CustomerDebtStatus.GoodStanding;
        }

        entry.BalanceAfter = profile.CurrentDebtBalance;
        entry.PrepaymentAfter = profile.PrepaymentBalance;

        await _unitOfWork.SaveChangesAsync(ct);
        return MapEntryResponse(entry);
    }

    public async Task<CustomerDebtEntryResponse> AdjustDebtAsync(AdjustDebtRequest request, CancellationToken ct = default)
    {
        if (request.AdjustmentAmount <= 0)
        {
            throw new BusinessException("INVALID_AMOUNT", "Số tiền điều chỉnh phải lớn hơn 0.");
        }

        if (string.IsNullOrWhiteSpace(request.ManagerApprovalCode) || request.ManagerApprovalCode != "MGR-ADJUST-2026")
        {
            throw new BusinessException("UNAUTHORIZED_ADJUSTMENT", "Mã phê duyệt điều chỉnh nợ của Quản lý không hợp lệ.");
        }

        var profile = await GetOrCreateProfileWithLockAsync(request.CustomerId, ct);
        var now = DateTime.UtcNow;

        if (request.IsIncrease)
        {
            profile.CurrentDebtBalance += request.AdjustmentAmount;
        }
        else
        {
            if (profile.CurrentDebtBalance < request.AdjustmentAmount)
            {
                throw new BusinessException("INVALID_ADJUSTMENT", "Không thể điều chỉnh giảm nợ vượt quá số nợ hiện tại.");
            }
            profile.CurrentDebtBalance -= request.AdjustmentAmount;
        }

        var entry = new CustomerDebtEntry
        {
            EntryCode = "CDE-" + now.ToString("yyyyMMddHHmmssfff"),
            CustomerId = request.CustomerId,
            EntryType = CustomerDebtType.DebtAdjustment,
            Amount = request.AdjustmentAmount,
            BalanceAfter = profile.CurrentDebtBalance,
            PrepaymentAfter = profile.PrepaymentBalance,
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

        var profile = await GetOrCreateProfileWithLockAsync(request.CustomerId, ct);
        if (profile.CurrentDebtBalance < request.WriteOffAmount)
        {
            throw new BusinessException("INVALID_WRITEOFF", "Số tiền xóa nợ xấu vượt quá số dư nợ hiện tại.");
        }

        var now = DateTime.UtcNow;
        profile.CurrentDebtBalance -= request.WriteOffAmount;
        profile.DebtStatus = CustomerDebtStatus.GoodStanding;

        var entry = new CustomerDebtEntry
        {
            EntryCode = "CDE-" + now.ToString("yyyyMMddHHmmssfff"),
            CustomerId = request.CustomerId,
            EntryType = CustomerDebtType.BadDebtWriteOff,
            Amount = request.WriteOffAmount,
            BalanceAfter = profile.CurrentDebtBalance,
            PrepaymentAfter = profile.PrepaymentBalance,
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
        var profile = await GetOrCreateProfileWithLockAsync(customerId, ct);
        var openInvoices = await _debtRepo.GetOpenInvoicesAsync(customerId, ct);

        return new CustomerDebtProfileResponse(
            profile.Id,
            profile.CustomerId,
            profile.CreditLimit,
            profile.MaxDebtDays,
            profile.CurrentDebtBalance,
            profile.PrepaymentBalance,
            profile.DebtStatus.ToString(),
            profile.LastDebtIncurredAtUtc,
            profile.LastRepaymentAtUtc,
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

    private async Task<CustomerDebtProfile> GetOrCreateProfileWithLockAsync(Guid customerId, CancellationToken ct)
    {
        var profile = await _debtRepo.GetProfileWithLockAsync(customerId, ct);
        if (profile != null) return profile;

        profile = new CustomerDebtProfile
        {
            CustomerId = customerId,
            CreditLimit = 2_000_000m, // Hạn mức mặc định 2 triệu
            MaxDebtDays = 30,
            CurrentDebtBalance = 0,
            PrepaymentBalance = 0,
            DebtStatus = CustomerDebtStatus.GoodStanding
        };

        await _debtRepo.AddAsync(profile, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        return profile;
    }

    private static CustomerDebtInvoiceResponse MapInvoiceResponse(CustomerDebtInvoice i) =>
        new(i.Id, i.CustomerId, i.OrderId, i.OrderCode, i.TotalDebtAmount, i.RemainingDebtAmount, i.DueDate, i.Status.ToString(), i.DueDate < DateTime.UtcNow && i.RemainingDebtAmount > 0);

    private static CustomerDebtEntryResponse MapEntryResponse(CustomerDebtEntry e) =>
        new(e.Id, e.EntryCode, e.CustomerId, e.EntryType.ToString(), e.Amount, e.BalanceAfter, e.PrepaymentAfter, e.ReferenceCode, e.Description, e.OccurredAtUtc);
}
