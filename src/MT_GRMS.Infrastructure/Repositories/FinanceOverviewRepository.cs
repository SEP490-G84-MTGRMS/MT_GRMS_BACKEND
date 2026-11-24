using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.FinanceOverview;
using MT_GRMS.Application.FinanceOverview.Dtos;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Domain.Exceptions;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.Infrastructure.Repositories;

public sealed class FinanceOverviewRepository : IFinanceOverviewRepository
{
    private const string InternalTransferSource = "INTERNAL_TRANSFER";
    private readonly AppDbContext _context;

    public FinanceOverviewRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<FinanceOverviewResponse> GetOverviewAsync(
        Guid? branchId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken ct = default)
    {
        var branches = await _context.Branches
            .AsNoTracking()
            .Where(x => x.IsActive && (!branchId.HasValue || x.Id == branchId.Value))
            .OrderByDescending(x => x.IsHeadquarter)
            .ThenBy(x => x.Name)
            .ToListAsync(ct);

        if (branchId.HasValue && branches.Count == 0)
        {
            throw new NotFoundException("Không tìm thấy chi nhánh trong cửa hàng hiện tại.");
        }

        var selectedBranchIds = branches.Select(x => x.Id).ToHashSet();
        var accounts = await _context.FinanceAccounts
            .AsNoTracking()
            .Where(x => x.IsActive &&
                (x.AccountType == FinanceAccountType.Till ||
                 x.AccountType == FinanceAccountType.Safe ||
                 x.AccountType == FinanceAccountType.Bank) &&
                (branchId.HasValue
                    ? x.BranchId == branchId.Value
                    : !x.BranchId.HasValue || selectedBranchIds.Contains(x.BranchId.Value)))
            .ToListAsync(ct);

        var accountIds = accounts.Select(x => x.Id).ToHashSet();
        var entries = await _context.CashbookEntries
            .AsNoTracking()
            .Where(x => accountIds.Contains(x.AccountId) && x.OccurredAtUtc >= fromUtc)
            .ToListAsync(ct);

        var expenses = await _context.ExpenseVouchers
            .AsNoTracking()
            .Where(x => !branchId.HasValue || x.BranchId == branchId.Value)
            .Where(x => x.Status != ExpenseVoucherStatus.Cancelled &&
                        x.Status != ExpenseVoucherStatus.Reversed &&
                        x.Status != ExpenseVoucherStatus.Paid)
            .ToListAsync(ct);

        var customerDebts = await _context.Customers
            .AsNoTracking()
            .Where(x => x.TotalDebt > 0)
            .ToListAsync(ct);

        var movements = await _context.ShiftCashMovements
            .AsNoTracking()
            .Include(x => x.Shift)
            .Where(x => x.Shift != null &&
                        (!branchId.HasValue || x.Shift.BranchId == branchId.Value) &&
                        x.Category == CashMovementCategory.CASH_DRAW &&
                        !x.IsConfirmedByOwner &&
                        x.Status != CashMovementStatus.Rejected &&
                        x.Status != CashMovementStatus.Cancelled)
            .ToListAsync(ct);

        var nowUtc = DateTime.UtcNow;
        var currentSnapshot = toUtc >= nowUtc.AddMinutes(-5);
        var branchRows = branches.Select(branch =>
        {
            var branchAccounts = accounts.Where(x => x.BranchId == branch.Id).ToList();
            var branchAccountIds = branchAccounts.Select(x => x.Id).ToHashSet();
            return BuildBranchRow(
            branch.Id,
            branch.BranchCode,
            branch.Name,
            branchAccounts,
            entries.Where(x => branchAccountIds.Contains(x.AccountId)).ToList(),
            expenses.Where(x => x.BranchId == branch.Id).ToList(),
            movements.Where(x => x.Shift?.BranchId == branch.Id).ToList(),
            fromUtc,
            toUtc,
            currentSnapshot);
        }).ToList();

        var totals = BuildTotals(accounts, entries, branchRows, fromUtc, toUtc);
        var attention = new FinanceAttentionDto(
            ReceivableAmount: currentSnapshot && !branchId.HasValue ? customerDebts.Sum(x => x.TotalDebt) : 0,
            DebtorCount: currentSnapshot && !branchId.HasValue ? customerDebts.Count : 0,
            PendingPayableAmount: currentSnapshot ? expenses.Sum(x => x.Amount) : 0,
            PendingPayableCount: currentSnapshot ? expenses.Count : 0,
            CashInTransit: currentSnapshot ? movements.Sum(x => x.Amount) : 0,
            PendingCashMovementCount: currentSnapshot ? movements.Count : 0,
            IsCurrentSnapshot: currentSnapshot);

        return new FinanceOverviewResponse(
            new FinanceOverviewScopeDto(
                branchId,
                branchId.HasValue ? branches[0].Name : "Toàn bộ chi nhánh",
                branches.Count),
            new FinanceOverviewPeriodDto(fromUtc, toUtc, "Asia/Ho_Chi_Minh"),
            toUtc,
            nowUtc,
            totals,
            attention,
            branchRows);
    }

    private static BranchCashHealthDto BuildBranchRow(
        Guid branchId,
        string branchCode,
        string branchName,
        IReadOnlyCollection<FinanceAccount> accounts,
        IReadOnlyCollection<CashbookEntry> entries,
        IReadOnlyCollection<ExpenseVoucher> expenses,
        IReadOnlyCollection<ShiftCashMovement> movements,
        DateTime fromUtc,
        DateTime toUtc,
        bool currentSnapshot)
    {
        var flow = CalculateFlow(accounts, entries, fromUtc, toUtc);
        var cashAtRegisters = accounts.Where(x => x.AccountType == FinanceAccountType.Till)
            .Sum(x => BalanceAt(x, entries, toUtc));
        var safeDeposit = accounts.Where(x => x.AccountType == FinanceAccountType.Safe)
            .Sum(x => BalanceAt(x, entries, toUtc));
        var pendingPayable = currentSnapshot ? expenses.Sum(x => x.Amount) : 0;
        var cashInTransit = currentSnapshot ? movements.Sum(x => x.Amount) : 0;
        var status = !currentSnapshot
            ? "HISTORICAL_CASH_ONLY"
            : pendingPayable > 0 || cashInTransit > 0 ? "ATTENTION" : "HEALTHY";

        return new BranchCashHealthDto(
            branchId,
            branchCode,
            branchName,
            flow.Opening,
            flow.CashIn,
            flow.CashOut,
            flow.CashIn - flow.CashOut,
            flow.InternalTransferNet,
            flow.Closing,
            cashAtRegisters,
            safeDeposit,
            cashInTransit,
            0,
            pendingPayable,
            currentSnapshot ? expenses.Count : 0,
            status);
    }

    private static FinanceOverviewTotalsDto BuildTotals(
        IReadOnlyCollection<FinanceAccount> accounts,
        IReadOnlyCollection<CashbookEntry> entries,
        IReadOnlyCollection<BranchCashHealthDto> branches,
        DateTime fromUtc,
        DateTime toUtc)
    {
        var flow = CalculateFlow(accounts, entries, fromUtc, toUtc);
        var cashAtRegisters = accounts.Where(x => x.AccountType == FinanceAccountType.Till)
            .Sum(x => BalanceAt(x, entries, toUtc));
        var safeDeposit = accounts.Where(x => x.AccountType == FinanceAccountType.Safe)
            .Sum(x => BalanceAt(x, entries, toUtc));
        var banks = accounts.Where(x => x.AccountType == FinanceAccountType.Bank)
            .Sum(x => BalanceAt(x, entries, toUtc));
        var unallocatedBanks = accounts.Where(x => x.AccountType == FinanceAccountType.Bank && !x.BranchId.HasValue)
            .Sum(x => BalanceAt(x, entries, toUtc));
        var transit = branches.Sum(x => x.CashInTransit);

        return new FinanceOverviewTotalsDto(
            flow.Opening,
            flow.CashIn,
            flow.CashOut,
            flow.CashIn - flow.CashOut,
            flow.InternalTransferNet,
            flow.Closing,
            cashAtRegisters + safeDeposit + banks,
            cashAtRegisters,
            safeDeposit,
            banks,
            unallocatedBanks,
            transit);
    }

    private static bool IsInternalSource(CashbookEntry entry) =>
        entry.FinancialNature == FinancialNature.InternalTransfer
        || string.Equals(entry.SourceType, InternalTransferSource, StringComparison.OrdinalIgnoreCase)
        || string.Equals(entry.SourceType, "CASH_DROP", StringComparison.OrdinalIgnoreCase)
        || string.Equals(entry.SourceType, "BANK_DEPOSIT", StringComparison.OrdinalIgnoreCase);

    private static (decimal Opening, decimal CashIn, decimal CashOut, decimal InternalTransferNet, decimal Closing)
        CalculateFlow(
            IReadOnlyCollection<FinanceAccount> accounts,
            IReadOnlyCollection<CashbookEntry> entries,
            DateTime fromUtc,
            DateTime toUtc)
    {
        var opening = accounts.Sum(x => BalanceAt(x, entries, fromUtc.AddTicks(-1)));
        var closing = accounts.Sum(x => BalanceAt(x, entries, toUtc));
        var periodEntries = entries.Where(x => x.OccurredAtUtc >= fromUtc && x.OccurredAtUtc <= toUtc).ToList();
        var internalEntries = periodEntries.Where(IsInternalSource).ToList();
        var external = periodEntries.Where(x => !IsInternalSource(x)).ToList();

        // Net COMPENSATING reversal entries against the side they reverse so voided receipts/payments do not inflate gross CashIn/CashOut
        var regularExternal = external.Where(x => !string.Equals(x.SourceType, "COMPENSATING", StringComparison.OrdinalIgnoreCase)).ToList();
        var compensating = external.Where(x => string.Equals(x.SourceType, "COMPENSATING", StringComparison.OrdinalIgnoreCase)).ToList();

        var cashIn = regularExternal.Where(x => x.EntryType == CashbookEntryType.Receipt).Sum(x => x.Amount)
            - compensating.Where(x => x.EntryType == CashbookEntryType.Payment).Sum(x => x.Amount);
        var cashOut = regularExternal.Where(x => x.EntryType == CashbookEntryType.Payment).Sum(x => x.Amount)
            - compensating.Where(x => x.EntryType == CashbookEntryType.Receipt).Sum(x => x.Amount);
        var internalNet = internalEntries.Where(x => x.EntryType == CashbookEntryType.Receipt).Sum(x => x.Amount)
            - internalEntries.Where(x => x.EntryType == CashbookEntryType.Payment).Sum(x => x.Amount);
        return (opening, Math.Max(0m, cashIn), Math.Max(0m, cashOut), internalNet, closing);
    }

    private static decimal BalanceAt(
        FinanceAccount account,
        IReadOnlyCollection<CashbookEntry> entries,
        DateTime asOfUtc)
    {
        if (account.CreatedAt > asOfUtc)
        {
            return 0;
        }

        var laterEntries = entries.Where(x => x.AccountId == account.Id && x.OccurredAtUtc > asOfUtc);
        var laterNet = laterEntries.Where(x => x.EntryType == CashbookEntryType.Receipt).Sum(x => x.Amount)
            - laterEntries.Where(x => x.EntryType == CashbookEntryType.Payment).Sum(x => x.Amount);
        return account.CurrentBalance - laterNet;
    }
}
