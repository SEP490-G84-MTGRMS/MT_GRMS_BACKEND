using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using MT_GRMS.Application.Auth;
using MT_GRMS.Application.BusinessDays;
using MT_GRMS.Application.BusinessDays.Dtos;
using MT_GRMS.Application.Cashbook;
using MT_GRMS.Application.Cashbook.Dtos;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Common.Interfaces;
using MT_GRMS.Application.Common.Services;
using MT_GRMS.Application.Expenses;
using MT_GRMS.Application.Expenses.Dtos;
using MT_GRMS.Application.HR.Employees;
using MT_GRMS.Application.HR.Employees.Dtos;
using MT_GRMS.Application.HR.ShiftConfigs;
using MT_GRMS.Application.HR.ShiftConfigs.Dtos;
using MT_GRMS.Application.Repacks;
using MT_GRMS.Application.Repacks.Dtos;
using MT_GRMS.Application.SupplierReturns;
using MT_GRMS.Application.SupplierReturns.Dtos;
using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Constants;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Entities.HR;
using MT_GRMS.Domain.Entities.Identity;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Entities.Sales;
using MT_GRMS.Domain.Entities.Supplier;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Domain.Exceptions;
using Xunit;

namespace MT_GRMS.UnitTests;

public class DeepBranchCoverageSuite4Tests
{
    #region Fakes & Infrastructure

    private class FakeTenantContext : ITenantContext
    {
        public string CurrentTenantId { get; set; } = "tenant-suite4";
        public Guid CurrentUserId { get; set; } = Guid.NewGuid();
        public string CurrentRole { get; set; } = "STORE_OWNER";
        public Guid? CurrentBranchId { get; set; } = Guid.NewGuid();
        public string? CustomRawUserId { get; set; } = null;
        public string CurrentRawUserId => CustomRawUserId ?? (CurrentUserId != Guid.Empty ? CurrentUserId.ToString() : string.Empty);

        public void SetContext(string tenantId, Guid userId, Guid? branchId)
        {
            CurrentTenantId = tenantId;
            CurrentUserId = userId;
            CurrentBranchId = branchId;
        }

        public void SetRole(string? role) => CurrentRole = role ?? string.Empty;
    }

    private class FakeUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
        public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private class FakeRepository<T> : IRepository<T> where T : BaseTenantEntity
    {
        public List<T> Items { get; } = new();

        public Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(Items.FirstOrDefault(x => x.Id == id && !x.IsDeleted));

        public Task<IReadOnlyList<T>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<T>>(Items.Where(x => !x.IsDeleted).ToList());

        public Task<IReadOnlyList<T>> FindAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<T>>(Items.AsQueryable().Where(predicate).Where(x => !x.IsDeleted).ToList());

        public Task AddAsync(T entity, CancellationToken cancellationToken = default)
        {
            if (entity.Id == Guid.Empty) entity.Id = Guid.NewGuid();
            if (!Items.Any(i => i.Id == entity.Id)) Items.Add(entity);
            return Task.CompletedTask;
        }

        public void Update(T entity)
        {
            var index = Items.FindIndex(x => x.Id == entity.Id);
            if (index >= 0) Items[index] = entity;
            else Items.Add(entity);
        }

        public void Delete(T entity) => entity.IsDeleted = true;
    }

    private class FakeCashbookRepository : ICashbookRepository
    {
        public List<FinanceAccount> Accounts { get; } = new();
        public List<CashbookEntry> Entries { get; } = new();
        public List<ShiftZReport> Shifts { get; } = new();

        public Task<FinanceAccount?> GetAccountWithLockAsync(Guid accountId, CancellationToken ct = default)
            => Task.FromResult(Accounts.FirstOrDefault(x => x.Id == accountId && !x.IsDeleted));

        public Task<ShiftZReport?> GetActiveShiftByTillIdAsync(Guid tillId, CancellationToken ct = default)
            => Task.FromResult(Shifts.FirstOrDefault(s => s.TillId == tillId && s.Status == ShiftState.Open && !s.IsDeleted));

        public Task<ShiftZReport?> GetLastClosedShiftByTillIdAsync(Guid tillId, CancellationToken ct = default)
            => Task.FromResult(Shifts.Where(s => s.TillId == tillId && s.Status == ShiftState.Closed).OrderByDescending(s => s.ClosedAtUtc).FirstOrDefault());

        public Task<string> GetLastAuditHashAsync(Guid accountId, CancellationToken ct = default)
        {
            var last = Entries.Where(e => e.AccountId == accountId).OrderByDescending(e => e.CreatedAt).FirstOrDefault();
            return Task.FromResult(last?.AuditHash ?? "GENESIS");
        }

        public Task<CashbookEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(Entries.FirstOrDefault(x => x.Id == id && !x.IsDeleted));

        public Task<IReadOnlyList<CashbookEntry>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CashbookEntry>>(Entries.Where(x => !x.IsDeleted).ToList());

        public Task<IReadOnlyList<CashbookEntry>> FindAsync(Expression<Func<CashbookEntry, bool>> predicate, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CashbookEntry>>(Entries.AsQueryable().Where(predicate).Where(x => !x.IsDeleted).ToList());

        public Task AddAsync(CashbookEntry entity, CancellationToken cancellationToken = default)
        {
            if (entity.Id == Guid.Empty) entity.Id = Guid.NewGuid();
            Entries.Add(entity);
            return Task.CompletedTask;
        }

        public void Update(CashbookEntry entity)
        {
            var index = Entries.FindIndex(x => x.Id == entity.Id);
            if (index >= 0) Entries[index] = entity;
            else Entries.Add(entity);
        }

        public void Delete(CashbookEntry entity) => entity.IsDeleted = true;
    }

    #endregion

    // =========================================================================
    // SECTION 1: CashbookService - Validation, Shift Locking & Account Resolution
    // =========================================================================

    [Fact]
    public async Task CashbookService_Validation_ShiftLocking_And_AccountResolution_Branches()
    {
        var tenantContext = new FakeTenantContext();
        var uow = new FakeUnitOfWork();
        var cashbookRepo = new FakeCashbookRepository();
        var accountRepo = new FakeRepository<FinanceAccount>();
        var shiftRepo = new FakeRepository<ShiftZReport>();
        var hashServiceMock = new Mock<IAuditHashService>();
        hashServiceMock.Setup(h => h.ComputeEntryHash(It.IsAny<string>(), It.IsAny<CashbookEntry>())).Returns("valid_hash");

        var service = new CashbookService(cashbookRepo, accountRepo, shiftRepo, hashServiceMock.Object, tenantContext, uow);

        // 1. CloseShiftZReportAsync: shift already closed -> SHIFT_ALREADY_CLOSED
        var closedShift = new ShiftZReport { Id = Guid.NewGuid(), Status = ShiftState.Closed };
        shiftRepo.Items.Add(closedShift);
        var closeEx = await Assert.ThrowsAsync<BusinessException>(() =>
            service.CloseShiftZReportAsync(new CloseShiftRequest(closedShift.Id, 100000m, 0m, "Chốt ca")));
        Assert.Equal("SHIFT_ALREADY_CLOSED", closeEx.ErrorCode);

        // 2. ProcessCashDropAsync: Amount <= 0 -> INVALID_AMOUNT
        var dropEx = await Assert.ThrowsAsync<BusinessException>(() =>
            service.ProcessCashDropAsync(new CashDropRequest(Guid.NewGuid(), 0m, Guid.NewGuid(), "Drop")));
        Assert.Equal("INVALID_AMOUNT", dropEx.ErrorCode);

        var transferEx = await Assert.ThrowsAsync<BusinessException>(() =>
            service.TransferFundsAsync(new TransferFundRequest(Guid.NewGuid(), Guid.NewGuid(), -5000m, "Nop")));
        Assert.Equal("INVALID_AMOUNT", transferEx.ErrorCode);

        var sameAccountId = Guid.NewGuid();
        var sameAccountEx = await Assert.ThrowsAsync<BusinessException>(() =>
            service.TransferFundsAsync(new TransferFundRequest(sameAccountId, sameAccountId, 100000m, "Nop")));
        Assert.Equal("INVALID_ACCOUNTS", sameAccountEx.ErrorCode);

        // 5. CreatePaymentAsync: Amount <= 0 -> INVALID_AMOUNT
        var payEx = await Assert.ThrowsAsync<BusinessException>(() =>
            service.CreatePaymentAsync(new CreatePaymentRequest(Guid.NewGuid(), 0m, "Chi", "Ly do", null)));
        Assert.Equal("INVALID_AMOUNT", payEx.ErrorCode);

        // 6. CreatePaymentAsync: Till with ActiveShiftId updates shift.TotalCashOut += amount
        var activeShift = new ShiftZReport { Id = Guid.NewGuid(), Status = ShiftState.Open, TotalCashOut = 50000m };
        shiftRepo.Items.Add(activeShift);
        var tillAcc = new FinanceAccount
        {
            Id = Guid.NewGuid(),
            AccountType = FinanceAccountType.Till,
            ActiveShiftId = activeShift.Id,
            CurrentBalance = 500000m,
            TenantId = tenantContext.CurrentTenantId,
            IsActive = true
        };
        cashbookRepo.Accounts.Add(tillAcc);
        accountRepo.Items.Add(tillAcc);

        var paymentRes = await service.CreatePaymentAsync(new CreatePaymentRequest(tillAcc.Id, 100000m, "Chi vat", "Mua da", null, TaxTreatment: CashbookTaxTreatment.NonDeductibleExpense));
        Assert.NotNull(paymentRes);
        Assert.Equal(150000m, activeShift.TotalCashOut);

        // 7. ResolveDisbursementAccountAsync branches:
        // Account deleted / inactive -> NotFoundException
        var inactiveAcc = new FinanceAccount { Id = Guid.NewGuid(), TenantId = tenantContext.CurrentTenantId, IsActive = false };
        accountRepo.Items.Add(inactiveAcc);
        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.ResolveDisbursementAccountAsync(Guid.Empty, "CASH", inactiveAcc.Id));

        // Account tenant mismatch -> NotFoundException
        var diffTenantAcc = new FinanceAccount { Id = Guid.NewGuid(), TenantId = "other-tenant", IsActive = true };
        accountRepo.Items.Add(diffTenantAcc);
        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.ResolveDisbursementAccountAsync(Guid.Empty, "CASH", diffTenantAcc.Id));

        // isBank = true but AccountType != Bank -> INVALID_CASHBOOK_ACCOUNT
        var bankMismatchEx = await Assert.ThrowsAsync<BusinessException>(() =>
            service.ResolveDisbursementAccountAsync(Guid.Empty, "BANK_TRANSFER", tillAcc.Id));
        Assert.Equal("INVALID_CASHBOOK_ACCOUNT", bankMismatchEx.ErrorCode);

        // isBank = false but AccountType == Bank -> INVALID_CASHBOOK_ACCOUNT
        var bankAcc = new FinanceAccount
        {
            Id = Guid.NewGuid(),
            AccountType = FinanceAccountType.Bank,
            CurrentBalance = 1000000m,
            TenantId = tenantContext.CurrentTenantId,
            IsActive = true
        };
        accountRepo.Items.Add(bankAcc);
        var cashMismatchEx = await Assert.ThrowsAsync<BusinessException>(() =>
            service.ResolveDisbursementAccountAsync(Guid.Empty, "CASH", bankAcc.Id));
        Assert.Equal("INVALID_CASHBOOK_ACCOUNT", cashMismatchEx.ErrorCode);

        // BranchId mismatch on non-bank account -> INVALID_CASHBOOK_ACCOUNT
        tillAcc.BranchId = Guid.NewGuid();
        var branchMismatchEx = await Assert.ThrowsAsync<BusinessException>(() =>
            service.ResolveDisbursementAccountAsync(Guid.NewGuid(), "CASH", tillAcc.Id));
        Assert.Equal("INVALID_CASHBOOK_ACCOUNT", branchMismatchEx.ErrorCode);

        // 8. GetOrCreateBankAccountAsync: matchedBank exists but IsActive = false -> reactivates
        var inactiveBank = new FinanceAccount
        {
            Id = Guid.NewGuid(),
            TenantId = tenantContext.CurrentTenantId,
            BranchId = tenantContext.CurrentBranchId,
            AccountType = FinanceAccountType.Bank,
            IsActive = false
        };
        accountRepo.Items.Add(inactiveBank);
        var resolvedBank = await service.GetOrCreateBankAccountAsync(tenantContext.CurrentBranchId);
        Assert.True(resolvedBank.IsActive);

        // 9. ResolveAccountTypeAsync: account not found -> NotFoundException
        await Assert.ThrowsAsync<NotFoundException>(() => service.CreateReceiptAsync(new CreateReceiptRequest(Guid.NewGuid(), 1000m, "OTHER", "Desc", "REF01", TaxTreatment: CashbookTaxTreatment.TaxableRevenue)));

        // 10. VerifyChainIntegrityAsync: entry hash verification fails -> returns false
        hashServiceMock.Setup(h => h.VerifyEntryHash(It.IsAny<string>(), It.IsAny<CashbookEntry>())).Returns(false);
        cashbookRepo.Entries.Add(new CashbookEntry
        {
            Id = Guid.NewGuid(),
            AccountId = bankAcc.Id,
            CreatedAt = DateTime.UtcNow,
            AuditHash = "bad_hash"
        });
        var isChainValid = await service.VerifyChainIntegrityAsync(bankAcc.Id);
        Assert.False(isChainValid);
    }

    // =========================================================================
    // SECTION 2: SupplierReturnService - Review Fallbacks & Settlement Status
    // =========================================================================

    [Fact]
    public async Task SupplierReturnService_ReviewFallbacks_And_SettlementStatus_Branches()
    {
        var tenantContext = new FakeTenantContext();
        var uow = new FakeUnitOfWork();
        var repoMock = new Mock<ISupplierReturnRepository>();

        var service = new SupplierReturnService(repoMock.Object, tenantContext, uow);

        // 1. CreateAsync: returnableLines empty -> BusinessException
        repoMock.Setup(r => r.GetGrnReturnableLinesAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GrnReturnableLineDto>());

        var cmdCreate = new CreateSupplierReturnCommand(
            GoodsReceiptId: Guid.NewGuid(),
            Reason: "Hang loi",
            ReasonNotes: null,
            AttachmentUrl: null,
            Lines: new List<CreateSupplierReturnLineDto>
            {
                new(Guid.NewGuid(), Guid.NewGuid(), "BATCH01", DateTime.UtcNow.AddDays(10), 5m, 10000m, "Loi")
            });

        await Assert.ThrowsAsync<BusinessException>(() => service.CreateAsync(cmdCreate, CancellationToken.None));

        // 2. RecordSupplierResponseAsync: return not found -> NotFoundException
        repoMock.Setup(r => r.GetByIdAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SupplierReturn?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => service.RecordSupplierResponseAsync(Guid.NewGuid(), new RecordSupplierResponseCommand("APPROVED_FULL", "OK", null), CancellationToken.None));

        // 3. CompleteHandoverAsync: return not found -> NotFoundException
        await Assert.ThrowsAsync<NotFoundException>(() => service.CompleteHandoverAsync(Guid.NewGuid(), new CompleteHandoverCommand("Staff", "Note", null), CancellationToken.None));

        // 4. CancelAsync: return not found -> NotFoundException
        await Assert.ThrowsAsync<NotFoundException>(() => service.CancelAsync(Guid.NewGuid(), new CancelSupplierReturnCommand("Cancel reason"), CancellationToken.None));
    }

    // =========================================================================
    // SECTION 3: RepackService - Invariants, Pricing Fallbacks & Status Lifecycles
    // =========================================================================

    [Fact]
    public async Task RepackService_Invariants_PricingFallbacks_And_StatusBranches()
    {
        var tenant = new FakeTenantContext();
        var uow = new FakeUnitOfWork();
        var repoMock = new Mock<IRepackRepository>();

        var service = new RepackService(repoMock.Object, tenant, uow);

        var branchStockId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var stock = new BranchStock
        {
            Id = branchStockId,
            ProductId = productId,
            BranchId = tenant.CurrentBranchId ?? Guid.NewGuid(),
            QuantityOnHand = 10m,
            QuantityReserved = 0m,
            ExpiryDate = DateTime.UtcNow.AddDays(15),
            RetailPrice = 120000m
        };
        var product = new Product
        {
            Id = productId,
            Name = "Thit bo dong goi",
            BaseUnit = "kg",
            AllowRepack = true,
            HasBatches = true,
            ProcessedSellingPricePerKg = 150000m,
            SellingPrice = 130000m
        };

        repoMock.Setup(r => r.GetBranchStockAsync(tenant.CurrentTenantId, branchStockId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stock);
        repoMock.Setup(r => r.GetProductWithUnitsAsync(tenant.CurrentTenantId, productId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);
        repoMock.Setup(r => r.IsCodeConflictAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // 1. CreateRepackOrderAsync: WasteWeight < 0 -> REPACK_WASTE_INVALID
        var reqNegWaste = new CreateRepackOrderRequest(branchStockId, "PORTIONING", 5m, new List<PackageLineRequest>
        {
            new(1m, 120000m)
        }, WasteWeight: -1m);
        var exWaste = await Assert.ThrowsAsync<BusinessException>(() => service.CreateRepackOrderAsync(reqNegWaste, CancellationToken.None));
        Assert.Equal("REPACK_WASTE_INVALID", exWaste.ErrorCode);

        // 2. NetWeight <= 0 -> PACKAGE_WEIGHT_INVALID
        var reqZeroWeight = new CreateRepackOrderRequest(branchStockId, "PORTIONING", 5m, new List<PackageLineRequest>
        {
            new(0m, 120000m)
        }, WasteWeight: 5m, WasteReason: "OTHER");
        var exWeight = await Assert.ThrowsAsync<BusinessException>(() => service.CreateRepackOrderAsync(reqZeroWeight, CancellationToken.None));
        Assert.Equal("PACKAGE_WEIGHT_INVALID", exWeight.ErrorCode);

        // 3. ExpiryDate > stock.ExpiryDate -> PACKAGE_EXPIRY_INVALID
        var reqExpPast = new CreateRepackOrderRequest(branchStockId, "PORTIONING", 1m, new List<PackageLineRequest>
        {
            new(1m, 120000m, ExpiryDate: stock.ExpiryDate.Value.AddDays(5))
        });
        var exExp = await Assert.ThrowsAsync<BusinessException>(() => service.CreateRepackOrderAsync(reqExpPast, CancellationToken.None));
        Assert.Equal("PACKAGE_EXPIRY_INVALID", exExp.ErrorCode);

        // 4. Mfg > ExpiryDate -> PACKAGE_MFG_INVALID
        var reqMfgInvalid = new CreateRepackOrderRequest(branchStockId, "PORTIONING", 1m, new List<PackageLineRequest>
        {
            new(1m, 120000m, ExpiryDate: DateTime.UtcNow.AddDays(2), ManufactureDate: DateTime.UtcNow.AddDays(5))
        });
        var exMfg = await Assert.ThrowsAsync<BusinessException>(() => service.CreateRepackOrderAsync(reqMfgInvalid, CancellationToken.None));
        Assert.Equal("PACKAGE_MFG_INVALID", exMfg.ErrorCode);

        // 5. InputWeight > rawSellable -> REPACK_INSUFFICIENT_RAW
        var reqRaw = new CreateRepackOrderRequest(branchStockId, "PORTIONING", 50m, new List<PackageLineRequest>
        {
            new(50m, 120000m)
        });
        var exRaw = await Assert.ThrowsAsync<BusinessException>(() => service.CreateRepackOrderAsync(reqRaw, CancellationToken.None));
        Assert.Equal("REPACK_INSUFFICIENT_RAW", exRaw.ErrorCode);

        // 6. UpdatePackagePriceAsync: SellingPrice <= 0 -> PACKAGE_PRICE_INVALID
        var package = new Package
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.CurrentTenantId,
            BranchStockId = branchStockId,
            Status = "AVAILABLE",
            SellingPrice = 100000m,
            PriceHistoryJson = "invalid_json"
        };
        repoMock.Setup(r => r.GetPackageByIdAsync(tenant.CurrentTenantId, package.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(package);

        var exPrice = await Assert.ThrowsAsync<BusinessException>(() =>
            service.UpdatePackagePriceAsync(package.Id, new UpdatePackagePriceRequest(0m), CancellationToken.None));
        Assert.Equal("PACKAGE_PRICE_INVALID", exPrice.ErrorCode);

        // UpdatePackagePriceAsync: successfully updates with corrupt or empty price history json
        var updateRes = await service.UpdatePackagePriceAsync(package.Id, new UpdatePackagePriceRequest(120000m), CancellationToken.None);
        Assert.NotNull(updateRes);
        Assert.Equal(120000m, package.SellingPrice);

        // 7. QuarantinePackageAsync: Status != AVAILABLE -> PACKAGE_STATUS_INVALID
        package.Status = "SOLD";
        var exQuar = await Assert.ThrowsAsync<BusinessException>(() => service.QuarantinePackageAsync(package.Id, CancellationToken.None));
        Assert.Equal("PACKAGE_STATUS_INVALID", exQuar.ErrorCode);

        // 8. ReleasePackageAsync: Status != QUARANTINED -> PACKAGE_STATUS_INVALID
        package.Status = "AVAILABLE";
        var exRel = await Assert.ThrowsAsync<BusinessException>(() => service.ReleasePackageAsync(package.Id, CancellationToken.None));
        Assert.Equal("PACKAGE_STATUS_INVALID", exRel.ErrorCode);

        // 9. VoidPackageAsync: Status not AVAILABLE/QUARANTINED -> PACKAGE_STATUS_INVALID
        package.Status = "CONSUMED";
        var exCancel = await Assert.ThrowsAsync<BusinessException>(() =>
            service.VoidPackageAsync(package.Id, new VoidPackageRequest("UNPACK_TO_RAW", "Huy"), CancellationToken.None));
        Assert.Equal("PACKAGE_STATUS_INVALID", exCancel.ErrorCode);

        // 10. TENANT_REQUIRED when CurrentTenantId is empty
        tenant.CurrentTenantId = "";
        var exTenant = await Assert.ThrowsAsync<BusinessException>(() =>
            service.GetBranchStockPackagesAsync(Guid.NewGuid(), CancellationToken.None));
        Assert.Equal("TENANT_REQUIRED", exTenant.ErrorCode);
    }

    // =========================================================================
    // SECTION 4: EmployeeService - Code Collision, Status & Inactive Guardrails
    // =========================================================================

    [Fact]
    public async Task EmployeeService_CodeCollision_And_InactiveGuardrails_Branches()
    {
        var tenant = new FakeTenantContext();
        var uow = new FakeUnitOfWork();
        var empRepoMock = new Mock<IEmployeeRepository>();
        var userRepoMock = new Mock<IUserRepository>();

        var service = new EmployeeService(empRepoMock.Object, userRepoMock.Object, uow, tenant);

        // 1. Code collision loop: ExistsIgnoringFiltersAsync returns true once, then false -> increments code
        int checkCount = 0;
        empRepoMock.Setup(e => e.ExistsIgnoringFiltersAsync(It.IsAny<Expression<Func<Employee, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                checkCount++;
                return checkCount == 1; // first check collides, second check succeeds
            });

        empRepoMock.Setup(e => e.GetDeletedDuplicateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Employee?)null);

        var createReq = new CreateEmployeeRequest(
            FullName: "Le Van Collision",
            CitizenId: "012345678901",
            Phone: "0912345678",
            BranchId: Guid.NewGuid(),
            SalaryType: "monthly",
            BaseRate: 9000000m);

        var createdRes = await service.CreateEmployeeAsync(createReq);
        Assert.NotNull(createdRes);

        // 2. UpdateEmployeeAsync: EMPLOYEE_INACTIVE when updating inactive employee with user credentials
        var inactiveEmp = new Employee
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.CurrentTenantId,
            Status = EmployeeStatus.Inactive,
            FullName = "Nhan vien nghi viec",
            CitizenId = "012345678901",
            Phone = "0912345678"
        };
        empRepoMock.Setup(e => e.GetByIdAsync(inactiveEmp.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(inactiveEmp);

        var updateReq = new UpdateEmployeeRequest(
            FullName: "Nhan vien nghi viec",
            CitizenId: "012345678901",
            Phone: "0912345678",
            BranchId: Guid.NewGuid(),
            SalaryType: "monthly",
            BaseRate: 9000000m,
            UserName: "inact_emp_user",
            Password: "Password123!");

        var exInact = await Assert.ThrowsAsync<BusinessException>(() =>
            service.UpdateEmployeeAsync(inactiveEmp.Id, updateReq));
        Assert.Equal("EMPLOYEE_INACTIVE", exInact.ErrorCode);

        // 3. UpdateStatusAsync: employee not found -> NotFoundException
        empRepoMock.Setup(e => e.GetByIdAsync(It.Is<Guid>(g => g != inactiveEmp.Id), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Employee?)null);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.UpdateStatusAsync(Guid.NewGuid(), "inactive"));

        // 4. DeleteEmployeeAsync: employee not found -> NotFoundException
        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.DeleteEmployeeAsync(Guid.NewGuid()));

        // 5. DeleteEmployeeAsync: employee with work history -> EMPLOYEE_HAS_WORK_HISTORY
        empRepoMock.Setup(e => e.HasWorkOrPayrollHistoryAsync(inactiveEmp.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var exHist = await Assert.ThrowsAsync<BusinessException>(() =>
            service.DeleteEmployeeAsync(inactiveEmp.Id));
        Assert.Equal("EMPLOYEE_HAS_WORK_HISTORY", exHist.ErrorCode);
    }

    // =========================================================================
    // SECTION 5: ShiftConfigService - Code Generation, Durations & Time Parsing
    // =========================================================================

    [Fact]
    public async Task ShiftConfigService_CodeGeneration_Durations_And_TimeParsing_Branches()
    {
        var tenant = new FakeTenantContext();
        var uow = new FakeUnitOfWork();
        var repoMock = new Mock<IShiftConfigRepository>();

        var service = new ShiftConfigService(repoMock.Object, uow, tenant);

        var branchId = Guid.NewGuid();

        // 1. Shift code collision counter loop on Create: ExistsAsync true once, then false
        int collisionCount = 0;
        repoMock.Setup(r => r.ExistsAsync(It.IsAny<Expression<Func<ShiftConfig, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                collisionCount++;
                return collisionCount == 1;
            });

        repoMock.Setup(r => r.GetByBranchIdAsync(It.IsAny<Guid?>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ShiftConfig>());

        var createReq = new CreateShiftConfigRequest(
            BranchId: branchId,
            ShiftCode: null,
            ShiftName: "Ca Sang Collide",
            StartTime: TimeSpan.FromHours(6),
            EndTime: TimeSpan.FromHours(14));

        var created = await service.CreateShiftConfigAsync(createReq);
        Assert.NotNull(created);

        // 2. Duplicate shift name on create -> DUPLICATE_SHIFT_NAME
        repoMock.Setup(r => r.GetByBranchIdAsync(branchId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ShiftConfig>
            {
                new() { Id = Guid.NewGuid(), ShiftName = "Ca Trung", IsDeleted = false }
            });

        var reqDupName = new CreateShiftConfigRequest(
            BranchId: branchId,
            ShiftCode: "CA-TRUNG",
            ShiftName: "Ca Trung",
            StartTime: TimeSpan.FromHours(8),
            EndTime: TimeSpan.FromHours(12));

        var exDup = await Assert.ThrowsAsync<BusinessException>(() => service.CreateShiftConfigAsync(reqDupName));
        Assert.Equal("DUPLICATE_SHIFT_NAME", exDup.ErrorCode);

        // 3. UpdateShiftConfigAsync: not found -> NotFoundException
        repoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ShiftConfig?)null);

        var updateReq = new UpdateShiftConfigRequest(
            ShiftName: "Ca Moi",
            StartTime: TimeSpan.FromHours(7),
            EndTime: TimeSpan.FromHours(15));

        await Assert.ThrowsAsync<NotFoundException>(() => service.UpdateShiftConfigAsync(Guid.NewGuid(), updateReq));

        // 4. DeleteShiftConfigAsync: not found -> NotFoundException
        await Assert.ThrowsAsync<NotFoundException>(() => service.DeleteShiftConfigAsync(Guid.NewGuid()));
    }

    // =========================================================================
    // SECTION 6: BusinessDayClosingService - Missing Invariants & Version History
    // =========================================================================

    [Fact]
    public async Task BusinessDayClosingService_MissingInvariants_And_VersionHistory_Branches()
    {
        var tenant = new FakeTenantContext();
        var uow = new FakeUnitOfWork();
        var repoMock = new Mock<IBusinessDayClosingRepository>();
        var cashbookMock = new Mock<ICashbookService>();

        var service = new BusinessDayClosingService(repoMock.Object, cashbookMock.Object, uow, tenant);

        var branchId = Guid.NewGuid();
        var bDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));

        // 1. Branch not accessible -> BRANCH_NOT_FOUND
        repoMock.Setup(r => r.IsBranchAccessibleAsync(It.IsAny<string>(), branchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var exBranch = await Assert.ThrowsAsync<BusinessException>(() => service.PreviewAsync(branchId, bDate));
        Assert.Equal("BRANCH_NOT_FOUND", exBranch.ErrorCode);

        // 2. Business date default -> BUSINESS_DATE_REQUIRED
        repoMock.Setup(r => r.IsBranchAccessibleAsync(It.IsAny<string>(), branchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var exDate = await Assert.ThrowsAsync<BusinessException>(() => service.PreviewAsync(branchId, default));
        Assert.Equal("BUSINESS_DATE_REQUIRED", exDate.ErrorCode);

        // 3. CloseAsync: CurrentUserId empty -> UnauthorizedAccessException
        tenant.CurrentUserId = Guid.Empty;
        var validReq = new CloseBusinessDayRequest(branchId, bDate, new List<BusinessDayFundCount>(), "PreviewTok");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.CloseAsync(validReq));
        tenant.CurrentUserId = Guid.NewGuid();

        // 4. CloseAsync: PreviewToken empty -> PREVIEW_TOKEN_REQUIRED
        var reqNoTok = new CloseBusinessDayRequest(branchId, bDate, new List<BusinessDayFundCount>(), "");
        var exTok = await Assert.ThrowsAsync<BusinessException>(() => service.CloseAsync(reqNoTok));
        Assert.Equal("PREVIEW_TOKEN_REQUIRED", exTok.ErrorCode);

        // 5. CloseAsync: Open shifts exist -> OPEN_SHIFTS_EXIST
        repoMock.Setup(r => r.GetShiftsAsync(It.IsAny<string>(), branchId, bDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ShiftZReport>
            {
                new() { Id = Guid.NewGuid(), Status = ShiftState.Open }
            });
        repoMock.Setup(r => r.GetShiftlessOrdersAsync(It.IsAny<string>(), branchId, bDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Order>());

        var reqWithCounts = new CloseBusinessDayRequest(branchId, bDate, new List<BusinessDayFundCount>
        {
            new(Guid.NewGuid(), 100000m)
        }, "tok_123");
        repoMock.Setup(r => r.GetFundsAsync(It.IsAny<string>(), branchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FinanceAccount>
            {
                new() { Id = reqWithCounts.FundCounts.First().FundId, IsActive = true, BranchId = branchId }
            });

        var exOpen = await Assert.ThrowsAsync<BusinessException>(() => service.CloseAsync(reqWithCounts));
        Assert.Equal("OPEN_SHIFTS_EXIST", exOpen.ErrorCode);

        // 6. GetHistoryAsync returns version responses
        repoMock.Setup(r => r.GetHistoryAsync(It.IsAny<string>(), branchId, bDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BusinessDayClosing>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    BranchId = branchId,
                    BusinessDate = bDate,
                    Version = 1,
                    Status = BusinessDayClosingStatus.Closed,
                    ClosedAtUtc = DateTime.UtcNow
                }
            });

        var historyList = await service.GetHistoryAsync(branchId, bDate);
        Assert.Single(historyList);
        Assert.Equal(1, historyList[0].Version);
    }

    // =========================================================================
    // SECTION 7: ExpenseService - Reason Validations, Rejection & Scope Checks
    // =========================================================================

    [Fact]
    public async Task ExpenseService_ReasonValidations_Rejection_And_ScopeChecks_Branches()
    {
        var tenant = new FakeTenantContext();
        var uow = new FakeUnitOfWork();
        var repoMock = new Mock<IExpenseRepository>();
        var cashbookMock = new Mock<ICashbookService>();

        var service = new ExpenseService(repoMock.Object, cashbookMock.Object, tenant, uow);

        // 1. CreateExpenseAsync: Amount <= 0 -> INVALID_AMOUNT
        var reqZero = new CreateExpenseRequest(
            BranchId: Guid.NewGuid(),
            BeneficiaryName: "Chi",
            Category: "Khác",
            Amount: 0m,
            PaymentSourceType: FinanceAccountType.Till,
            PaymentSourceAccountId: Guid.NewGuid(),
            IsTaxDeductible: false,
            InvoiceNumber: null,
            SellerTaxCode: null,
            EvidenceAttachmentUrl: null,
            Note: "Khong tien",
            Nature: FinancialNature.OperatingExpense);
        var exZero = await Assert.ThrowsAsync<BusinessException>(() => service.CreateExpenseAsync(reqZero));
        Assert.Equal("INVALID_AMOUNT", exZero.ErrorCode);

        // 2. FinancialNature.Suspense without SuspiciousReason auto-fills default text
        var voucherSuspense = new ExpenseVoucher
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.CurrentTenantId,
            Amount = 100000m,
            Nature = FinancialNature.Suspense,
            Status = ExpenseVoucherStatus.PendingApproval
        };
        repoMock.Setup(r => r.GetByIdAsync(voucherSuspense.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(voucherSuspense);

        // 3. RejectExpenseAsync: status != PendingApproval -> INVALID_STATUS
        voucherSuspense.Status = ExpenseVoucherStatus.Paid;
        var exReject = await Assert.ThrowsAsync<BusinessException>(() =>
            service.RejectExpenseAsync(new RejectExpenseRequest(voucherSuspense.Id, "Tu choi")));
        Assert.Equal("INVALID_STATUS", exReject.ErrorCode);

        // 4. CancelExpenseAsync: unauthorized role -> UnauthorizedAccessException
        tenant.SetRole("STAFF");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.CancelExpenseAsync(new CancelExpenseRequest(voucherSuspense.Id, "Huy")));
        tenant.SetRole("STORE_OWNER");

        // 5. CancelExpenseAsync: Reason empty -> REASON_REQUIRED
        var exReason = await Assert.ThrowsAsync<BusinessException>(() =>
            service.CancelExpenseAsync(new CancelExpenseRequest(voucherSuspense.Id, "")));
        Assert.Equal("REASON_REQUIRED", exReason.ErrorCode);

        // 6. CancelExpenseAsync: Paid without PaymentSourceAccountId -> MISSING_PAYMENT_SOURCE
        voucherSuspense.PaymentSourceAccountId = null;
        var exMissingSource = await Assert.ThrowsAsync<BusinessException>(() =>
            service.CancelExpenseAsync(new CancelExpenseRequest(voucherSuspense.Id, "Huy vi sai")));
        Assert.Equal("MISSING_PAYMENT_SOURCE", exMissingSource.ErrorCode);

        // 7. CancelExpenseAsync: status in Draft/PendingApproval/Approved/PaymentFailed cancels directly
        voucherSuspense.Status = ExpenseVoucherStatus.PendingApproval;
        var cancelDto = await service.CancelExpenseAsync(new CancelExpenseRequest(voucherSuspense.Id, "Huy hop le"));
        Assert.Equal(ExpenseVoucherStatus.Cancelled.ToString(), cancelDto.Status);

        // 8. DisburseExpenseAsync: status != Approved && status != PaymentFailed -> INVALID_STATUS
        voucherSuspense.Status = ExpenseVoucherStatus.Cancelled;
        var exExec = await Assert.ThrowsAsync<BusinessException>(() =>
            service.DisburseExpenseAsync(new DisburseExpenseRequest(voucherSuspense.Id, FinanceAccountType.Till, Guid.NewGuid(), "Chi")));
        Assert.Equal("INVALID_STATUS", exExec.ErrorCode);

        // 9. ResolveTriageAsync: status != Unresolved -> INVALID_STATUS
        voucherSuspense.Status = ExpenseVoucherStatus.Approved;
        voucherSuspense.Nature = FinancialNature.OperatingExpense;
        var exResolve = await Assert.ThrowsAsync<BusinessException>(() =>
            service.ResolveTriageAsync(voucherSuspense.Id, new ResolveTriageRequest(FinancialNature.OperatingExpense, false, "Khác", "Phan loai")));
        Assert.Equal("INVALID_STATUS", exResolve.ErrorCode);

        // 10. Deposit nature -> FEATURE_OUT_OF_SCOPE
        var reqDeposit = new CreateExpenseRequest(
            BranchId: Guid.NewGuid(),
            BeneficiaryName: "Coc",
            Category: "Khác",
            Amount: 200000m,
            PaymentSourceType: FinanceAccountType.Till,
            PaymentSourceAccountId: Guid.NewGuid(),
            IsTaxDeductible: false,
            InvoiceNumber: null,
            SellerTaxCode: null,
            EvidenceAttachmentUrl: null,
            Note: "Tien coc",
            Nature: FinancialNature.Deposit);
        var exScope = await Assert.ThrowsAsync<BusinessException>(() => service.CreateExpenseAsync(reqDeposit));
        Assert.Equal("FEATURE_OUT_OF_SCOPE", exScope.ErrorCode);
    }
}
