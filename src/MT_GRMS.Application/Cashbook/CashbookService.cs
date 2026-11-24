using MT_GRMS.Application.Cashbook.Dtos;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Common.Services;
using MT_GRMS.Application.Notifications;
using MT_GRMS.Application.Tax;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Domain.Exceptions;

namespace MT_GRMS.Application.Cashbook;

public class CashbookService : ICashbookService
{
    private sealed record EntryClassification(
        string CategoryCode,
        FinancialNature FinancialNature,
        CashbookTaxTreatment TaxTreatment,
        string? TaxTreatmentReason = null);

    private readonly ICashbookRepository _cashbookRepo;
    private readonly IRepository<FinanceAccount> _accountRepo;
    private readonly IRepository<ShiftZReport> _shiftRepo;
    private readonly IAuditHashService _auditHashService;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IRepository<ExpenseVoucher>? _expenseVoucherRepo;
    private readonly IRepository<Branch>? _branchRepo;
    private readonly IHouseholdTaxRepository? _taxRepository;
    private readonly NotificationOutbox? _notificationOutbox;

    public CashbookService(
        ICashbookRepository cashbookRepo,
        IRepository<FinanceAccount> accountRepo,
        IRepository<ShiftZReport> shiftRepo,
        IAuditHashService auditHashService,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork,
        IRepository<ExpenseVoucher>? expenseVoucherRepo = null,
        IHouseholdTaxRepository? taxRepository = null,
        IRepository<Branch>? branchRepo = null,
        NotificationOutbox? notificationOutbox = null)
    {
        _cashbookRepo = cashbookRepo;
        _accountRepo = accountRepo;
        _branchRepo = branchRepo;
        _shiftRepo = shiftRepo;
        _auditHashService = auditHashService;
        _tenantContext = tenantContext;
        _unitOfWork = unitOfWork;
        _expenseVoucherRepo = expenseVoucherRepo;
        _taxRepository = taxRepository;
        _notificationOutbox = notificationOutbox;
    }

    public async Task<ShiftResponse> OpenShiftAsync(OpenShiftRequest request, CancellationToken ct = default)
    {
        RequireOwner();
        return await _unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var till = await _cashbookRepo.GetAccountWithLockAsync(request.TillId, token)
                ?? throw new NotFoundException($"Không tìm thấy két quầy với Id: {request.TillId}");
            if (till.AccountType != FinanceAccountType.Till || !till.BranchId.HasValue || !till.IsActive || till.IsDeleted)
                throw new BusinessException("INVALID_TILL", "Két quầy không hợp lệ hoặc không còn hoạt động.");

            var activeShifts = await _shiftRepo.FindAsync(
                s => s.BranchId == till.BranchId.Value &&
                     (s.Status == ShiftState.Open || s.Status == ShiftState.Closing) && !s.IsDeleted, token);
            if (activeShifts.Any())
            {
                throw new BusinessException("ACTIVE_SHIFT_EXISTS", "Chi nhánh đang có ca làm việc chưa chốt. Vui lòng chốt ca trước.");
            }

            var lastClosed = await _cashbookRepo.GetLastClosedShiftByTillIdAsync(request.TillId, token);
            var expectedOpeningFloat = lastClosed?.RetainedFloatAmount ?? till.CurrentBalance;

            var shift = new ShiftZReport
            {
                TenantId = _tenantContext.CurrentTenantId ?? string.Empty,
                BranchId = till.BranchId.Value,
                TillId = request.TillId,
                CashierUserId = _tenantContext.CurrentUserId != Guid.Empty ? _tenantContext.CurrentUserId : Guid.NewGuid(),
                OpeningFloat = expectedOpeningFloat,
                ExpectedCash = expectedOpeningFloat,
                ActualCashCounted = request.CountedFloat,
                Discrepancy = request.CountedFloat - expectedOpeningFloat,
                RetainedFloatAmount = 0,
                DroppedToSafeAmount = 0,
                Status = ShiftState.Open,
                Notes = request.Notes
            };

            await _shiftRepo.AddAsync(shift, token);
            till.ActiveShiftId = shift.Id;
            till.CurrentBalance = request.CountedFloat;
            await _unitOfWork.SaveChangesAsync(token);

            return MapShiftResponse(shift);
        }, ct);
    }

    public async Task<ShiftResponse> CloseShiftZReportAsync(CloseShiftRequest request, CancellationToken ct = default)
    {
        RequireOwner();
        return await _unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var shift = await _shiftRepo.GetByIdAsync(request.ShiftId, token)
                ?? throw new NotFoundException($"Không tìm thấy ca làm việc: {request.ShiftId}");

            if (shift.Status is ShiftState.Closed or ShiftState.ForceClosed)
            {
                throw new BusinessException("SHIFT_ALREADY_CLOSED", "Ca làm việc này đã được chốt trước đó.");
            }

            var till = await _cashbookRepo.GetAccountWithLockAsync(shift.TillId, token)
                ?? throw new NotFoundException($"Không tìm thấy két quầy: {shift.TillId}");

            shift.ExpectedCash = shift.OpeningFloat + shift.TotalCashIn - shift.TotalCashOut;
            shift.ActualCashCounted = request.ActualCashCounted;
            shift.Discrepancy = request.ActualCashCounted - shift.ExpectedCash;
            shift.RetainedFloatAmount = request.RetainedFloatAmount;
            shift.DroppedToSafeAmount = Math.Max(0, request.ActualCashCounted - request.RetainedFloatAmount);
            shift.ClosedAtUtc = DateTime.UtcNow;
            shift.Status = ShiftState.Closed;
            shift.Notes = request.Notes;

            // Cập nhật số dư thực tế theo kiểm kê trước khi ghi nhận chênh lệch để BalanceAfter phản ánh đúng
            decimal discrepancy = shift.Discrepancy ?? 0m;
            till.CurrentBalance = request.ActualCashCounted;
            if (discrepancy > 2000)
            {
                await RecordEntryInternalAsync(till, till.BranchId, CashbookEntryType.Receipt, discrepancy, "CASH_OVER_SUSPENSE", shift.Id, "SHF-" + shift.Id.ToString()[..8], "Thừa tiền chốt ca chờ chủ xử lý", token);
            }
            else if (discrepancy < -2000)
            {
                await RecordEntryInternalAsync(till, till.BranchId, CashbookEntryType.Payment, Math.Abs(discrepancy), "CASH_SHORTAGE_PENDING", shift.Id, "SHF-" + shift.Id.ToString()[..8], "Thiếu tiền chốt ca chờ xử lý bồi hoàn", token);
            }

            // Chuyển phần tiền bàn giao về két sắt chi nhánh thay vì làm bốc hơi khỏi hệ thống
            if (shift.DroppedToSafeAmount > 0m && till.BranchId.HasValue)
            {
                var safe = await GetOrCreateSafeAsync(till.BranchId.Value, token);
                var lockedSafe = await _cashbookRepo.GetAccountWithLockAsync(safe.Id, token) ?? safe;
                till.CurrentBalance = request.RetainedFloatAmount;
                lockedSafe.CurrentBalance += shift.DroppedToSafeAmount;
                var dropRef = "SHF-DROP-" + shift.Id.ToString()[..8];
                await RecordEntryInternalAsync(till, till.BranchId, CashbookEntryType.Payment, shift.DroppedToSafeAmount, "CASH_DROP", shift.Id, dropRef, "Bàn giao tiền chốt ca sang két sắt", token);
                await RecordEntryInternalAsync(lockedSafe, till.BranchId, CashbookEntryType.Receipt, shift.DroppedToSafeAmount, "CASH_DROP", shift.Id, dropRef, "Nhận tiền bàn giao chốt ca từ két quầy", token);
            }
            else
            {
                till.CurrentBalance = request.RetainedFloatAmount;
            }

            till.ActiveShiftId = null;
            if (Math.Abs(discrepancy) > 2000m && _notificationOutbox != null)
                await _notificationOutbox.EnqueueShiftDiscrepancyAsync(shift, discrepancy, token);
            await _unitOfWork.SaveChangesAsync(token);

            return MapShiftResponse(shift);
        }, ct);
    }

    public async Task<bool> ProcessCashDropAsync(CashDropRequest request, CancellationToken ct = default)
    {
        RequireOwner();
        if (request.Amount <= 0)
        {
            throw new BusinessException("INVALID_AMOUNT", "Số tiền cash drop phải lớn hơn 0.");
        }

        return await _unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var shift = await _shiftRepo.GetByIdAsync(request.ShiftId, token)
                ?? throw new NotFoundException("Không tìm thấy ca làm việc.");

            var till = await _cashbookRepo.GetAccountWithLockAsync(shift.TillId, token)
                ?? throw new NotFoundException("Không tìm thấy két quầy.");

            if (till.CurrentBalance < request.Amount)
            {
                throw new InsufficientStockException($"Két quầy không đủ tiền để cash drop (Hiện có: {till.CurrentBalance:N0}đ).");
            }

            var safe = await GetOrCreateSafeAsync(till.BranchId ?? Guid.Empty, token);
            var lockedSafe = await _cashbookRepo.GetAccountWithLockAsync(safe.Id, token) ?? safe;

            till.CurrentBalance -= request.Amount;
            lockedSafe.CurrentBalance += request.Amount;
            shift.DroppedToSafeAmount += request.Amount;

            await RecordEntryInternalAsync(till, till.BranchId, CashbookEntryType.Payment, request.Amount, "CASH_DROP", shift.Id, "CD-" + shift.Id.ToString()[..8], "Cash drop chuyển tiền két quầy sang két sắt", token);
            await RecordEntryInternalAsync(lockedSafe, till.BranchId, CashbookEntryType.Receipt, request.Amount, "CASH_DROP", shift.Id, "CD-" + shift.Id.ToString()[..8], "Nhận cash drop từ két quầy", token);

            await _unitOfWork.SaveChangesAsync(token);
            return true;
        }, ct);
    }

    public Task<CashbookEntryResponse> DisburseCashAsync(Guid accountId, FinanceAccountType accountType, decimal amount, string sourceType, Guid? sourceId, string referenceCode, string description, CancellationToken ct = default) =>
        DisburseCashCoreAsync(accountId, amount, sourceType, sourceId, referenceCode, description, null, ct);

    private Task<CashbookEntryResponse> DisburseCashCoreAsync(Guid accountId, decimal amount, string sourceType, Guid? sourceId, string referenceCode, string description, EntryClassification? classification, CancellationToken ct) =>
        PostCashMovementAsync(accountId, CashbookEntryType.Payment, amount, sourceType, sourceId, referenceCode, description, classification, ct);

    public Task<CashbookEntryResponse> ReceiveCashAsync(Guid accountId, FinanceAccountType accountType, decimal amount, string sourceType, Guid? sourceId, string referenceCode, string description, CancellationToken ct = default) =>
        ReceiveCashCoreAsync(accountId, amount, sourceType, sourceId, referenceCode, description, null, ct);

    private Task<CashbookEntryResponse> ReceiveCashCoreAsync(Guid accountId, decimal amount, string sourceType, Guid? sourceId, string referenceCode, string description, EntryClassification? classification, CancellationToken ct) =>
        PostCashMovementAsync(accountId, CashbookEntryType.Receipt, amount, sourceType, sourceId, referenceCode, description, classification, ct);

    private async Task<CashbookEntryResponse> PostCashMovementAsync(Guid accountId, CashbookEntryType entryType, decimal amount, string sourceType, Guid? sourceId, string referenceCode, string description, EntryClassification? classification, CancellationToken ct)
    {
        if (amount <= 0)
        {
            var movement = entryType == CashbookEntryType.Payment ? "chi" : "thu";
            throw new BusinessException("INVALID_AMOUNT", $"Số tiền {movement} phải lớn hơn 0.");
        }

        return await _unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var account = await _cashbookRepo.GetAccountWithLockAsync(accountId, token)
                ?? throw new NotFoundException("Không tìm thấy tài khoản quỹ.");
            var entry = await RecordCashMovementAsync(account, entryType, amount, sourceType, sourceId, referenceCode, description, classification, token);
            await _unitOfWork.SaveChangesAsync(token);
            return MapEntryResponse(entry);
        }, ct);
    }

    private async Task<CashbookEntry> RecordCashMovementAsync(FinanceAccount account, CashbookEntryType entryType, decimal amount, string sourceType, Guid? sourceId, string referenceCode, string description, EntryClassification? classification, CancellationToken ct)
    {
        var isPayment = entryType == CashbookEntryType.Payment;
        if (isPayment && account.CurrentBalance < amount)
        {
            throw new InsufficientStockException($"Tài khoản không đủ số dư để chi (Còn {account.CurrentBalance:N0}đ, Yêu cầu {amount:N0}đ).");
        }

        account.CurrentBalance += isPayment ? -amount : amount;
        if (account.AccountType == FinanceAccountType.Till && account.ActiveShiftId.HasValue)
        {
            var shift = await _shiftRepo.GetByIdAsync(account.ActiveShiftId.Value, ct);
            if (shift != null)
            {
                if (isPayment) shift.TotalCashOut += amount;
                else shift.TotalCashIn += amount;
            }
        }

        return await RecordEntryInternalAsync(account, account.BranchId, entryType, amount, sourceType, sourceId, referenceCode, description, classification, ct);
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
        => (await GetEntriesPageAsync(accountId, branchId, fromDate, toDate, page, pageSize, entryType, sourceType, keyword, ct: ct)).Items.ToList();

    public async Task<PagedResult<CashbookEntryResponse>> GetEntriesPageAsync(
        Guid? accountId,
        Guid? branchId,
        DateTime? fromDate,
        DateTime? toDate,
        int page = 1,
        int pageSize = 50,
        CashbookEntryType? entryType = null,
        string? sourceType = null,
        string? keyword = null,
        string? status = null, string? counterparty = null,
        CancellationToken ct = default)
    {
        RequireOwner();
        DateTime? fromUtc = fromDate.HasValue
            ? (fromDate.Value.Kind == DateTimeKind.Utc ? fromDate.Value : fromDate.Value.Kind == DateTimeKind.Local ? fromDate.Value.ToUniversalTime() : DateTime.SpecifyKind(fromDate.Value.AddHours(-7), DateTimeKind.Utc))
            : null;
        DateTime? toUtc = toDate.HasValue
            ? (toDate.Value.Kind == DateTimeKind.Utc ? toDate.Value : toDate.Value.Kind == DateTimeKind.Local ? toDate.Value.ToUniversalTime() : DateTime.SpecifyKind(toDate.Value.AddHours(-7), DateTimeKind.Utc))
            : null;
        if (fromUtc.HasValue && toUtc.HasValue && fromUtc.Value > toUtc.Value)
        {
            throw new BusinessException("INVALID_DATE_RANGE", "Ngày bắt đầu không được sau ngày kết thúc.");
        }

        var normalizedStatus = status?.Trim().ToUpperInvariant();
        var kw = keyword?.Trim().ToLowerInvariant();
        var party = counterparty?.Trim().ToLowerInvariant();
        var isGuid = Guid.TryParse(kw, out var searchGuid);

        var entries = await _cashbookRepo.FindAsync(e =>
            (!accountId.HasValue || e.AccountId == accountId.Value) &&
            (!branchId.HasValue || e.BranchId == branchId.Value || (e.AccountType == FinanceAccountType.Bank && e.BranchId == null)) &&
            (!fromUtc.HasValue || e.OccurredAtUtc >= fromUtc.Value) &&
            (!toUtc.HasValue || e.OccurredAtUtc <= toUtc.Value) &&
            (!entryType.HasValue || (e.EntryType == entryType.Value && e.SourceType != "ADJUSTMENT")) &&
            (string.IsNullOrEmpty(sourceType) || e.SourceType == sourceType) &&
            (string.IsNullOrEmpty(party) ||
             (e.ReferenceCode == "" ? e.SourceType : e.ReferenceCode).ToLower().Contains(party)) &&
            (string.IsNullOrEmpty(kw) ||
             e.Description.ToLower().Contains(kw) ||
             e.ReferenceCode.ToLower().Contains(kw) ||
             e.EntryCode.ToLower().Contains(kw) ||
             (isGuid && e.SourceId == searchGuid)),
            ct);

        var entryIds = entries.Where(e => e.SourceType != "COMPENSATING").Select(e => e.Id).ToArray();
        var voidedIds = entryIds.Length > 0
            ? (await _cashbookRepo.FindAsync(e => e.SourceType == "COMPENSATING" && e.SourceId.HasValue
                && entryIds.Contains(e.SourceId.Value), ct)).Select(e => e.SourceId!.Value).ToHashSet()
            : new HashSet<Guid>();
        var filtered = entries.Where(e => normalizedStatus switch
        {
            "VOIDED" => voidedIds.Contains(e.Id),
            "COMPENSATING" => e.SourceType == "COMPENSATING",
            "ACTIVE" => e.SourceType != "COMPENSATING" && !voidedIds.Contains(e.Id),
            _ => true
        }).ToList();

        return new PagedResult<CashbookEntryResponse>
        {
            Items = filtered.OrderByDescending(e => e.OccurredAtUtc).ThenByDescending(e => e.Id)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .Select(e => MapEntryResponse(e) with { IsVoided = voidedIds.Contains(e.Id) }).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalItems = filtered.Count
        };
    }

    public async Task<FinanceAccount> GetOrCreateTillAsync(Guid branchId, string tillName, CancellationToken ct = default)
    {
        RequireOwner();
        ValidateFinancialManagementRole();
        var existingList = await _accountRepo.FindAsync(t => t.BranchId == branchId && t.AccountType == FinanceAccountType.Till && t.IsActive && !t.IsDeleted, ct);
        var existing = existingList.OrderBy(t => t.CreatedAt).FirstOrDefault();
        if (existing != null) return existing;

        var till = new FinanceAccount
        {
            TenantId = _tenantContext.CurrentTenantId ?? string.Empty,
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
        RequireOwner();
        var existingList = await _accountRepo.FindAsync(
            s => s.BranchId == branchId && s.AccountType == FinanceAccountType.Safe && s.IsActive && !s.IsDeleted,
            ct);
        var existing = existingList.OrderBy(s => s.CreatedAt).FirstOrDefault();
        if (existing != null) return existing;

        var safe = new FinanceAccount
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.CurrentTenantId ?? string.Empty,
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

    public async Task<FinanceAccount> ResolveDisbursementAccountAsync(
        Guid branchId,
        string paymentMethod,
        Guid? cashbookAccountId = null,
        CancellationToken ct = default)
    {
        var normalized = (paymentMethod ?? string.Empty).Trim().ToUpperInvariant();
        var isBank = normalized.Contains("BANK")
            || normalized.Contains("TRANSFER")
            || normalized.Contains("VIETQR")
            || normalized == "QR";

        if (!cashbookAccountId.HasValue)
        {
            return isBank
                ? await GetOrCreateBankAccountAsync(branchId, ct: ct)
                : await GetOrCreateSafeAsync(branchId, ct);
        }

        var account = await _accountRepo.GetByIdAsync(cashbookAccountId.Value, ct);
        if (account == null || account.IsDeleted || !account.IsActive)
        {
            throw new NotFoundException("Tài khoản quỹ được chọn không tồn tại hoặc đang ngừng hoạt động.");
        }

        var currentTenantId = _tenantContext.CurrentTenantId;
        if (!string.IsNullOrEmpty(currentTenantId) && account.TenantId != currentTenantId)
        {
            throw new NotFoundException("Tài khoản quỹ được chọn không thuộc cửa hàng hiện tại.");
        }

        if (isBank && account.AccountType != FinanceAccountType.Bank)
        {
            throw new BusinessException("INVALID_CASHBOOK_ACCOUNT", "Phương thức chuyển khoản chỉ áp dụng cho tài khoản ngân hàng.");
        }

        if (!isBank && account.AccountType == FinanceAccountType.Bank)
        {
            throw new BusinessException("INVALID_CASHBOOK_ACCOUNT", "Phương thức tiền mặt không áp dụng cho tài khoản ngân hàng.");
        }

        if (account.AccountType != FinanceAccountType.Bank
            && account.BranchId.HasValue
            && branchId != Guid.Empty
            && account.BranchId.Value != branchId)
        {
            throw new BusinessException("INVALID_CASHBOOK_ACCOUNT", "Tài khoản quỹ được chọn không thuộc chi nhánh của phiếu lương.");
        }

        return account;
    }

    public async Task<FinanceAccount> GetOrCreateBankAccountAsync(
        Guid? branchId = null,
        string? bankName = null,
        string? accountNumber = null,
        string? accountHolderName = null,
        CancellationToken ct = default)
    {
        RequireOwner();
        var existingBanks = await _accountRepo.FindAsync(b => b.AccountType == FinanceAccountType.Bank && !b.IsDeleted, ct);
        if (_branchRepo != null)
        {
            if (!branchId.HasValue)
                throw new BusinessException("BRANCH_REQUIRED", "Cần chọn chi nhánh để xác định tài khoản ngân hàng.");
            var branch = await _branchRepo.GetByIdAsync(branchId.Value, ct);
            if (branch == null || branch.IsDeleted || branch.TenantId != _tenantContext.CurrentTenantId)
                throw new BusinessException("BRANCH_NOT_FOUND", "Không tìm thấy chi nhánh thuộc cửa hàng.");
            var current = existingBanks.FirstOrDefault(b => b.Id == branch.CurrentBankFinanceAccountId
                && b.TenantId == branch.TenantId && b.BranchId == branch.Id && b.IsActive);
            return current ?? throw new BusinessException("BANK_ACCOUNT_NOT_CONFIGURED", "Chi nhánh chưa chọn tài khoản ngân hàng nhận tiền.");
        }
        var matchedBank = existingBanks
            .Where(b => b.IsActive && b.BranchId == branchId)
            .FirstOrDefault()
            ?? existingBanks.FirstOrDefault(b => b.BranchId == branchId);

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
            // Sổ ngân hàng phục vụ chốt ngày phải tách theo chi nhánh. Tài khoản vật lý
            // dùng chung được biểu diễn bằng PaymentAccount/BranchPaymentAccount.
            BranchId = branchId,
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
        RequireOwner();
        var accounts = await _accountRepo.GetAllAsync(ct);
        // Danh sách chỉ phản ánh tài khoản đã cấu hình; GET không tự tạo két hoặc ngân hàng.
        var results = new List<CashAccountResponse>();
        var branchMap = _branchRepo != null
            ? (await _branchRepo.GetAllAsync(ct)).ToDictionary(b => b.Id, b => b.Name)
            : new Dictionary<Guid, string>();

        foreach (var till in accounts.Where(a => a.AccountType == FinanceAccountType.Till && (!branchId.HasValue || a.BranchId == branchId.Value)))
        {
            var bName = till.BranchId.HasValue && branchMap.TryGetValue(till.BranchId.Value, out var name)
                ? name
                : "Chi nhánh chính";
            results.Add(new CashAccountResponse(
                till.Id,
                till.BranchId,
                bName,
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
            var bName = safe.BranchId.HasValue && branchMap.TryGetValue(safe.BranchId.Value, out var name)
                ? name
                : "Toàn hệ thống (Chủ cửa hàng)";
            results.Add(new CashAccountResponse(
                safe.Id,
                safe.BranchId,
                bName,
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

        foreach (var bank in accounts.Where(a =>
                     a.AccountType == FinanceAccountType.Bank &&
                     a.IsActive &&
                     (!branchId.HasValue || !a.BranchId.HasValue || a.BranchId == branchId.Value)))
        {
            var bName = bank.BranchId.HasValue && branchMap.TryGetValue(bank.BranchId.Value, out var name)
                ? name
                : "Toàn hệ thống";
            results.Add(new CashAccountResponse(
                bank.Id,
                bank.BranchId,
                bName,
                $"{bank.BankName} - TK {bank.AccountNumber}",
                "BANK",
                bank.BankName,
                bank.AccountNumber,
                bank.CurrentBalance,
                bank.CurrentBalance,
                bank.IsActive,
                "VND",
                false,
                bank.BankCode,
                bank.AccountHolderName,
                bank.IsDefaultForVietQr
            ));
        }

        return results;
    }

    public async Task<CashbookSummaryResponse> GetSummaryAsync(Guid? branchId = null, CancellationToken ct = default)
    {
        RequireOwner();
        var accounts = await GetAccountsAsync(branchId, ct);
        var accountIds = accounts.Select(a => a.Id).ToHashSet();
        var entries = await _cashbookRepo.FindAsync(
            e => !branchId.HasValue || accountIds.Contains(e.AccountId),
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

    public async Task<CashbookEntryResponse> CreateReceiptAsync(CreateReceiptRequest request, CancellationToken ct = default)
    {
        RequireOwner();
        ValidateFinancialManagementRole();
        if (request.Amount <= 0)
        {
            throw new BusinessException("INVALID_AMOUNT", "Số tiền thu phải lớn hơn 0.");
        }

        var classification = ResolveReceiptClassification(request);
        ValidateDescription(request.Description);

        var refCode = string.IsNullOrWhiteSpace(request.ReferenceCode)
            ? "PT-" + DateTime.UtcNow.ToString("yyyyMMdd") + "-" + Random.Shared.Next(100, 999)
            : request.ReferenceCode.Trim();

        var description = string.IsNullOrWhiteSpace(request.Description)
            ? request.Category
            : request.Description.Trim();

        return await ReceiveCashCoreAsync(request.AccountId, request.Amount, "MANUAL", null, refCode, description, classification, ct);
    }

    public async Task<CashbookEntryResponse> CreatePaymentAsync(CreatePaymentRequest request, CancellationToken ct = default)
    {
        RequireOwner();
        ValidateFinancialManagementRole();
        if (request.Amount <= 0)
        {
            throw new BusinessException("INVALID_AMOUNT", "Số tiền chi phải lớn hơn 0.");
        }

        return await _unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var account = await _cashbookRepo.GetAccountWithLockAsync(request.AccountId, token)
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

            var classification = ResolvePaymentClassification(request);
            ValidateDescription(request.Description);

            var refCode = string.IsNullOrWhiteSpace(request.ReferenceCode)
                ? "PC-" + DateTime.UtcNow.ToString("yyyyMMdd") + "-" + Random.Shared.Next(100, 999)
                : request.ReferenceCode.Trim();

            var description = string.IsNullOrWhiteSpace(request.Description)
                ? request.Category
                : request.Description.Trim();

            var entry = await RecordCashMovementAsync(account, CashbookEntryType.Payment, request.Amount, "MANUAL", null,
                refCode, description, classification, token);
            await _unitOfWork.SaveChangesAsync(token);
            return MapEntryResponse(entry);
        }, ct);
    }

    private static EntryClassification ResolveReceiptClassification(CreateReceiptRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CategoryCode))
            throw new BusinessException("CATEGORY_REQUIRED", "Phiếu thu bắt buộc có mã danh mục nghiệp vụ.");
        var code = request.CategoryCode.Trim().ToUpperInvariant();
        return code switch
        {
            "OWNER_DEPOSIT" => RequireTreatment(code, FinancialNature.OwnerDeposit, CashbookTaxTreatment.NonTaxableReceipt, request.TaxTreatment),
            "OFF_POS_SALE" => RequireTreatment(code, FinancialNature.OtherIncome, CashbookTaxTreatment.TaxableRevenue, request.TaxTreatment),
            "INTERNAL_REIMBURSEMENT" => RequireTreatment(code, FinancialNature.OtherIncome, CashbookTaxTreatment.NonTaxableReceipt, request.TaxTreatment),
            "BANK_INTEREST" => RequireTreatment(code, FinancialNature.OtherIncome, CashbookTaxTreatment.TaxableRevenue, request.TaxTreatment),
            "OTHER_INCOME" => ResolveOtherIncome(request),
            _ => throw new BusinessException("INVALID_CATEGORY", $"Danh mục thu '{request.CategoryCode}' không hợp lệ.")
        };
    }

    private static EntryClassification ResolveOtherIncome(CreateReceiptRequest request)
    {
        if (request.TaxTreatment is not (CashbookTaxTreatment.TaxableRevenue or CashbookTaxTreatment.NonTaxableReceipt))
        {
            throw new BusinessException("TAX_TREATMENT_REQUIRED", "Thu nhập khác bắt buộc chọn tính vào doanh thu chịu thuế hoặc không tính thuế.");
        }
        if (request.TaxTreatment == CashbookTaxTreatment.NonTaxableReceipt && string.IsNullOrWhiteSpace(request.TaxTreatmentReason))
        {
            throw new BusinessException("TAX_REASON_REQUIRED", "Khoản thu không tính thuế bắt buộc có lý do.");
        }
        ValidateTaxReason(request.TaxTreatmentReason);
        return new EntryClassification("OTHER_INCOME", FinancialNature.OtherIncome, request.TaxTreatment.Value, request.TaxTreatmentReason?.Trim());
    }

    private static EntryClassification ResolvePaymentClassification(CreatePaymentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CategoryCode))
            throw new BusinessException("CATEGORY_REQUIRED", "Phiếu chi bắt buộc có mã danh mục nghiệp vụ.");
        var code = request.CategoryCode.Trim().ToUpperInvariant();
        return code switch
        {
            "OWNER_DRAW" => RequireTreatment(code, FinancialNature.OwnerDraw, CashbookTaxTreatment.NonDeductibleExpense, request.TaxTreatment),
            "ASSET_PURCHASE" => RequireTreatment(code, FinancialNature.OperatingExpense, CashbookTaxTreatment.NonDeductibleExpense, request.TaxTreatment),
            "OTHER_PAYMENT" => ResolveOtherPayment(request),
            "TAX_PAYMENT" => throw new BusinessException("USE_TAX_PAYMENT_ENDPOINT", "Nộp thuế phải được ghi nhận qua nghiệp vụ nộp thuế chuyên dụng và có chứng từ tham chiếu."),
            _ => throw new BusinessException("INVALID_CATEGORY", $"Danh mục chi '{request.CategoryCode}' không hợp lệ.")
        };
    }

    private static EntryClassification ResolveOtherPayment(CreatePaymentRequest request)
    {
        if (request.TaxTreatment is not (CashbookTaxTreatment.DeductibleExpense or CashbookTaxTreatment.NonDeductibleExpense))
        {
            throw new BusinessException("TAX_TREATMENT_REQUIRED", "Chi khác bắt buộc chọn chi phí được trừ hoặc không được trừ.");
        }
        if (request.TaxTreatment == CashbookTaxTreatment.DeductibleExpense && string.IsNullOrWhiteSpace(request.ReferenceCode))
        {
            throw new BusinessException("EVIDENCE_REQUIRED", "Chi phí được trừ bắt buộc có số hoặc mã chứng từ.");
        }
        if (request.ReferenceCode?.Trim().Length > 100)
            throw new BusinessException("REFERENCE_TOO_LONG", "Mã chứng từ không được vượt quá 100 ký tự.");
        ValidateTaxReason(request.TaxTreatmentReason);
        return new EntryClassification("OTHER_PAYMENT", FinancialNature.OperatingExpense, request.TaxTreatment.Value, request.TaxTreatmentReason?.Trim());
    }

    private static EntryClassification RequireTreatment(string categoryCode, FinancialNature nature, CashbookTaxTreatment expected, CashbookTaxTreatment? requested)
    {
        if (requested.HasValue && requested.Value != expected)
        {
            throw new BusinessException("INVALID_TAX_TREATMENT", $"Cách xử lý thuế không phù hợp với danh mục {categoryCode}.");
        }
        return new EntryClassification(categoryCode, nature, expected);
    }

    private static void ValidateDescription(string? description)
    {
        if (description?.Trim().Length > 255)
        {
            throw new BusinessException("DESCRIPTION_TOO_LONG", "Diễn giải không được vượt quá 255 ký tự.");
        }
    }

    private static void ValidateTaxReason(string? reason)
    {
        if (reason?.Trim().Length > 255)
            throw new BusinessException("TAX_REASON_TOO_LONG", "Lý do xử lý thuế không được vượt quá 255 ký tự.");
    }

    public async Task<TaxPaymentResponse> RecordTaxPaymentAsync(RecordTaxPaymentRequest request, CancellationToken ct = default)
    {
        RequireOwner();
        ValidateFinancialManagementRole();
        if (_expenseVoucherRepo == null || _taxRepository == null)
        {
            throw new InvalidOperationException("Dịch vụ nộp thuế chưa được cấu hình đầy đủ.");
        }
        if (request.Amount <= 0)
        {
            throw new BusinessException("INVALID_AMOUNT", "Số tiền nộp thuế phải lớn hơn 0.");
        }
        if (string.IsNullOrWhiteSpace(request.BeneficiaryName) || string.IsNullOrWhiteSpace(request.PaymentReference))
        {
            throw new BusinessException("TAX_PAYMENT_EVIDENCE_REQUIRED", "Nộp thuế phải có đơn vị thụ hưởng và mã/chứng từ tham chiếu thanh toán.");
        }

        return await _unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var account = await _cashbookRepo.GetAccountWithLockAsync(request.SourceAccountId, token)
                ?? throw new NotFoundException("Không tìm thấy tài khoản nguồn nộp thuế.");
            if (!string.Equals(account.TenantId, _tenantContext.CurrentTenantId, StringComparison.Ordinal) ||
                !account.IsActive || account.AccountType is not (FinanceAccountType.Bank or FinanceAccountType.Safe))
            {
                throw new BusinessException("TAX_PAYMENT_ACCOUNT_DENIED", "Nộp thuế chỉ được thực hiện từ tài khoản ngân hàng hoặc két sắt đang hoạt động.");
            }
            if (account.CurrentBalance < request.Amount)
            {
                throw new BusinessException("INSUFFICIENT_FUNDS", "Tài khoản nguồn không đủ số dư để nộp thuế.");
            }

            HouseholdTaxSnapshot? snapshot = null;
            if (request.HouseholdTaxSnapshotId.HasValue)
            {
                snapshot = await _taxRepository.GetSnapshotForUpdateAsync(request.HouseholdTaxSnapshotId.Value, token)
                    ?? throw new BusinessException("TAX_SNAPSHOT_NOT_FOUND", "Không tìm thấy snapshot thuế đã chốt để liên kết chứng từ nộp thuế.");
                if (snapshot.Status != TaxSnapshotStatus.Finalized)
                    throw new BusinessException("TAX_SNAPSHOT_NOT_FINALIZED", "Hãy chọn bản chốt thuế đang có hiệu lực để liên kết chứng từ nộp thuế.");

                if (snapshot.BranchId.HasValue && snapshot.BranchId != account.BranchId)
                {
                    throw new BusinessException("TAX_PAYMENT_ACCOUNT_DENIED", "Tài khoản nguồn không cùng phạm vi chi nhánh với snapshot thuế được chọn.");
                }
            }

            var paidAtUtc = DateTime.UtcNow;
            var voucher = new ExpenseVoucher
            {
                TenantId = _tenantContext.CurrentTenantId,
                VoucherCode = "TAX-" + paidAtUtc.ToString("yyyyMMddHHmmssfff"),
                BranchId = account.BranchId,
                BeneficiaryName = request.BeneficiaryName.Trim(),
                Category = "Nộp thuế",
                Amount = request.Amount,
                Status = ExpenseVoucherStatus.Paid,
                Nature = FinancialNature.TaxPayment,
                ReferenceCode = request.PaymentReference.Trim(),
                EvidenceAttachmentUrl = string.IsNullOrWhiteSpace(request.EvidenceAttachmentUrl) ? null : request.EvidenceAttachmentUrl.Trim(),
                PaymentSourceType = account.AccountType,
                PaymentSourceAccountId = account.Id,
                HouseholdTaxSnapshotId = snapshot?.Id,
                PaidAtUtc = paidAtUtc,
                ExpenseDate = paidAtUtc,
                Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
                IsTaxDeductible = false
            };
            await _expenseVoucherRepo.AddAsync(voucher, token);

            account.CurrentBalance -= request.Amount;
            var entry = await RecordEntryInternalAsync(
                account,
                account.BranchId,
                CashbookEntryType.Payment,
                request.Amount,
                "TAX_PAYMENT",
                voucher.Id,
                voucher.ReferenceCode!,
                $"Nộp thuế: {voucher.BeneficiaryName}",
                token);
            await QueueEntryNotificationAsync(entry, BusinessNotificationTypes.TaxPaymentRecorded, "Đã ghi nhận tiền nộp thuế", token);
            await _unitOfWork.SaveChangesAsync(token);

            return new TaxPaymentResponse(voucher.Id, voucher.HouseholdTaxSnapshotId, MapEntryResponse(entry));
        }, ct);
    }

    private Task QueueEntryNotificationAsync(CashbookEntry entry, string type, string title, CancellationToken ct) =>
        _notificationOutbox?.EnqueueAsync(_tenantContext.CurrentTenantId, entry.Id, new BusinessNotification(
            type, title, $"Bút toán {entry.EntryCode}.",
            $"/store-owner/cashbook?accountId={entry.AccountId}&branchId={entry.BranchId?.ToString() ?? "ALL"}&search={Uri.EscapeDataString(entry.EntryCode)}"), ct) ?? Task.CompletedTask;

    public async Task<TransferFundResponse> TransferFundsAsync(TransferFundRequest request, CancellationToken ct = default)
    {
        RequireOwner();
        if (request.Amount <= 0)
        {
            throw new BusinessException("INVALID_AMOUNT", "Số tiền chuyển quỹ phải lớn hơn 0.");
        }
        if (request.FromAccountId == request.ToAccountId)
        {
            throw new BusinessException("INVALID_ACCOUNTS", "Tài khoản nguồn và tài khoản đích không được trùng nhau.");
        }

        // Khóa theo cùng thứ tự ID kể cả khi hai yêu cầu chuyển theo hướng ngược nhau.
        var sourceFirst = request.FromAccountId.CompareTo(request.ToAccountId) < 0;
        var firstId = sourceFirst ? request.FromAccountId : request.ToAccountId;
        var secondId = sourceFirst ? request.ToAccountId : request.FromAccountId;

        return await _unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var first = await _cashbookRepo.GetAccountWithLockAsync(firstId, token)
                ?? throw new NotFoundException($"Không tìm thấy tài khoản quỹ với Id: {firstId}");
            var second = await _cashbookRepo.GetAccountWithLockAsync(secondId, token)
                ?? throw new NotFoundException($"Không tìm thấy tài khoản quỹ với Id: {secondId}");
            var source = sourceFirst ? first : second;
            var destination = sourceFirst ? second : first;

            var dateStr = DateTime.UtcNow.ToString("yyyyMMdd");
            var rand = Random.Shared.Next(100, 999);
            var note = string.IsNullOrWhiteSpace(request.Note) ? "Chuyển tiền nội bộ" : request.Note.Trim();

            var payment = await RecordCashMovementAsync(source, CashbookEntryType.Payment, request.Amount,
                "INTERNAL_TRANSFER", null, $"PC-TRF-{dateStr}-{rand}", note, null, token);
            var receipt = await RecordCashMovementAsync(destination, CashbookEntryType.Receipt, request.Amount,
                "INTERNAL_TRANSFER", payment.Id, $"PT-TRF-{dateStr}-{rand}", note, null, token);
            await _unitOfWork.SaveChangesAsync(token);
            return new TransferFundResponse(MapEntryResponse(payment), MapEntryResponse(receipt));
        }, ct);
    }

    private static readonly HashSet<string> NonVoidableSystemSources = new(StringComparer.OrdinalIgnoreCase)
    {
        "COMPENSATING",
        "REVERSAL",
        "EXPENSE",
        "EXPENSE_REVERSAL",
        "DEBT_REPAYMENT",
        "TAX_PAYMENT",
        "POS_SALE",
        "POS",
        "INTERNAL_TRANSFER",
        "CASH_DROP",
        "BANK_DEPOSIT",
        "PAYSLIP",
    };

    public async Task<CashbookEntryResponse> VoidTransactionAsync(Guid entryId, VoidEntryRequest request, CancellationToken ct = default)
    {
        RequireOwner();
        ValidateFinancialManagementRole();
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new BusinessException("REASON_REQUIRED", "Lý do gạch bỏ bút toán là bắt buộc.");
        }

        return await _unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var origEntry = await _cashbookRepo.GetByIdAsync(entryId, token)
                ?? throw new NotFoundException($"Không tìm thấy bút toán sổ cái với Id: {entryId}");

            if (string.Equals(origEntry.SourceType, "COMPENSATING", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(origEntry.SourceType, "REVERSAL", StringComparison.OrdinalIgnoreCase))
            {
                throw new BusinessException("CANNOT_VOID_REVERSAL", "Không thể gạch bỏ bút toán bù trừ/đảo ngược.");
            }

            if (NonVoidableSystemSources.Contains(origEntry.SourceType))
            {
                throw new BusinessException(
                    "CANNOT_VOID_SYSTEM_ENTRY",
                    $"Không thể gạch bỏ trực tiếp bút toán hệ thống ({origEntry.SourceType}) tại Sổ quỹ để tránh lệch công nợ/chi phí. Vui lòng hủy từ phân hệ gốc.");
            }

            // Lock account before checking existing compensating entries to prevent TOCTOU double-void
            var lockedAccount = await _cashbookRepo.GetAccountWithLockAsync(origEntry.AccountId, token)
                ?? throw new NotFoundException($"Không tìm thấy tài khoản quỹ với Id: {origEntry.AccountId}");

            var existingCompList = await _cashbookRepo.FindAsync(
                e => (e.SourceType == "COMPENSATING" || e.SourceType == "REVERSAL" || e.SourceType == "EXPENSE_REVERSAL") &&
                     (e.SourceId == entryId || e.ReversedEntryId == entryId),
                token);
            if (existingCompList.Count > 0)
            {
                throw new BusinessException("ALREADY_VOIDED", "Bút toán này đã được gạch bỏ trước đó.");
            }

            var dateStr = DateTime.UtcNow.ToString("yyyyMMdd");
            var rand = Random.Shared.Next(100, 999);
            var compRefCode = $"GB-{dateStr}-{rand}";
            var compDescription = $"[BÚT TOÁN GẠCH BỎ] Lý do: {request.Reason.Trim()}. Bù trừ cho phiếu gốc {origEntry.EntryCode}";
            var classification = new EntryClassification(origEntry.CategoryCode, origEntry.FinancialNature, origEntry.TaxTreatment, origEntry.TaxTreatmentReason);

            var reverseType = origEntry.EntryType == CashbookEntryType.Receipt
                ? CashbookEntryType.Payment
                : CashbookEntryType.Receipt;

            if (reverseType == CashbookEntryType.Payment && lockedAccount.CurrentBalance < origEntry.Amount)
            {
                throw new BusinessException("INSUFFICIENT_BALANCE", $"Số dư tài khoản quỹ không đủ để gạch bỏ phiếu thu này (Hiện có: {lockedAccount.CurrentBalance:N0}đ).");
            }

            var compEntry = await RecordCashMovementAsync(
                lockedAccount,
                reverseType,
                origEntry.Amount,
                "COMPENSATING",
                origEntry.Id,
                compRefCode,
                compDescription,
                classification,
                token);
            await _unitOfWork.SaveChangesAsync(token);
            return MapEntryResponse(compEntry);
        }, ct);
    }

    private Task<CashbookEntry> RecordEntryInternalAsync(FinanceAccount account, Guid? branchId, CashbookEntryType entryType, decimal amount, string sourceType, Guid? sourceId, string referenceCode, string description, CancellationToken ct) =>
        RecordEntryInternalAsync(account, branchId, entryType, amount, sourceType, sourceId, referenceCode, description, null, ct);

    private async Task<CashbookEntry> RecordEntryInternalAsync(FinanceAccount account, Guid? branchId, CashbookEntryType entryType, decimal amount, string sourceType, Guid? sourceId, string referenceCode, string description, EntryClassification? classification, CancellationToken ct)
    {
        classification ??= InferClassification(sourceType, entryType);
        var prevHash = await _cashbookRepo.GetLastAuditHashAsync(account.Id, ct);

        var entry = new CashbookEntry
        {
            TenantId = _tenantContext.CurrentTenantId,
            EntryCode = $"CB-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Random.Shared.Next(1000, 9999)}",
            AccountId = account.Id,
            AccountType = account.AccountType,
            BranchId = branchId,
            EntryType = entryType,
            Amount = amount,
            BalanceAfter = account.CurrentBalance,
            SourceType = sourceType,
            CategoryCode = classification.CategoryCode,
            FinancialNature = classification.FinancialNature,
            TaxTreatment = classification.TaxTreatment,
            TaxTreatmentReason = classification.TaxTreatmentReason,
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

    private static EntryClassification InferClassification(string sourceType, CashbookEntryType entryType)
    {
        var source = sourceType.ToUpperInvariant();
        if (source is "INTERNAL_TRANSFER" or "CASH_DROP" or "BANK_DEPOSIT")
            return new EntryClassification(source, FinancialNature.InternalTransfer, CashbookTaxTreatment.InternalTransfer);
        if (source == "TAX_PAYMENT")
            return new EntryClassification("TAX_PAYMENT", FinancialNature.TaxPayment, CashbookTaxTreatment.TaxPayment);
        if (source is "POS" or "POS_SALE")
            return new EntryClassification("POS_SALE", FinancialNature.OtherIncome, CashbookTaxTreatment.TaxableRevenue);
        if (source is "DEBT" or "DEBT_REPAYMENT")
            return new EntryClassification("DEBT_COLLECTION", FinancialNature.OtherIncome, CashbookTaxTreatment.NonTaxableReceipt);
        return new EntryClassification("REVIEW_REQUIRED", FinancialNature.Suspense, CashbookTaxTreatment.ReviewRequired,
            $"Chưa có quy tắc phân loại tự động cho nguồn {sourceType}/{entryType}");
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
            e.OccurredAtUtc,
            e.CategoryCode,
            e.FinancialNature,
            e.TaxTreatment,
            e.TaxTreatmentReason
        );

    private void ValidateFinancialManagementRole() => RequireOwner();

    private void RequireOwner() => TenantRoleGuard.RequireStoreOwner(_tenantContext, "sổ quỹ và tài chính");

    public async Task<bool> VerifyChainIntegrityAsync(Guid accountId, CancellationToken ct = default)
    {
        RequireOwner();
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
