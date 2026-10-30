using MT_GRMS.Application.Cashbook.Dtos;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Common.Services;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Domain.Exceptions;

namespace MT_GRMS.Application.Cashbook;

public class CashbookService : ICashbookService
{
    private readonly ICashbookRepository _cashbookRepo;
    private readonly IRepository<BranchCashTill> _tillRepo;
    private readonly IRepository<BranchSafe> _safeRepo;
    private readonly IRepository<CashInTransit> _transitRepo;
    private readonly IRepository<ShiftZReport> _shiftRepo;
    private readonly IRepository<TenantBankAccount> _bankRepo;
    private readonly IAuditHashService _auditHashService;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitOfWork _unitOfWork;

    public CashbookService(
        ICashbookRepository cashbookRepo,
        IRepository<BranchCashTill> tillRepo,
        IRepository<BranchSafe> safeRepo,
        IRepository<CashInTransit> transitRepo,
        IRepository<ShiftZReport> shiftRepo,
        IRepository<TenantBankAccount> bankRepo,
        IAuditHashService auditHashService,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork)
    {
        _cashbookRepo = cashbookRepo;
        _tillRepo = tillRepo;
        _safeRepo = safeRepo;
        _transitRepo = transitRepo;
        _shiftRepo = shiftRepo;
        _bankRepo = bankRepo;
        _auditHashService = auditHashService;
        _tenantContext = tenantContext;
        _unitOfWork = unitOfWork;
    }

    public async Task<ShiftResponse> OpenShiftAsync(OpenShiftRequest request, CancellationToken ct = default)
    {
        var till = await _cashbookRepo.GetTillWithLockAsync(request.TillId, ct)
            ?? throw new NotFoundException($"Không tìm thấy két quầy với Id: {request.TillId}");

        var activeShift = await _cashbookRepo.GetActiveShiftByTillIdAsync(request.TillId, ct);
        if (activeShift != null)
        {
            throw new BusinessException("ACTIVE_SHIFT_EXISTS", "Két quầy này đang có ca làm việc chưa chốt. Vui lòng chốt ca trước.");
        }

        var lastClosed = await _cashbookRepo.GetLastClosedShiftByTillIdAsync(request.TillId, ct);
        var expectedOpeningFloat = lastClosed?.RetainedFloatAmount ?? till.CurrentBalance;

        var shift = new ShiftZReport
        {
            TillId = request.TillId,
            CashierUserId = Guid.NewGuid(),
            OpeningFloat = expectedOpeningFloat,
            ExpectedCash = expectedOpeningFloat,
            ActualCashCounted = request.CountedFloat,
            Discrepancy = request.CountedFloat - expectedOpeningFloat,
            RetainedFloatAmount = 0,
            DroppedToSafeAmount = 0,
            Status = ShiftState.Open,
            Notes = request.Notes
        };

        await _shiftRepo.AddAsync(shift, ct);
        till.ActiveShiftId = shift.Id;
        till.CurrentBalance = request.CountedFloat;
        await _unitOfWork.SaveChangesAsync(ct);

        return MapShiftResponse(shift);
    }

    public async Task<ShiftResponse> CloseShiftZReportAsync(CloseShiftRequest request, CancellationToken ct = default)
    {
        var shift = await _shiftRepo.GetByIdAsync(request.ShiftId, ct)
            ?? throw new NotFoundException($"Không tìm thấy ca làm việc: {request.ShiftId}");

        if (shift.Status == ShiftState.Closed)
        {
            throw new BusinessException("SHIFT_ALREADY_CLOSED", "Ca làm việc này đã được chốt trước đó.");
        }

        var till = await _cashbookRepo.GetTillWithLockAsync(shift.TillId, ct)
            ?? throw new NotFoundException($"Không tìm thấy két quầy: {shift.TillId}");

        shift.ExpectedCash = shift.OpeningFloat + shift.TotalCashIn - shift.TotalCashOut;
        shift.ActualCashCounted = request.ActualCashCounted;
        shift.Discrepancy = request.ActualCashCounted - shift.ExpectedCash;
        shift.RetainedFloatAmount = request.RetainedFloatAmount;
        shift.DroppedToSafeAmount = Math.Max(0, request.ActualCashCounted - request.RetainedFloatAmount);
        shift.ClosedAtUtc = DateTime.UtcNow;
        shift.Status = ShiftState.Closed;
        shift.Notes = request.Notes;

        // Xử lý chênh lệch ngoài dung sai (+- 2000đ)
        if (shift.Discrepancy > 2000)
        {
            await RecordEntryInternalAsync(till.Id, FinanceAccountType.Till, till.BranchId, CashbookEntryType.Receipt, shift.Discrepancy, "CASH_OVER_SUSPENSE", shift.Id, "SHF-" + shift.Id.ToString()[..8], "Thừa tiền chốt ca chờ chủ xử lý", ct);
        }
        else if (shift.Discrepancy < -2000)
        {
            await RecordEntryInternalAsync(till.Id, FinanceAccountType.Till, till.BranchId, CashbookEntryType.Payment, Math.Abs(shift.Discrepancy), "CASH_SHORTAGE_PENDING", shift.Id, "SHF-" + shift.Id.ToString()[..8], "Thiếu tiền chốt ca chờ xử lý bồi hoàn", ct);
        }

        till.ActiveShiftId = null;
        till.CurrentBalance = request.RetainedFloatAmount;
        await _unitOfWork.SaveChangesAsync(ct);

        return MapShiftResponse(shift);
    }

    public async Task<bool> ProcessCashDropAsync(CashDropRequest request, CancellationToken ct = default)
    {
        if (request.Amount <= 0)
        {
            throw new BusinessException("INVALID_AMOUNT", "Số tiền cash drop phải lớn hơn 0.");
        }

        var shift = await _shiftRepo.GetByIdAsync(request.ShiftId, ct)
            ?? throw new NotFoundException("Không tìm thấy ca làm việc.");

        var till = await _cashbookRepo.GetTillWithLockAsync(shift.TillId, ct)
            ?? throw new NotFoundException("Không tìm thấy két quầy.");

        if (till.CurrentBalance < request.Amount)
        {
            throw new InsufficientStockException($"Két quầy không đủ tiền để cash drop (Hiện có: {till.CurrentBalance:N0}đ).");
        }

        var safe = await GetOrCreateSafeAsync(till.BranchId, ct);
        var lockedSafe = await _cashbookRepo.GetSafeWithLockAsync(safe.Id, ct) ?? safe;

        till.CurrentBalance -= request.Amount;
        lockedSafe.CurrentBalance += request.Amount;
        shift.DroppedToSafeAmount += request.Amount;

        await RecordEntryInternalAsync(till.Id, FinanceAccountType.Till, till.BranchId, CashbookEntryType.Payment, request.Amount, "CASH_DROP", shift.Id, "CD-" + shift.Id.ToString()[..8], "Cash drop chuyển tiền két quầy sang két sắt", ct);
        await RecordEntryInternalAsync(lockedSafe.Id, FinanceAccountType.Safe, till.BranchId, CashbookEntryType.Receipt, request.Amount, "CASH_DROP", shift.Id, "CD-" + shift.Id.ToString()[..8], "Nhận cash drop từ két quầy", ct);

        await _unitOfWork.SaveChangesAsync(ct);
        return true;
    }

    public async Task<CashInTransitResponse> CreateDepositToBankAsync(DepositToBankRequest request, CancellationToken ct = default)
    {
        if (request.Amount <= 0)
        {
            throw new BusinessException("INVALID_AMOUNT", "Số tiền nộp ngân hàng phải lớn hơn 0.");
        }

        var safe = await _cashbookRepo.GetSafeWithLockAsync(request.SourceAccountId, ct)
            ?? throw new NotFoundException("Không tìm thấy két sắt nguồn.");

        if (safe.CurrentBalance < request.Amount)
        {
            throw new InsufficientStockException($"Két sắt không đủ tiền để nộp ngân hàng (Hiện có: {safe.CurrentBalance:N0}đ).");
        }

        safe.CurrentBalance -= request.Amount;

        var transit = new CashInTransit
        {
            TransitCode = "CIT-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss"),
            SourceAccountId = safe.Id,
            SourceAccountType = FinanceAccountType.Safe,
            TargetBankAccountId = request.TargetBankAccountId,
            AmountSent = request.Amount,
            CarrierUserId = request.CarrierUserId,
            Status = CashInTransitStatus.PendingBankConfirmation,
            Note = request.Note
        };

        await _transitRepo.AddAsync(transit, ct);

        await RecordEntryInternalAsync(safe.Id, FinanceAccountType.Safe, safe.BranchId, CashbookEntryType.Payment, request.Amount, "BANK_DEPOSIT", transit.Id, transit.TransitCode, "Xuất két sắt nộp tiền vào tài khoản ngân hàng", ct);

        await _unitOfWork.SaveChangesAsync(ct);

        return new CashInTransitResponse(transit.Id, transit.TransitCode, transit.SourceAccountId, transit.TargetBankAccountId, transit.AmountSent, transit.AmountReceived, transit.DiscrepancyAmount, transit.CarrierUserId, transit.Status.ToString(), transit.CreatedAt);
    }

    public async Task<CashInTransitResponse> ConfirmBankDepositAsync(ConfirmBankDepositRequest request, CancellationToken ct = default)
    {
        var transit = await _transitRepo.GetByIdAsync(request.TransitId, ct)
            ?? throw new NotFoundException("Không tìm thấy lệnh nộp tiền đang chuyển.");

        if (transit.Status != CashInTransitStatus.PendingBankConfirmation)
        {
            throw new BusinessException("INVALID_STATUS", "Lệnh nộp tiền này không ở trạng thái chờ xác nhận.");
        }

        var bankAccount = await _cashbookRepo.GetBankAccountWithLockAsync(transit.TargetBankAccountId, ct)
            ?? throw new NotFoundException("Không tìm thấy tài khoản ngân hàng thụ hưởng.");

        transit.AmountReceived = request.ActualReceivedAmount;
        transit.DiscrepancyAmount = request.ActualReceivedAmount - transit.AmountSent;
        transit.ConfirmedAtUtc = DateTime.UtcNow;
        transit.Status = transit.DiscrepancyAmount != 0 ? CashInTransitStatus.Discrepancy : CashInTransitStatus.Completed;

        bankAccount.CurrentBalance += request.ActualReceivedAmount;

        await RecordEntryInternalAsync(bankAccount.Id, FinanceAccountType.Bank, null, CashbookEntryType.Receipt, request.ActualReceivedAmount, "BANK_DEPOSIT", transit.Id, transit.TransitCode, "Xác nhận tiền nộp đã vào tài khoản ngân hàng", ct);

        await _unitOfWork.SaveChangesAsync(ct);

        return new CashInTransitResponse(transit.Id, transit.TransitCode, transit.SourceAccountId, transit.TargetBankAccountId, transit.AmountSent, transit.AmountReceived, transit.DiscrepancyAmount, transit.CarrierUserId, transit.Status.ToString(), transit.CreatedAt);
    }

    public async Task<CashbookEntryResponse> DisburseCashAsync(Guid accountId, FinanceAccountType accountType, decimal amount, string sourceType, Guid? sourceId, string referenceCode, string description, CancellationToken ct = default)
    {
        if (amount <= 0)
        {
            throw new BusinessException("INVALID_AMOUNT", "Số tiền chi phải lớn hơn 0.");
        }

        Guid? branchId = null;

        if (accountType == FinanceAccountType.Till)
        {
            var till = await _cashbookRepo.GetTillWithLockAsync(accountId, ct)
                ?? throw new NotFoundException("Không tìm thấy két quầy.");

            if (till.CurrentBalance < amount)
            {
                throw new InsufficientStockException($"Két không đủ tiền mặt (Còn {till.CurrentBalance:N0}đ). Không thể thực hiện chi {amount:N0}đ.");
            }

            till.CurrentBalance -= amount;
            branchId = till.BranchId;

            if (till.ActiveShiftId.HasValue)
            {
                var shift = await _shiftRepo.GetByIdAsync(till.ActiveShiftId.Value, ct);
                if (shift != null) shift.TotalCashOut += amount;
            }
        }
        else if (accountType == FinanceAccountType.Safe)
        {
            var safe = await _cashbookRepo.GetSafeWithLockAsync(accountId, ct)
                ?? throw new NotFoundException("Không tìm thấy két sắt.");

            if (safe.CurrentBalance < amount)
            {
                throw new InsufficientStockException($"Két sắt không đủ tiền (Còn {safe.CurrentBalance:N0}đ).");
            }

            safe.CurrentBalance -= amount;
            branchId = safe.BranchId;
        }
        else if (accountType == FinanceAccountType.Bank)
        {
            var bank = await _cashbookRepo.GetBankAccountWithLockAsync(accountId, ct)
                ?? throw new NotFoundException("Không tìm thấy tài khoản ngân hàng.");

            if (bank.CurrentBalance < amount)
            {
                throw new InsufficientStockException($"Tài khoản ngân hàng không đủ số dư (Còn {bank.CurrentBalance:N0}đ).");
            }

            bank.CurrentBalance -= amount;
        }

        var entry = await RecordEntryInternalAsync(accountId, accountType, branchId, CashbookEntryType.Payment, amount, sourceType, sourceId, referenceCode, description, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return MapEntryResponse(entry);
    }

    public async Task<CashbookEntryResponse> ReceiveCashAsync(Guid accountId, FinanceAccountType accountType, decimal amount, string sourceType, Guid? sourceId, string referenceCode, string description, CancellationToken ct = default)
    {
        if (amount <= 0)
        {
            throw new BusinessException("INVALID_AMOUNT", "Số tiền thu phải lớn hơn 0.");
        }

        Guid? branchId = null;

        if (accountType == FinanceAccountType.Till)
        {
            var till = await _cashbookRepo.GetTillWithLockAsync(accountId, ct)
                ?? throw new NotFoundException("Không tìm thấy két quầy.");

            till.CurrentBalance += amount;
            branchId = till.BranchId;

            if (till.ActiveShiftId.HasValue)
            {
                var shift = await _shiftRepo.GetByIdAsync(till.ActiveShiftId.Value, ct);
                if (shift != null) shift.TotalCashIn += amount;
            }
        }
        else if (accountType == FinanceAccountType.Safe)
        {
            var safe = await _cashbookRepo.GetSafeWithLockAsync(accountId, ct)
                ?? throw new NotFoundException("Không tìm thấy két sắt.");

            safe.CurrentBalance += amount;
            branchId = safe.BranchId;
        }
        else if (accountType == FinanceAccountType.Bank)
        {
            var bank = await _cashbookRepo.GetBankAccountWithLockAsync(accountId, ct)
                ?? throw new NotFoundException("Không tìm thấy tài khoản ngân hàng.");

            bank.CurrentBalance += amount;
        }

        var entry = await RecordEntryInternalAsync(accountId, accountType, branchId, CashbookEntryType.Receipt, amount, sourceType, sourceId, referenceCode, description, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return MapEntryResponse(entry);
    }

    public async Task<List<CashbookEntryResponse>> GetEntriesAsync(Guid? accountId, Guid? branchId, DateTime? fromDate, DateTime? toDate, int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        var entries = await _cashbookRepo.FindAsync(e =>
            (!accountId.HasValue || e.AccountId == accountId.Value) &&
            (!branchId.HasValue || e.BranchId == branchId.Value) &&
            (!fromDate.HasValue || e.OccurredAtUtc >= fromDate.Value) &&
            (!toDate.HasValue || e.OccurredAtUtc <= toDate.Value), ct);

        return entries
            .OrderByDescending(e => e.OccurredAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(MapEntryResponse)
            .ToList();
    }

    public async Task<BranchCashTill> GetOrCreateTillAsync(Guid branchId, string tillName, CancellationToken ct = default)
    {
        var existingList = await _tillRepo.FindAsync(t => t.BranchId == branchId && t.TillName == tillName, ct);
        var existing = existingList.FirstOrDefault();
        if (existing != null) return existing;

        var till = new BranchCashTill
        {
            BranchId = branchId,
            TillName = tillName,
            CurrentBalance = 0,
            IsActive = true
        };

        await _tillRepo.AddAsync(till, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        return till;
    }

    public async Task<BranchSafe> GetOrCreateSafeAsync(Guid branchId, CancellationToken ct = default)
    {
        var existingList = await _safeRepo.FindAsync(s => s.BranchId == branchId, ct);
        var existing = existingList.FirstOrDefault();
        if (existing != null) return existing;

        var safe = new BranchSafe
        {
            BranchId = branchId,
            SafeName = "Két Sắt Chi Nhánh",
            CurrentBalance = 0
        };

        await _safeRepo.AddAsync(safe, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        return safe;
    }

    private async Task<CashbookEntry> RecordEntryInternalAsync(Guid accountId, FinanceAccountType accountType, Guid? branchId, CashbookEntryType entryType, decimal amount, string sourceType, Guid? sourceId, string referenceCode, string description, CancellationToken ct)
    {
        var prevHash = await _cashbookRepo.GetLastAuditHashAsync(accountId, ct);

        decimal balanceAfter = 0;
        if (accountType == FinanceAccountType.Till)
        {
            var till = await _tillRepo.GetByIdAsync(accountId, ct);
            balanceAfter = till?.CurrentBalance ?? 0;
        }
        else if (accountType == FinanceAccountType.Safe)
        {
            var safe = await _safeRepo.GetByIdAsync(accountId, ct);
            balanceAfter = safe?.CurrentBalance ?? 0;
        }
        else if (accountType == FinanceAccountType.Bank)
        {
            var bank = await _bankRepo.GetByIdAsync(accountId, ct);
            balanceAfter = bank?.CurrentBalance ?? 0;
        }

        var entry = new CashbookEntry
        {
            EntryCode = "CB-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"),
            AccountId = accountId,
            AccountType = accountType,
            BranchId = branchId,
            EntryType = entryType,
            Amount = amount,
            BalanceAfter = balanceAfter,
            SourceType = sourceType,
            SourceId = sourceId,
            ReferenceCode = referenceCode,
            Description = description,
            OccurredAtUtc = DateTime.UtcNow
        };

        entry.AuditHash = _auditHashService.ComputeEntryHash(prevHash, entry);
        await _cashbookRepo.AddAsync(entry, ct);

        return entry;
    }

    private static ShiftResponse MapShiftResponse(ShiftZReport s) =>
        new(
            s.Id,
            s.TillId,
            s.CashierUserId,
            s.OpeningFloat,
            s.TotalCashIn,
            s.TotalCashOut,
            s.ExpectedCash,
            s.ActualCashCounted,
            s.Discrepancy,
            s.RetainedFloatAmount,
            s.DroppedToSafeAmount,
            s.Status.ToString(),
            s.CreatedAt,
            s.ClosedAtUtc
        );

    private static CashbookEntryResponse MapEntryResponse(CashbookEntry e) =>
        new(
            e.Id,
            e.EntryCode,
            e.AccountId,
            e.AccountType.ToString(),
            e.BranchId,
            e.EntryType.ToString(),
            e.Amount,
            e.BalanceAfter,
            e.SourceType,
            e.SourceId,
            e.ReferenceCode,
            e.AuditHash,
            e.Description,
            e.OccurredAtUtc
        );
}
