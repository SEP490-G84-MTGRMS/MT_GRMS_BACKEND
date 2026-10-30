using System.Linq.Expressions;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Suppliers;
using MT_GRMS.Application.Suppliers.Dtos;
using MT_GRMS.Domain.Entities.Supplier;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Domain.Exceptions;
using Xunit;

namespace MT_GRMS.UnitTests;

public class SupplierManagementTests
{
    private class FakeTenantContext : ITenantContext
    {
        public string CurrentTenantId { get; set; } = "tenant-test-01";
        public Guid CurrentUserId { get; set; } = Guid.Parse("11111111-1111-1111-1111-111111111111");
        public Guid? CurrentBranchId { get; set; } = null;
        public string CurrentRole { get; set; } = "STORE_OWNER";
        public void SetContext(string tenantId, Guid userId, Guid? branchId)
        {
            CurrentTenantId = tenantId;
            CurrentUserId = userId;
            CurrentBranchId = branchId;
        }
        public void SetRole(string? role) => CurrentRole = role?.Trim() ?? string.Empty;
    }

    private class FakeUnitOfWork : IUnitOfWork
    {
        public int SaveChangesCallCount { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesCallCount++;
            return Task.FromResult(1);
        }
    }

    private class FakeSupplierRepository : ISupplierRepository
    {
        public List<Supplier> Suppliers { get; } = new();
        public List<SupplierAPLedger> Ledgers { get; } = new();
        public List<SupplierDeliveryConfig> DeliveryConfigs { get; } = new();
        public List<SupplierBlackoutDate> BlackoutDates { get; } = new();

        public Task AddAsync(Supplier entity, CancellationToken cancellationToken = default)
        {
            Suppliers.Add(entity);
            return Task.CompletedTask;
        }

        public void Delete(Supplier entity)
        {
            entity.IsDeleted = true;
        }

        public void Update(Supplier entity)
        {
            // in-memory tracking
        }

        public Task<Supplier?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Suppliers.FirstOrDefault(s => s.Id == id && !s.IsDeleted));
        }

        public Task<IReadOnlyList<Supplier>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            IReadOnlyList<Supplier> list = Suppliers.Where(s => !s.IsDeleted).ToList();
            return Task.FromResult(list);
        }

        public Task<IReadOnlyList<Supplier>> FindAsync(Expression<Func<Supplier, bool>> predicate, CancellationToken cancellationToken = default)
        {
            var compiled = predicate.Compile();
            IReadOnlyList<Supplier> list = Suppliers.Where(compiled).ToList();
            return Task.FromResult(list);
        }

        public Task<Supplier?> GetByIdWithDetailsAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var supplier = Suppliers.FirstOrDefault(s => s.Id == id && !s.IsDeleted);
            if (supplier != null)
            {
                supplier.DeliveryConfig = DeliveryConfigs.FirstOrDefault(c => c.SupplierId == id);
                supplier.BlackoutDates = BlackoutDates.Where(b => b.SupplierId == id).ToList();
            }
            return Task.FromResult(supplier);
        }

        public Task<bool> ExistsByCodeAsync(string code, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Suppliers.Any(s => s.Code.Equals(code, StringComparison.OrdinalIgnoreCase) && !s.IsDeleted));
        }

        public Task<int> CountTodayAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Suppliers.Count);
        }

        public Task<(IReadOnlyList<Supplier> Items, int TotalCount)> GetPagedListAsync(
            string? searchTerm,
            SupplierType? supplierType,
            SupplierStatus? status,
            Guid? categoryId,
            int pageIndex,
            int pageSize,
            string? code = null,
            string? name = null,
            string? contactPerson = null,
            string? phone = null,
            CancellationToken cancellationToken = default)
        {
            var query = Suppliers.Where(s => !s.IsDeleted).AsEnumerable();

            if (!string.IsNullOrWhiteSpace(code)) query = query.Where(s => s.Code.ToLower().Contains(code.ToLower()));
            if (!string.IsNullOrWhiteSpace(name)) query = query.Where(s => s.Name.ToLower().Contains(name.ToLower()));
            if (!string.IsNullOrWhiteSpace(contactPerson)) query = query.Where(s => s.ContactPerson.ToLower().Contains(contactPerson.ToLower()));
            if (!string.IsNullOrWhiteSpace(phone)) query = query.Where(s => s.Phone.Contains(phone));

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.ToLower();
                query = query.Where(s => s.Name.ToLower().Contains(term) || s.Code.ToLower().Contains(term) || s.Phone.Contains(term));
            }
            if (supplierType.HasValue) query = query.Where(s => s.SupplierType == supplierType.Value);
            if (status.HasValue) query = query.Where(s => s.Status == status.Value);
            if (categoryId.HasValue) query = query.Where(s => s.CategoryId == categoryId.Value);

            var list = query.ToList();
            var paged = list.Skip((pageIndex - 1) * pageSize).Take(pageSize).ToList();
            return Task.FromResult(((IReadOnlyList<Supplier>)paged, list.Count));
        }

        public Task<bool> HasLedgerTransactionsAsync(Guid supplierId, APDocType docType, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Ledgers.Any(l => l.SupplierId == supplierId && l.DocType == docType));
        }

        public Task<SupplierAPLedger?> GetLedgerByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Ledgers.FirstOrDefault(l => l.IdempotencyKey == idempotencyKey));
        }

        public Task AddLedgerAsync(SupplierAPLedger ledger, CancellationToken cancellationToken = default)
        {
            Ledgers.Add(ledger);
            return Task.CompletedTask;
        }

        public Task<(IReadOnlyList<SupplierAPLedger> Items, int TotalCount)> GetPagedLedgerAsync(
            Guid supplierId,
            DateTime? fromDate,
            DateTime? toDate,
            int pageIndex,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            var query = Ledgers.Where(l => l.SupplierId == supplierId).AsEnumerable();
            if (fromDate.HasValue) query = query.Where(l => l.TransactionDate >= fromDate.Value);
            if (toDate.HasValue) query = query.Where(l => l.TransactionDate <= toDate.Value);

            var list = query.OrderByDescending(l => l.TransactionDate).ToList();
            var paged = list.Skip((pageIndex - 1) * pageSize).Take(pageSize).ToList();
            return Task.FromResult(((IReadOnlyList<SupplierAPLedger>)paged, list.Count));
        }

        public Task<SupplierDeliveryConfig?> GetDeliveryConfigAsync(Guid supplierId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(DeliveryConfigs.FirstOrDefault(c => c.SupplierId == supplierId));
        }

        public Task AddDeliveryConfigAsync(SupplierDeliveryConfig config, CancellationToken cancellationToken = default)
        {
            DeliveryConfigs.Add(config);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<SupplierBlackoutDate>> GetBlackoutDatesAsync(Guid supplierId, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<SupplierBlackoutDate> list = BlackoutDates.Where(b => b.SupplierId == supplierId).ToList();
            return Task.FromResult(list);
        }

        public Task ReplaceBlackoutDatesAsync(Guid supplierId, Guid configId, IEnumerable<SupplierBlackoutDate> blackoutDates, CancellationToken cancellationToken = default)
        {
            BlackoutDates.RemoveAll(b => b.SupplierId == supplierId);
            BlackoutDates.AddRange(blackoutDates);
            return Task.CompletedTask;
        }

        public List<SupplierProduct> SupplierProducts { get; } = new();
        public List<MT_GRMS.Domain.Entities.Catalog.Product> Products { get; } = new();

        public Task<SupplierProduct?> GetSupplierProductAsync(Guid supplierId, Guid productId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(SupplierProducts.FirstOrDefault(sp => sp.SupplierId == supplierId && sp.ProductId == productId));
        }

        public Task<(IReadOnlyList<SupplierProduct> Items, int TotalCount)> GetPagedSupplierProductsAsync(
            Guid supplierId,
            string? searchTerm,
            Guid? categoryId,
            SupplierProductStatus? status,
            int pageIndex,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            var query = SupplierProducts.AsEnumerable();
            if (supplierId != Guid.Empty) query = query.Where(sp => sp.SupplierId == supplierId);
            if (status.HasValue) query = query.Where(sp => sp.Status == status.Value);
            var list = query.ToList();
            var paged = list.Skip((pageIndex - 1) * pageSize).Take(pageSize).ToList();
            return Task.FromResult(((IReadOnlyList<SupplierProduct>)paged, list.Count));
        }

        public Task AddSupplierProductAsync(SupplierProduct supplierProduct, CancellationToken cancellationToken = default)
        {
            SupplierProducts.Add(supplierProduct);
            return Task.CompletedTask;
        }

        public Task ClearPrimarySupplierFlagAsync(Guid productId, Guid? excludeSupplierId = null, CancellationToken cancellationToken = default)
        {
            var primaries = SupplierProducts.Where(sp => sp.ProductId == productId && sp.IsPrimarySupplier);
            if (excludeSupplierId.HasValue && excludeSupplierId != Guid.Empty)
            {
                primaries = primaries.Where(sp => sp.SupplierId != excludeSupplierId.Value);
            }
            foreach (var sp in primaries)
            {
                sp.IsPrimarySupplier = false;
            }
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<SupplierProduct>> GetSuppliersForProductAsync(Guid productId, bool onlyActive = false, CancellationToken cancellationToken = default)
        {
            var query = SupplierProducts.Where(sp => sp.ProductId == productId);
            if (onlyActive)
            {
                query = query.Where(sp => sp.Status == SupplierProductStatus.Active);
            }
            return Task.FromResult((IReadOnlyList<SupplierProduct>)query.ToList());
        }

        public Task<bool> ProductExistsAsync(Guid productId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Products.Any(p => p.Id == productId));
        }

        public Task<MT_GRMS.Domain.Entities.Catalog.Product?> GetProductByIdAsync(Guid productId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Products.FirstOrDefault(p => p.Id == productId));
        }

        public List<SupplierBranchAssignment> BranchAssignments { get; } = new();

        public Task<(IReadOnlyList<SupplierBranchAssignment> Items, int TotalCount)> GetPagedBranchAssignmentsAsync(
            string? supplierSearch,
            string? branchSearch,
            Guid? supplierId,
            Guid? branchId,
            int pageIndex,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            var query = BranchAssignments.AsEnumerable();
            if (supplierId.HasValue && supplierId.Value != Guid.Empty)
            {
                query = query.Where(x => x.SupplierId == supplierId.Value);
            }
            if (branchId.HasValue && branchId.Value != Guid.Empty)
            {
                query = query.Where(x => x.BranchId == branchId.Value);
            }
            var list = query.ToList();
            var paged = list.Skip((pageIndex - 1) * pageSize).Take(pageSize).ToList();
            return Task.FromResult(((IReadOnlyList<SupplierBranchAssignment>)paged, list.Count));
        }


        public Task<bool> BranchAssignmentExistsAsync(Guid supplierId, Guid branchId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(BranchAssignments.Any(x => x.SupplierId == supplierId && x.BranchId == branchId));
        }

        public Task AddBranchAssignmentAsync(SupplierBranchAssignment assignment, CancellationToken cancellationToken = default)
        {
            BranchAssignments.Add(assignment);
            return Task.CompletedTask;
        }

        public Task RemoveBranchAssignmentAsync(Guid supplierId, Guid branchId, CancellationToken cancellationToken = default)
        {
            var item = BranchAssignments.FirstOrDefault(x => x.SupplierId == supplierId && x.BranchId == branchId);
            if (item != null)
            {
                BranchAssignments.Remove(item);
            }
            return Task.CompletedTask;
        }
    }



    [Fact]
    public async Task REQ01_CreateSupplier_ValidData_GeneratesCodeAndSucceeds()
    {
        // ARRANGE
        var repo = new FakeSupplierRepository();
        var uow = new FakeUnitOfWork();
        var service = new SupplierService(repo, uow, new FakeTenantContext());

        var cmd = new CreateSupplierCommand
        {
            Name = "Công ty Thực phẩm Fresh Farm",
            SupplierType = SupplierType.Goods,
            Phone = "0901234567",
            PaymentTermDays = 30,
            CreditLimit = 50000000
        };

        // ACT
        var result = await service.CreateSupplierAsync(cmd);

        // ASSERT
        Assert.NotNull(result);
        Assert.StartsWith("SUP-", result.Code);
        Assert.Equal("Công ty Thực phẩm Fresh Farm", result.Name);
        Assert.Equal(SupplierType.Goods, result.SupplierType);
        Assert.Equal(0, result.CurrentDebt);
        Assert.Equal(SupplierStatus.Active, result.Status);
        Assert.Equal(1, uow.SaveChangesCallCount);
    }

    [Fact]
    public async Task REQ01_CreateSupplier_DuplicateCode_ThrowsBusinessException()
    {
        // ARRANGE
        var repo = new FakeSupplierRepository();
        var uow = new FakeUnitOfWork();
        var service = new SupplierService(repo, uow, new FakeTenantContext());

        await repo.AddAsync(new Supplier
        {
            Code = "SUP-001",
            Name = "Nhà cung cấp hiện có",
            Phone = "0988888888"
        });

        var cmd = new CreateSupplierCommand
        {
            Code = "SUP-001",
            Name = "Nhà cung cấp trùng mã",
            Phone = "0911111111"
        };

        // ACT & ASSERT
        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.CreateSupplierAsync(cmd));
        Assert.Equal("DUPLICATE_SUPPLIER_CODE", ex.ErrorCode);
    }

    [Fact]
    public async Task REQ02_UpdateSupplier_ExpandGoodsToBoth_Succeeds()
    {
        // ARRANGE
        var repo = new FakeSupplierRepository();
        var uow = new FakeUnitOfWork();
        var service = new SupplierService(repo, uow, new FakeTenantContext());

        var supplier = new Supplier
        {
            Id = Guid.NewGuid(),
            Code = "SUP-002",
            Name = "NCC Hàng Khai Anh",
            SupplierType = SupplierType.Goods,
            Phone = "0902222222"
        };
        await repo.AddAsync(supplier);

        var updateCmd = new UpdateSupplierCommand
        {
            Name = "NCC Hàng Khai Anh",
            SupplierType = SupplierType.Both,
            Phone = "0902222222"
        };

        // ACT
        var result = await service.UpdateSupplierAsync(supplier.Id, updateCmd);

        // ASSERT
        Assert.Equal(SupplierType.Both, result.SupplierType);
    }

    [Fact]
    public async Task REQ02_UpdateSupplier_NarrowBothToGoods_WhenServiceInvoiceExists_ThrowsBusinessException()
    {
        // ARRANGE
        var repo = new FakeSupplierRepository();
        var uow = new FakeUnitOfWork();
        var service = new SupplierService(repo, uow, new FakeTenantContext());

        var supplierId = Guid.NewGuid();
        var supplier = new Supplier
        {
            Id = supplierId,
            Code = "SUP-003",
            Name = "Công ty Tiện ích Green",
            SupplierType = SupplierType.Both,
            Phone = "0903333333"
        };
        await repo.AddAsync(supplier);

        // Đã từng phát sinh hóa đơn dịch vụ
        await repo.AddLedgerAsync(new SupplierAPLedger
        {
            SupplierId = supplierId,
            DocType = APDocType.ServiceInvoice,
            DocNo = "SVC-001",
            Amount = 1000000,
            Direction = 1
        });

        var updateCmd = new UpdateSupplierCommand
        {
            Name = "Công ty Tiện ích Green",
            SupplierType = SupplierType.Goods, // Cố gắng thu hẹp về Goods
            Phone = "0903333333"
        };

        // ACT & ASSERT (BR-S07)
        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.UpdateSupplierAsync(supplierId, updateCmd));
        Assert.Equal("INVALID_SUPPLIER_TYPE_NARROWING", ex.ErrorCode);
    }

    [Fact]
    public async Task REQ03_UpdateStatus_ToInactive_WhenDebtIsZero_Succeeds()
    {
        // ARRANGE
        var repo = new FakeSupplierRepository();
        var uow = new FakeUnitOfWork();
        var service = new SupplierService(repo, uow, new FakeTenantContext());

        var supplier = new Supplier
        {
            Id = Guid.NewGuid(),
            Code = "SUP-004",
            Name = "NCC Rau Củ Quả",
            CurrentDebt = 0,
            Status = SupplierStatus.Active,
            Phone = "0904444444"
        };
        await repo.AddAsync(supplier);

        // ACT
        var result = await service.UpdateStatusAsync(supplier.Id, new UpdateSupplierStatusRequest
        {
            Status = SupplierStatus.Inactive
        });

        // ASSERT
        Assert.Equal(SupplierStatus.Inactive, result.Status);
    }

    [Fact]
    public async Task REQ03_UpdateStatus_ToInactive_WhenDebtExists_SucceedsWithWarningCount()
    {
        var repo = new FakeSupplierRepository();
        var uow = new FakeUnitOfWork();
        var service = new SupplierService(repo, uow, new FakeTenantContext());

        var supplier = new Supplier
        {
            Id = Guid.NewGuid(),
            Code = "SUP-005",
            Name = "Công ty Đông Lạnh Frozen Food",
            CurrentDebt = 15000000,
            Status = SupplierStatus.Active,
            Phone = "0905555555"
        };
        await repo.AddAsync(supplier);

        var result = await service.UpdateStatusAsync(supplier.Id, new UpdateSupplierStatusRequest
        {
            Status = SupplierStatus.Inactive
        });

        Assert.Equal(SupplierStatus.Inactive, result.Status);
    }

    [Fact]
    public async Task REQ04_RecordAPTransaction_IncreasesDebtAndUpdatesBalanceAfter()
    {
        // ARRANGE
        var repo = new FakeSupplierRepository();
        var uow = new FakeUnitOfWork();
        var service = new SupplierService(repo, uow, new FakeTenantContext());

        var supplier = new Supplier
        {
            Id = Guid.NewGuid(),
            Code = "SUP-006",
            Name = "Nhà cung cấp A",
            CurrentDebt = 10000000,
            Phone = "0906666666"
        };
        await repo.AddAsync(supplier);

        var cmd = new RecordAPTransactionCommand
        {
            SupplierId = supplier.Id,
            Amount = 5000000,
            Direction = 1, // Tăng nợ
            DocType = APDocType.PurchaseInvoice,
            DocNo = "GRN-20261010-001",
            IdempotencyKey = "GRN-20261010-001"
        };

        // ACT
        var result = await service.RecordAPTransactionAsync(cmd);

        // ASSERT
        Assert.Equal(15000000, result.BalanceAfter);
        Assert.Equal(15000000, supplier.CurrentDebt);
        Assert.Single(repo.Ledgers);
    }

    [Fact]
    public async Task REQ04_RecordAPTransaction_DuplicateIdempotencyKey_ReturnsExistingWithoutDoubleCount()
    {
        // ARRANGE
        var repo = new FakeSupplierRepository();
        var uow = new FakeUnitOfWork();
        var service = new SupplierService(repo, uow, new FakeTenantContext());

        var supplier = new Supplier
        {
            Id = Guid.NewGuid(),
            Code = "SUP-007",
            Name = "Nhà cung cấp B",
            CurrentDebt = 15000000,
            Phone = "0907777777"
        };
        await repo.AddAsync(supplier);

        // Đã có transaction trước đó
        await repo.AddLedgerAsync(new SupplierAPLedger
        {
            Id = Guid.NewGuid(),
            SupplierId = supplier.Id,
            Amount = 5000000,
            Direction = 1,
            BalanceAfter = 15000000,
            DocNo = "GRN-20261010-001",
            IdempotencyKey = "GRN-20261010-001"
        });

        var cmd = new RecordAPTransactionCommand
        {
            SupplierId = supplier.Id,
            Amount = 5000000,
            Direction = 1,
            DocNo = "GRN-20261010-001",
            IdempotencyKey = "GRN-20261010-001"
        };

        // ACT (Retry lại request)
        var result = await service.RecordAPTransactionAsync(cmd);

        // ASSERT (BR-S08: Không cộng dồn thêm tiền, giữ nguyên nợ 15,000,000)
        Assert.Equal(15000000, result.BalanceAfter);
        Assert.Equal(15000000, supplier.CurrentDebt);
        Assert.Single(repo.Ledgers); // Vẫn chỉ có 1 bản ghi
    }

    [Fact]
    public async Task REQ05_LinkCustomer_DualRole_SucceedsWithoutInterferingDebt()
    {
        // ARRANGE
        var repo = new FakeSupplierRepository();
        var uow = new FakeUnitOfWork();
        var service = new SupplierService(repo, uow, new FakeTenantContext());

        var supplier = new Supplier
        {
            Id = Guid.NewGuid(),
            Code = "SUP-008",
            Name = "Đại lý Bán lẻ & Cung ứng An Bình",
            CurrentDebt = 8000000,
            Phone = "0908888888"
        };
        await repo.AddAsync(supplier);

        var customerId = Guid.NewGuid();

        // ACT
        var result = await service.LinkCustomerAsync(supplier.Id, new LinkCustomerRequest { CustomerId = customerId });

        // ASSERT (BR-S11)
        Assert.Equal(customerId, result.LinkedCustomerId);
        Assert.Equal(8000000, result.CurrentDebt); // Công nợ AP giữ nguyên
    }

    [Fact]
    public void REQDEL01_CalculateEarliestDeliveryDate_AfterCutoffOnFriday_SkipsSundayAndSelectsNextMonday()
    {
        // ARRANGE:
        // Đơn tạo vào Thứ Sáu 2026-10-16 lúc 16:30 (vượt Cutoff 16:00)
        // LeadTime: 24h
        // Giao hàng Thứ 2 - Thứ 7 (nghỉ Chủ nhật)
        var orderTime = new DateTime(2026, 10, 16, 16, 30, 0, DateTimeKind.Utc); // Friday
        int leadTimeHours = 24;
        string cutoffTime = "16:00";
        int weekdaysMask = 63; // Mon (1) + Tue (2) + Wed (4) + Thu (8) + Fri (16) + Sat (32) = 63 (Sun = 64 là nghỉ)

        // ACT
        var earliest = DeliveryScheduleCalculator.CalculateEarliestDeliveryDate(
            orderTime,
            leadTimeHours,
            cutoffTime,
            weekdaysMask,
            blackoutDates: null);

        // ASSERT:
        // Vượt Cutoff Thứ 6 -> tính từ Thứ 7 (2026-10-17).
        // +24h Lead time -> Chủ nhật (2026-10-18), nhưng Chủ nhật nghỉ giao hàng!
        // -> Ngày giao sớm nhất hợp lệ là Thứ 2 tuần kế tiếp (2026-10-19).
        Assert.Equal(DayOfWeek.Monday, earliest.DayOfWeek);
        Assert.Equal(new DateTime(2026, 10, 19), earliest.Date);
    }

    [Fact]
    public void REQDEL02_CalculateEarliestDeliveryDate_WhenHolidayBlackoutDatesExist_SkipsHolidays()
    {
        // ARRANGE:
        // Đặt hàng Thứ 2 (2026-08-31) lúc 10:00 (trước Cutoff 17:00)
        // LeadTime: 24h -> Đến Thứ 3 (2026-09-01)
        // Blackout Date: Lễ Quốc khánh 2026-09-01 đến 2026-09-03
        var orderTime = new DateTime(2026, 8, 31, 10, 0, 0, DateTimeKind.Utc); // Monday
        int leadTimeHours = 24;
        string cutoffTime = "17:00";
        int weekdaysMask = 127; // Nhận giao tất cả các ngày

        var blackoutDates = new List<SupplierBlackoutDate>
        {
            new()
            {
                StartDate = new DateTime(2026, 9, 1),
                EndDate = new DateTime(2026, 9, 3),
                Reason = "Nghỉ lễ Quốc khánh 2/9",
                IsRecurringAnnual = false
            }
        };

        // ACT
        var earliest = DeliveryScheduleCalculator.CalculateEarliestDeliveryDate(
            orderTime,
            leadTimeHours,
            cutoffTime,
            weekdaysMask,
            blackoutDates);

        // ASSERT:
        // Sau nghỉ lễ là ngày 2026-09-04 (Thứ Sáu)
        Assert.Equal(new DateTime(2026, 9, 4), earliest.Date);
        Assert.Equal(DayOfWeek.Friday, earliest.DayOfWeek);
    }

    [Fact]
    public async Task REQ_SBA01_AssignSupplierToBranches_Success()
    {
        // ARRANGE
        var repo = new FakeSupplierRepository();
        var uow = new FakeUnitOfWork();
        var service = new SupplierService(repo, uow, new FakeTenantContext());

        var supplier = new Supplier
        {
            Id = Guid.NewGuid(),
            Name = "Vinamilk",
            Code = "SUP-001",
            Status = SupplierStatus.Active,
            TenantId = "tenant-test-01"
        };
        await repo.AddAsync(supplier);

        var branch1 = Guid.NewGuid();
        var branch2 = Guid.NewGuid();

        var cmd = new AssignSupplierToBranchesCommand
        {
            SupplierId = supplier.Id,
            BranchIds = new List<Guid> { branch1, branch2 }
        };

        // ACT
        var result = await service.AssignSupplierToBranchesAsync(cmd);

        // ASSERT
        Assert.Equal(2, result.Count);
        Assert.Equal(2, repo.BranchAssignments.Count);
        Assert.Contains(repo.BranchAssignments, a => a.BranchId == branch1);
        Assert.Contains(repo.BranchAssignments, a => a.BranchId == branch2);
    }

    [Fact]
    public async Task REQ_SBA02_UnassignSupplierBranch_Success()
    {
        // ARRANGE
        var repo = new FakeSupplierRepository();
        var uow = new FakeUnitOfWork();
        var service = new SupplierService(repo, uow, new FakeTenantContext());

        var supplier = new Supplier
        {
            Id = Guid.NewGuid(),
            Name = "Vinamilk",
            Code = "SUP-001",
            Status = SupplierStatus.Active,
            TenantId = "tenant-test-01"
        };
        await repo.AddAsync(supplier);

        var branchId = Guid.NewGuid();
        await repo.AddBranchAssignmentAsync(new SupplierBranchAssignment
        {
            Id = Guid.NewGuid(),
            SupplierId = supplier.Id,
            BranchId = branchId
        });

        // ACT
        await service.UnassignSupplierBranchAsync(supplier.Id, branchId);

        // ASSERT
        Assert.Empty(repo.BranchAssignments);
    }
}


