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
    private readonly IRepository<FinanceAccount> _accountRepo;
    private readonly IRepository<CashInTransit> _transitRepo;
    private readonly IRepository<ShiftZReport> _shiftRepo;
    private readonly IAuditHashService _auditHashService;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitOfWork _unitOfWork;

    public CashbookService(
        ICashbookRepository cashbookRepo,
        IRepository<FinanceAccount> accountRepo,
        IRepository<CashInTransit> transitRepo,
        IRepository<ShiftZReport> shiftRepo,
        IAuditHashService auditHashService,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork)
    {
        _cashbookRepo = cashbookRepo;
        _accountRepo = accountRepo;
        _transitRepo = transitRepo;
        _shiftRepo = shiftRepo;
        _auditHashService = auditHashService;
        _tenantContext = tenantContext;
        _unitOfWork = unitOfWork;
    }

    public async Task<ShiftResponse> OpenShiftAsync(OpenShiftRequest request, CancellationToken ct = default)
    {
        var till = await _cashbookRepo.GetAccountWithLockAsync(request.TillId, ct)
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

        var till = await _cashbookRepo.GetAccountWithLockAsync(shift.TillId, ct)
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
        decimal discrepancy = shift.Discrepancy ?? 0m;
        if (discrepancy > 2000)
        {
            await RecordEntryInternalAsync(till.Id, FinanceAccountType.Till, till.BranchId, CashbookEntryType.Receipt, discrepancy, "CASH_OVER_SUSPENSE", shift.Id, "SHF-" + shift.Id.ToString()[..8], "Thừa tiền chốt ca chờ chủ xử lý", ct);
        }
        else if (discrepancy < -2000)
        {
            await RecordEntryInternalAsync(till.Id, FinanceAccountType.Till, till.BranchId, CashbookEntryType.Payment, Math.Abs(discrepancy), "CASH_SHORTAGE_PENDING", shift.Id, "SHF-" + shift.Id.ToString()[..8], "Thiếu tiền chốt ca chờ xử lý bồi hoàn", ct);
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

        var till = await _cashbookRepo.GetAccountWithLockAsync(shift.TillId, ct)
            ?? throw new NotFoundException("Không tìm thấy két quầy.");

        if (till.CurrentBalance < request.Amount)
        {
            throw new InsufficientStockException($"Két quầy không đủ tiền để cash drop (Hiện có: {till.CurrentBalance:N0}đ).");
        }

        var safe = await GetOrCreateSafeAsync(till.BranchId ?? Guid.Empty, ct);
        var lockedSafe = await _cashbookRepo.GetAccountWithLockAsync(safe.Id, ct) ?? safe;

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

        var safe = await _cashbookRepo.GetAccountWithLockAsync(request.SourceAccountId, ct)
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

        var bankAccount = await _cashbookRepo.GetAccountWithLockAsync(transit.TargetBankAccountId, ct)
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

        var shouldManageTransaction = !_unitOfWork.HasActiveTransaction;
        if (shouldManageTransaction)
        {
            await _unitOfWork.BeginTransactionAsync(ct);
        }

        try
        {
            var account = await _cashbookRepo.GetAccountWithLockAsync(accountId, ct)
                ?? throw new NotFoundException("Không tìm thấy tài khoản quỹ.");

            if (account.CurrentBalance < amount)
            {
                throw new InsufficientStockException($"Tài khoản không đủ số dư để chi (Còn {account.CurrentBalance:N0}đ, Yêu cầu {amount:N0}đ).");
            }

            account.CurrentBalance -= amount;
            var branchId = account.BranchId;

            if (account.AccountType == FinanceAccountType.Till && account.ActiveShiftId.HasValue)
            {
                var shift = await _shiftRepo.GetByIdAsync(account.ActiveShiftId.Value, ct);
                if (shift != null) shift.TotalCashOut += amount;
            }

            var entry = await RecordEntryInternalAsync(accountId, account.AccountType, branchId, CashbookEntryType.Payment, amount, sourceType, sourceId, referenceCode, description, ct);
            await _unitOfWork.SaveChangesAsync(ct);

            if (shouldManageTransaction)
            {
                await _unitOfWork.CommitTransactionAsync(ct);
            }

            return MapEntryResponse(entry);
        }
        catch
        {
            if (shouldManageTransaction)
            {
                await _unitOfWork.RollbackTransactionAsync(ct);
            }
            throw;
        }
    }

    public async Task<CashbookEntryResponse> ReceiveCashAsync(Guid accountId, FinanceAccountType accountType, decimal amount, string sourceType, Guid? sourceId, string referenceCode, string description, CancellationToken ct = default)
    {
        if (amount <= 0)
        {
            throw new BusinessException("INVALID_AMOUNT", "Số tiền thu phải lớn hơn 0.");
        }

        var shouldManageTransaction = !_unitOfWork.HasActiveTransaction;
        if (shouldManageTransaction)
        {
            await _unitOfWork.BeginTransactionAsync(ct);
        }

        try
        {
            var account = await _cashbookRepo.GetAccountWithLockAsync(accountId, ct)
                ?? throw new NotFoundException("Không tìm thấy tài khoản quỹ.");

            account.CurrentBalance += amount;
            var branchId = account.BranchId;

            if (account.AccountType == FinanceAccountType.Till && account.ActiveShiftId.HasValue)
            {
                var shift = await _shiftRepo.GetByIdAsync(account.ActiveShiftId.Value, ct);
                if (shift != null) shift.TotalCashIn += amount;
            }

            var entry = await RecordEntryInternalAsync(accountId, account.AccountType, branchId, CashbookEntryType.Receipt, amount, sourceType, sourceId, referenceCode, description, ct);
            await _unitOfWork.SaveChangesAsync(ct);

            if (shouldManageTransaction)
            {
                await _unitOfWork.CommitTransactionAsync(ct);
            }

            return MapEntryResponse(entry);
        }
        catch
        {
            if (shouldManageTransaction)
            {
                await _unitOfWork.RollbackTransactionAsync(ct);
            }
            throw;
        }
    }

    public async Task<List<CashbookEntryResponse>> GetEntriesAsync(
        Guid? accountId,
        Guid? branchId,
        DateTime? fromDate,
        DateTime? toDate,
        int page = 1,
        int pageSize = 50,
        CashbookEntryType? entryType = null,
        string? sourceType = null,
        string? keyword = null,
        CancellationToken ct = default)
    {
        var kw = keyword?.Trim().ToLowerInvariant();
        var isGuid = Guid.TryParse(kw, out var searchGuid);

        var entries = await _cashbookRepo.FindAsync(e =>
            (!accountId.HasValue || e.AccountId == accountId.Value) &&
            (!branchId.HasValue || e.BranchId == branchId.Value || (e.AccountType == FinanceAccountType.Bank && e.BranchId == null)) &&
            (!fromDate.HasValue || e.OccurredAtUtc >= fromDate.Value) &&
            (!toDate.HasValue || e.OccurredAtUtc <= toDate.Value) &&
            (!entryType.HasValue || e.EntryType == entryType.Value) &&
            (string.IsNullOrEmpty(sourceType) || e.SourceType == sourceType) &&
            (string.IsNullOrEmpty(kw) ||
             e.Description.ToLower().Contains(kw) ||
             e.ReferenceCode.ToLower().Contains(kw) ||
             e.EntryCode.ToLower().Contains(kw) ||
             (isGuid && e.SourceId == searchGuid)),
            ct);

        return entries
            .OrderByDescending(e => e.OccurredAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(MapEntryResponse)
            .ToList();
    }

    public async Task<FinanceAccount> GetOrCreateTillAsync(Guid branchId, string tillName, CancellationToken ct = default)
    {
        var existingList = await _accountRepo.FindAsync(t => t.BranchId == branchId && t.AccountType == FinanceAccountType.Till, ct);
        var existing = existingList.FirstOrDefault(t => t.AccountName == tillName) ?? existingList.FirstOrDefault();
        if (existing != null) return existing;

        var till = new FinanceAccount
        {
            BranchId = branchId,
            AccountType = FinanceAccountType.Till,
            AccountName = tillName,
            CurrentBalance = 0,
            IsActive = true
        };

        await _accountRepo.AddAsync(till, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        return till;
    }

    public async Task<FinanceAccount> GetOrCreateSafeAsync(Guid branchId, CancellationToken ct = default)
    {
        var existingList = await _accountRepo.FindAsync(s => s.BranchId == branchId && s.AccountType == FinanceAccountType.Safe, ct);
        var existing = existingList.FirstOrDefault();
        if (existing != null) return existing;

        var safe = new FinanceAccount
        {
            BranchId = branchId,
            AccountType = FinanceAccountType.Safe,
            AccountName = "Két Sắt Chi Nhánh",
            CurrentBalance = 0,
            IsActive = true
        };

        await _accountRepo.AddAsync(safe, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        return safe;
    }

    public async Task<FinanceAccount> GetOrCreateBankAccountAsync(
        Guid? branchId = null,
        string? bankName = null,
        string? accountNumber = null,
        string? accountHolderName = null,
        CancellationToken ct = default)
    {
        var existingBanks = await _accountRepo.FindAsync(b => b.AccountType == FinanceAccountType.Bank && !b.IsDeleted, ct);
        var matchedBank = existingBanks
            .Where(b => b.IsActive && (!branchId.HasValue || !b.BranchId.HasValue || b.BranchId == branchId.Value))
            .FirstOrDefault()
            ?? existingBanks.FirstOrDefault(b => b.IsActive)
            ?? existingBanks.FirstOrDefault();

        if (matchedBank != null)
        {
            if (!matchedBank.IsActive)
            {
                matchedBank.IsActive = true;
                _accountRepo.Update(matchedBank);
                await _unitOfWork.SaveChangesAsync(ct);
            }
            return matchedBank;
        }

        var resolvedName = string.IsNullOrWhiteSpace(bankName) ? "Tài khoản Ngân hàng cửa hàng" : bankName.Trim();
        var bank = new FinanceAccount
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.CurrentTenantId ?? string.Empty,
            BranchId = null,
            AccountType = FinanceAccountType.Bank,
            AccountName = resolvedName,
            BankName = resolvedName,
            AccountNumber = string.IsNullOrWhiteSpace(accountNumber) ? null : accountNumber.Trim(),
            AccountHolderName = string.IsNullOrWhiteSpace(accountHolderName) ? "CHỦ HỘ KINH DOANH" : accountHolderName.Trim(),
            CurrentBalance = 0m,
            IsActive = true
        };

        await _accountRepo.AddAsync(bank, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        return bank;
    }

    public async Task<List<CashAccountResponse>> GetAccountsAsync(Guid? branchId = null, CancellationToken ct = default)
    {
        var accounts = await _accountRepo.GetAllAsync(ct);
        var defaultBranchId = accounts.FirstOrDefault(a => a.BranchId.HasValue)?.BranchId ?? branchId ?? Guid.NewGuid();
        var currentTenantId = _tenantContext.CurrentTenantId ?? string.Empty;

        if (accounts.Count == 0)
        {
            var initTill = new FinanceAccount
            {
                Id = Guid.NewGuid(),
                TenantId = currentTenantId,
                BranchId = defaultBranchId,
                AccountType = FinanceAccountType.Till,
                AccountName = "Két tiền mặt quầy chính",
                CurrentBalance = 0m,
                IsActive = true
            };
            var initSafe = new FinanceAccount
            {
                Id = Guid.NewGuid(),
                TenantId = currentTenantId,
                BranchId = defaultBranchId,
                AccountType = FinanceAccountType.Safe,
                AccountName = "Két sắt chủ shop (Safe Deposit)",
                CurrentBalance = 0m,
                IsActive = true
            };
            var initBank = new FinanceAccount
            {
                Id = Guid.NewGuid(),
                TenantId = currentTenantId,
                AccountType = FinanceAccountType.Bank,
                AccountName = "Tài khoản Ngân hàng cửa hàng",
                BankName = "Tài khoản Ngân hàng cửa hàng",
                AccountHolderName = "CHỦ HỘ KINH DOANH",
                CurrentBalance = 0m,
                IsActive = true
            };

            await _accountRepo.AddAsync(initTill, ct);
            await _accountRepo.AddAsync(initSafe, ct);
            await _accountRepo.AddAsync(initBank, ct);
            await _unitOfWork.SaveChangesAsync(ct);
            accounts = await _accountRepo.GetAllAsync(ct);
        }
        else if (!accounts.Any(a => a.AccountType == FinanceAccountType.Bank && !a.IsDeleted))
        {
            await GetOrCreateBankAccountAsync(branchId, ct: ct);
            accounts = await _accountRepo.GetAllAsync(ct);
        }

        var results = new List<CashAccountResponse>();

        foreach (var till in accounts.Where(a => a.AccountType == FinanceAccountType.Till && (!branchId.HasValue || a.BranchId == branchId.Value)))
        {
            results.Add(new CashAccountResponse(
                till.Id,
                till.BranchId,
                "Chi nhánh chính",
                till.AccountName,
                "CASH",
                null,
                null,
                till.CurrentBalance,
                till.CurrentBalance,
                till.IsActive,
                "VND",
                false
            ));
        }

        foreach (var safe in accounts.Where(a => a.AccountType == FinanceAccountType.Safe && (!branchId.HasValue || a.BranchId == branchId.Value)))
        {
            results.Add(new CashAccountResponse(
                safe.Id,
                safe.BranchId,
                "Toàn hệ thống (Chủ cửa hàng)",
                safe.AccountName,
                "CASH",
                null,
                null,
                safe.CurrentBalance,
                safe.CurrentBalance,
                safe.IsActive,
                "VND",
                true
            ));
        }

        foreach (var bank in accounts.Where(a => a.AccountType == FinanceAccountType.Bank && a.IsActive))
        {
            results.Add(new CashAccountResponse(
                bank.Id,
                null,
                "Toàn hệ thống",
                $"{bank.BankName} - TK {bank.AccountNumber}",
                "BANK",
                bank.BankName,
                bank.AccountNumber,
                bank.CurrentBalance,
                bank.CurrentBalance,
                bank.IsActive,
                "VND",
                false
            ));
        }

        return results;
    }

    public async Task<CashbookSummaryResponse> GetSummaryAsync(Guid? branchId = null, CancellationToken ct = default)
    {
        var accounts = await GetAccountsAsync(branchId, ct);
        var accountIds = accounts.Select(a => a.Id).ToHashSet();
        var entries = await _cashbookRepo.FindAsync(
            e => accountIds.Contains(e.AccountId) || (!branchId.HasValue || e.BranchId == branchId.Value),
            ct);

        decimal totalCashInDrawers = accounts.Where(a => a.AccountType == "CASH" && !a.IsSafeDeposit).Sum(a => a.CurrentBalance);
        decimal totalSafeDepositBalance = accounts.Where(a => a.IsSafeDeposit).Sum(a => a.CurrentBalance);
        decimal totalBankDeposits = accounts.Where(a => a.AccountType == "BANK").Sum(a => a.CurrentBalance);

        decimal totalReceipts = entries.Where(e => e.EntryType == CashbookEntryType.Receipt).Sum(e => e.Amount);
        decimal totalPayments = entries.Where(e => e.EntryType == CashbookEntryType.Payment).Sum(e => e.Amount);

        decimal totalClosingBalance = totalCashInDrawers + totalSafeDepositBalance + totalBankDeposits;
        decimal totalOpeningBalance = totalClosingBalance - totalReceipts + totalPayments;

        return new CashbookSummaryResponse(
            totalOpeningBalance,
            totalReceipts,
            totalPayments,
            totalClosingBalance,
            totalCashInDrawers,
            totalBankDeposits,
            totalSafeDepositBalance
        );
    }

    private async Task<FinanceAccountType> ResolveAccountTypeAsync(Guid accountId, CancellationToken ct)
    {
        var acc = await _accountRepo.GetByIdAsync(accountId, ct);
        if (acc != null) return acc.AccountType;
        throw new NotFoundException($"Không tìm thấy tài khoản quỹ với Id: {accountId}");
    }

    public async Task<CashbookEntryResponse> CreateReceiptAsync(CreateReceiptRequest request, CancellationToken ct = default)
    {
        if (request.Amount <= 0)
        {
            throw new BusinessException("INVALID_AMOUNT", "Số tiền thu phải lớn hơn 0.");
        }

        var accountType = await ResolveAccountTypeAsync(request.AccountId, ct);
        var refCode = string.IsNullOrWhiteSpace(request.ReferenceCode)
            ? "PT-" + DateTime.UtcNow.ToString("yyyyMMdd") + "-" + Random.Shared.Next(100, 999)
            : request.ReferenceCode.Trim();

        var description = string.IsNullOrWhiteSpace(request.Description)
            ? request.Category
            : request.Description.Trim();

        return await ReceiveCashAsync(request.AccountId, accountType, request.Amount, "MANUAL", null, refCode, description, ct);
    }

    public async Task<CashbookEntryResponse> CreatePaymentAsync(CreatePaymentRequest request, CancellationToken ct = default)
    {
        if (request.Amount <= 0)
        {
            throw new BusinessException("INVALID_AMOUNT", "Số tiền chi phải lớn hơn 0.");
        }

        var account = await _accountRepo.GetByIdAsync(request.AccountId, ct)
            ?? throw new NotFoundException("Không tìm thấy tài khoản quỹ.");

        // Quy tắc Poka-yoke GBR-CSH-03: Chặn âm két tiền mặt
        if (account.AccountType == FinanceAccountType.Till && request.Amount > account.CurrentBalance)
        {
            throw new BusinessException("INSUFFICIENT_FUNDS", $"Số tiền chi ({request.Amount:N0} đ) vượt quá số dư két hiện có ({account.CurrentBalance:N0} đ). Quy tắc Poka-yoke GBR-CSH-03 không cho phép âm két tiền mặt.");
        }
        else if (account.AccountType == FinanceAccountType.Safe && request.Amount > account.CurrentBalance)
        {
            throw new BusinessException("INSUFFICIENT_FUNDS", $"Số tiền chi ({request.Amount:N0} đ) vượt quá số dư két sắt hiện có ({account.CurrentBalance:N0} đ).");
        }

        var refCode = string.IsNullOrWhiteSpace(request.ReferenceCode)
            ? "PC-" + DateTime.UtcNow.ToString("yyyyMMdd") + "-" + Random.Shared.Next(100, 999)
            : request.ReferenceCode.Trim();

        var description = string.IsNullOrWhiteSpace(request.Description)
            ? request.Category
            : request.Description.Trim();

        return await DisburseCashAsync(request.AccountId, account.AccountType, request.Amount, "MANUAL", null, refCode, description, ct);
    }

    private async Task LockAccountByIdAsync(Guid accountId, CancellationToken ct)
    {
        await _cashbookRepo.GetAccountWithLockAsync(accountId, ct);
    }

    public async Task<TransferFundResponse> TransferFundsAsync(TransferFundRequest request, CancellationToken ct = default)
    {
        if (request.Amount <= 0)
        {
            throw new BusinessException("INVALID_AMOUNT", "Số tiền chuyển quỹ phải lớn hơn 0.");
        }
        if (request.FromAccountId == request.ToAccountId)
        {
            throw new BusinessException("INVALID_ACCOUNTS", "Tài khoản nguồn và tài khoản đích không được trùng nhau.");
        }

        var fromType = await ResolveAccountTypeAsync(request.FromAccountId, ct);
        var toType = await ResolveAccountTypeAsync(request.ToAccountId, ct);

        // Quy tắc Deterministic Resource Ordering: Luôn khóa 2 tài khoản theo thứ tự ID xác định cố định
        var firstId = request.FromAccountId.CompareTo(request.ToAccountId) < 0 ? request.FromAccountId : request.ToAccountId;
        var secondId = request.FromAccountId.CompareTo(request.ToAccountId) < 0 ? request.ToAccountId : request.FromAccountId;

        TransferFundResponse? response = null;
        await _unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            await LockAccountByIdAsync(firstId, token);
            await LockAccountByIdAsync(secondId, token);

            var dateStr = DateTime.UtcNow.ToString("yyyyMMdd");
            var rand = Random.Shared.Next(100, 999);
            var payRefCode = $"PC-TRF-{dateStr}-{rand}";
            var recRefCode = $"PT-TRF-{dateStr}-{rand}";
            var note = string.IsNullOrWhiteSpace(request.Note) ? "Chuyển tiền nội bộ" : request.Note.Trim();

            var payEntry = await DisburseCashAsync(request.FromAccountId, fromType, request.Amount, "INTERNAL_TRANSFER", null, payRefCode, note, token);
            var recEntry = await ReceiveCashAsync(request.ToAccountId, toType, request.Amount, "INTERNAL_TRANSFER", payEntry.Id, recRefCode, note, token);

            response = new TransferFundResponse(payEntry, recEntry);
        }, ct);

        return response!;
    }

    public async Task<CashbookEntryResponse?> ReconcileCashAccountAsync(ReconcileAccountRequest request, CancellationToken ct = default)
    {
        ValidateFinancialManagementRole();
        var account = await _accountRepo.GetByIdAsync(request.AccountId, ct)
            ?? throw new NotFoundException("Không tìm thấy tài khoản quỹ.");

        var diff = request.ActualCountedBalance - account.CurrentBalance;

        if (diff != 0 && string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new BusinessException("REASON_REQUIRED", "Chênh lệch kiểm kê bắt buộc phải có lý do giải trình cụ thể theo quy tắc GBR-CSH-04.");
        }

        if (diff == 0)
        {
            return null;
        }

        var refCode = "KK-" + DateTime.UtcNow.ToString("yyyyMMdd") + "-" + Random.Shared.Next(100, 999);

        if (diff > 0)
        {
            return await ReceiveCashAsync(request.AccountId, account.AccountType, diff, "ADJUSTMENT", null, refCode, $"Điều chỉnh thừa kiểm kê: {request.Reason.Trim()} (+{diff:N0} đ)", ct);
        }
        else
        {
            return await DisburseCashAsync(request.AccountId, account.AccountType, Math.Abs(diff), "ADJUSTMENT", null, refCode, $"Điều chỉnh thiếu kiểm kê: {request.Reason.Trim()} ({diff:N0} đ)", ct);
        }
    }

    public async Task<CashbookEntryResponse> VoidTransactionAsync(Guid entryId, VoidEntryRequest request, CancellationToken ct = default)
    {
        ValidateFinancialManagementRole();
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new BusinessException("REASON_REQUIRED", "Lý do gạch bỏ bút toán là bắt buộc.");
        }

        var origEntry = await _cashbookRepo.GetByIdAsync(entryId, ct)
            ?? throw new NotFoundException($"Không tìm thấy bút toán sổ cái với Id: {entryId}");

        var existingCompList = await _cashbookRepo.FindAsync(e => e.SourceType == "COMPENSATING" && e.SourceId == entryId, ct);
        if (existingCompList.Count > 0)
        {
            throw new BusinessException("ALREADY_VOIDED", "Bút toán này đã được gạch bỏ trước đó.");
        }

        var dateStr = DateTime.UtcNow.ToString("yyyyMMdd");
        var rand = Random.Shared.Next(100, 999);
        var compRefCode = $"GB-{dateStr}-{rand}";
        var compDescription = $"[BÚT TOÁN GẠCH BỎ] Lý do: {request.Reason.Trim()}. Bù trừ cho phiếu gốc {origEntry.EntryCode}";

        if (origEntry.EntryType == CashbookEntryType.Receipt)
        {
            return await DisburseCashAsync(origEntry.AccountId, origEntry.AccountType, origEntry.Amount, "COMPENSATING", origEntry.Id, compRefCode, compDescription, ct);
        }
        else
        {
            return await ReceiveCashAsync(origEntry.AccountId, origEntry.AccountType, origEntry.Amount, "COMPENSATING", origEntry.Id, compRefCode, compDescription, ct);
        }
    }

    private async Task<CashbookEntry> RecordEntryInternalAsync(Guid accountId, FinanceAccountType accountType, Guid? branchId, CashbookEntryType entryType, decimal amount, string sourceType, Guid? sourceId, string referenceCode, string description, CancellationToken ct)
    {
        var prevHash = await _cashbookRepo.GetLastAuditHashAsync(accountId, ct);
        var acc = await _accountRepo.GetByIdAsync(accountId, ct);
        decimal balanceAfter = acc?.CurrentBalance ?? 0;

        var entry = new CashbookEntry
        {
            TenantId = _tenantContext.CurrentTenantId,
            EntryCode = "CB-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"),
            AccountId = accountId,
            AccountType = accountType,
            BranchId = branchId,
            EntryType = entryType,
            Amount = amount,
            BalanceAfter = balanceAfter,
            SourceType = sourceType,
            SourceId = sourceId,
            ReversedEntryId = (sourceType == "COMPENSATING" || sourceType == "REVERSAL") ? sourceId : null,
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

    private void ValidateFinancialManagementRole()
    {
        var role = _tenantContext.CurrentRole;
        if (!string.IsNullOrWhiteSpace(role) &&
            !role.Equals("Owner", StringComparison.OrdinalIgnoreCase) &&
            !role.Equals("Store_Owner", StringComparison.OrdinalIgnoreCase) &&
            !role.Equals("StoreOwner", StringComparison.OrdinalIgnoreCase) &&
            !role.Equals("Admin", StringComparison.OrdinalIgnoreCase) &&
            !role.Equals("Manager", StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Chỉ Chủ hộ kinh doanh hoặc Quản lý cửa hàng mới có thẩm quyền thực hiện thao tác tài chính này.");
        }
    }

    public async Task<bool> VerifyChainIntegrityAsync(Guid accountId, CancellationToken ct = default)
    {
        var entries = await _cashbookRepo.FindAsync(e => e.AccountId == accountId, ct);
        var sortedEntries = entries
            .OrderBy(e => e.OccurredAtUtc)
            .ThenBy(e => e.CreatedAt)
            .ToList();

        if (sortedEntries.Count == 0) return true;

        string prevHash = "GENESIS_HASH";
        foreach (var entry in sortedEntries)
        {
            if (!_auditHashService.VerifyEntryHash(prevHash, entry))
            {
                return false;
            }
            prevHash = entry.AuditHash;
        }

        return true;
    }
}
