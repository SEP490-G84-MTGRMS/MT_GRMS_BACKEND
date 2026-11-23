using System;
using System.Collections.Generic;
using System.Threading;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.PosShifts;
using MT_GRMS.Application.PosShifts.Dtos;
using MT_GRMS.Application.HR.Employees.Dtos;
using MT_GRMS.Application.HR.Payslips.Dtos;
using MT_GRMS.Application.HR.Schedules.Dtos;
using MT_GRMS.Application.HR.ShiftConfigs.Dtos;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Entities.HR;
using MT_GRMS.Domain.Entities.Identity;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Entities.Sales;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Infrastructure.Persistence;
using MT_GRMS.Infrastructure.Repositories;
using Xunit;

namespace MT_GRMS.IntegrationTests;

public class HrAndPosRepositoriesIntegrationTests
{
    private const string TenantId = "tenant-hr-repo-01";

    private class TestTenantContext : ITenantContext
    {
        public string CurrentTenantId { get; set; } = TenantId;
        public Guid CurrentUserId { get; set; } = Guid.NewGuid();
        public Guid? CurrentBranchId { get; set; } = null;
        public void SetContext(string tenantId, Guid userId, Guid? branchId)
        {
            CurrentTenantId = tenantId;
            CurrentUserId = userId;
            CurrentBranchId = branchId;
        }
        public string CurrentRole { get; set; } = "ADMIN";
        public void SetRole(string? role) => CurrentRole = role ?? string.Empty;
    }

    private (AppDbContext Context, TestTenantContext Tenant) CreateDbContext()
    {
        var tenant = new TestTenantContext();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"HrRepoDb_{Guid.NewGuid()}")
            .Options;

        return (new AppDbContext(options, tenant), tenant);
    }

    [Fact]
    public async Task EmployeeRepository_AllMethods_ShouldWork()
    {
        var (context, _) = CreateDbContext();
        using (context)
        {
            var repo = new EmployeeRepository(context);

            var branch = new Branch
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                BranchCode = "BR-EMP",
                Name = "Chi nhánh NV",
                CreatedAt = DateTime.UtcNow
            };
            var emp = new Employee
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                BranchId = branch.Id,
                Branch = branch,
                EmployeeCode = "NV001",
                FullName = "Nguyen Van NV",
                Phone = "0934567890",
                CitizenId = "001200000001",
                Status = EmployeeStatus.Active,
                SalaryType = SalaryType.Monthly,
                CreatedAt = DateTime.UtcNow
            };
            await context.Branches.AddAsync(branch);
            await repo.AddAsync(emp);
            await context.SaveChangesAsync();

            // 1. GetByIdAsync
            Assert.NotNull(await repo.GetByIdAsync(emp.Id));

            // 2. GetByCodeAsync
            Assert.NotNull(await repo.GetByCodeAsync("nv001"));

            // 3. GetByPhoneAsync
            Assert.NotNull(await repo.GetByPhoneAsync("0934567890"));

            // 4. GetByCitizenIdAsync
            Assert.NotNull(await repo.GetByCitizenIdAsync("001200000001"));

            // 5. GetPagedListAsync with filters
            var (items, count) = await repo.GetPagedListAsync(new EmployeeQueryFilter
            {
                Keyword = "NV001",
                BranchId = branch.Id,
                Status = "Active",
                SalaryType = "Monthly",
                Page = 1,
                PageSize = 10
            });
            Assert.Equal(1, count);
            Assert.Single(items);

            // 6. GetByUserIdAsync
            var userId = Guid.NewGuid().ToString();
            emp.UserId = userId;
            await context.SaveChangesAsync();
            Assert.NotNull(await repo.GetByUserIdAsync(userId));

            // 7. GetMaxSequentialCodeAsync
            var maxCode = await repo.GetMaxSequentialCodeAsync("NV", 5);
            Assert.Equal("NV001", maxCode);

            // 8. ExistsAsync & ExistsIgnoringFiltersAsync
            Assert.True(await repo.ExistsAsync(e => e.EmployeeCode == "NV001"));
            Assert.True(await repo.ExistsIgnoringFiltersAsync(e => e.EmployeeCode == "NV001"));

            // 9. GetDeletedDuplicateAsync
            var dup = await repo.GetDeletedDuplicateAsync(TenantId, "001200000001", "0934567890");
            Assert.Null(dup);

            // 10. Update & Allowances full lifecycle (Insert, Update, Delete)
            emp.FullName = "Nguyen Van Updated";
            repo.Update(emp);

            // Step 1: Add initial allowance
            repo.SyncAllowances(emp, new List<EmployeeAllowanceDto>
            {
                new("Xang xe", 200_000m)
            }, TenantId);
            await context.SaveChangesAsync();
            var allow1 = emp.Allowances.First();

            // Step 2: Update existing + Add without ID + Add with non-matching ID
            repo.SyncAllowances(emp, new List<EmployeeAllowanceDto>
            {
                new("Xang xe updated", 250_000m, allow1.Id),
                new("Com trua", 300_000m),
                new("Thuong ngoai", 500_000m, Guid.NewGuid())
            }, TenantId);
            await context.SaveChangesAsync();
            Assert.Equal(3, emp.Allowances.Count);

            // Step 3: Delete by omitting from new list
            var comTrua = emp.Allowances.First(a => a.Name == "Com trua");
            repo.SyncAllowances(emp, new List<EmployeeAllowanceDto>
            {
                new("Com trua giu lai", 350_000m, comTrua.Id)
            }, TenantId);
            await context.SaveChangesAsync();
            Assert.Single(emp.Allowances);

            repo.RemoveAllowances(emp.Allowances);
            await context.SaveChangesAsync();
            Assert.Empty(emp.Allowances);

            // 11. HasWorkOrPayrollHistoryAsync
            Assert.False(await repo.HasWorkOrPayrollHistoryAsync(emp.Id));

            // 12. GetActiveEmployeesAsync
            var activeEmps = await repo.GetActiveEmployeesAsync(branch.Id);
            Assert.Single(activeEmps);

            // 13. Delete
            repo.Delete(emp);
            await context.SaveChangesAsync();
            Assert.Null(await repo.GetByIdAsync(emp.Id));
        }
    }

    [Fact]
    public async Task ShiftRepository_AllMethods_ShouldWork()
    {
        var (context, _) = CreateDbContext();
        using (context)
        {
            var repo = new ShiftRepository(context);

            var branch = new Branch
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                BranchCode = "BR-SHIFT",
                Name = "Chi nhánh Shift",
                CreatedAt = DateTime.UtcNow
            };
            var shift = new ShiftZReport
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                BranchId = branch.Id,
                ShiftCode = "CA-01",
                Status = ShiftState.Open,
                OpenedAtUtc = DateTime.UtcNow,
                OpeningFloat = 500_000m,
                CreatedAt = DateTime.UtcNow
            };
            await context.Branches.AddAsync(branch);
            await repo.AddAsync(shift);
            await context.SaveChangesAsync();

            // 1. GetByIdAsync
            var found = await repo.GetByIdAsync(shift.Id);
            Assert.NotNull(found);

            // 2. GetActiveShiftAsync
            var active = await repo.GetActiveShiftAsync(branch.Id);
            Assert.NotNull(active);
            Assert.Equal(ShiftState.Open, active!.Status);

            // 3. GetBusinessDayStartTimeAsync
            var startTime = await repo.GetBusinessDayStartTimeAsync(branch.Id);
            Assert.True(startTime.TotalHours >= 0);

            // 4. Next Shift Code
            var nextCode = await repo.GenerateNextShiftCodeAsync(branch.Id);
            Assert.NotEmpty(nextCode);

            // 5. Cash Movement
            var movement = new ShiftCashMovement
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                ShiftId = shift.Id,
                MovementType = CashMovementType.IN,
                Category = CashMovementCategory.CASH_DEPOSIT,
                Amount = 100000m,
                Status = CashMovementStatus.PendingApproval,
                Reason = "Tien le",
                CreatedAt = DateTime.UtcNow
            };
            await repo.AddCashMovementAsync(movement);
            await context.SaveChangesAsync();
            var foundMovement = await repo.GetCashMovementByIdAsync(movement.Id);
            Assert.NotNull(foundMovement);
            var pendingMovements = await repo.GetPendingMovementsAsync(branch.Id);
            Assert.NotEmpty(pendingMovements);

            // Shift participation is persisted in the shift snapshot.
            var testUserId = Guid.NewGuid();
            var service = new ShiftService(repo, new UnitOfWork(context), new TestTenantContext());
            await service.RecordParticipantAsync(shift.Id, testUserId, "cashier", "Shift Emp");
            context.ChangeTracker.Clear();

            var savedShift = await repo.GetByIdAsync(shift.Id);
            Assert.NotNull(savedShift);
            var participant = Assert.Single(JsonSerializer.Deserialize<ShiftParticipantDto[]>(savedShift.ParticipantsJson)!);
            Assert.Equal(testUserId, participant.UserId);
            Assert.Equal("cashier", participant.UserName);

            // 7. Branch Resolution & Employee lookup
            var resolvedBranch = await repo.ResolveBranchIdAsync("BR-SHIFT");
            Assert.Equal(branch.Id, resolvedBranch);

            var userStrId = testUserId.ToString();
            var emp = new Employee
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                BranchId = branch.Id,
                EmployeeCode = "NV-SH",
                FullName = "Shift Emp",
                Phone = "0977777777",
                UserId = userStrId
            };
            await context.Employees.AddAsync(emp);
            await context.SaveChangesAsync();

            var foundEmp = await repo.GetEmployeeByUserIdAsync(userStrId);
            Assert.NotNull(foundEmp);

            var paymentSettings = new Tenant { Id = TenantId, Name = "Cửa hàng thử nghiệm" };
            await context.Tenants.AddAsync(paymentSettings);
            await context.SaveChangesAsync();
            paymentSettings.CashOutApprovalThreshold = 350_000m;
            await repo.SavePaymentConfigAsync(paymentSettings);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            var fetchedSettings = await repo.GetPaymentConfigAsync(TenantId);
            Assert.NotNull(fetchedSettings);
            Assert.Equal(350_000m, fetchedSettings.CashOutApprovalThreshold);

            // 9. Closed shift & sales metrics
            var closedShift = new ShiftZReport
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                BranchId = branch.Id,
                ShiftCode = "CA-02",
                Status = ShiftState.Closed,
                OpenedAtUtc = DateTime.UtcNow.AddHours(-4),
                ClosedAtUtc = DateTime.UtcNow.AddMinutes(-30),
                OpeningFloat = 500_000m,
                CreatedAt = DateTime.UtcNow.AddHours(-4)
            };
            await repo.AddShiftAsync(closedShift);

            // Add order in shift to compute sales metrics
            context.Orders.Add(new Order
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                BranchId = branch.Id,
                ShiftId = shift.Id,
                OrderCode = "HD-SHIFT-01",
                PaymentMethod = "CASH",
                PaymentStatus = "PAID",
                Status = "Completed",
                FinalAmount = 200_000m,
                CreatedAt = DateTime.UtcNow
            });
            context.Orders.Add(new Order
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                BranchId = branch.Id,
                ShiftId = shift.Id,
                OrderCode = "HD-SHIFT-02",
                PaymentMethod = "VIETQR",
                PaymentStatus = "PAID",
                Status = "Completed",
                FinalAmount = 300_000m,
                CreatedAt = DateTime.UtcNow
            });

            // Add schedule for cashier
            context.EmployeeSchedules.Add(new EmployeeSchedule
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                BranchId = branch.Id,
                EmployeeId = emp.Id,
                WorkDate = DateTime.UtcNow.Date,
                PlannedStartTime = new TimeSpan(8, 0, 0),
                PlannedEndTime = new TimeSpan(16, 0, 0),
                Status = ScheduleStatus.Published,
                CreatedAt = DateTime.UtcNow
            });
            await context.SaveChangesAsync();

            var latestClosed = await repo.GetLatestClosedShiftAsync(branch.Id);
            Assert.NotNull(latestClosed);
            Assert.Equal(closedShift.Id, latestClosed!.Id);

            var history = await repo.GetHistoryAsync(branch.Id, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1));
            Assert.NotEmpty(history);

            var metrics = await repo.GetShiftSalesMetricsAsync(shift.Id);
            Assert.Equal(200_000m, metrics.CashSales);
            Assert.Equal(300_000m, metrics.BankRevenue);
            Assert.Equal(2, metrics.OrdersCount);

            var empSchedules = await repo.GetCashierSchedulesAsync(emp.Id, branch.Id, DateTime.UtcNow.Date);
            Assert.NotEmpty(empSchedules);

            var branchSchedules = await repo.GetBranchCashierSchedulesAsync(branch.Id, DateTime.UtcNow.Date);
            Assert.NotEmpty(branchSchedules);
        }
    }

    [Fact]
    public async Task ShiftConfigRepository_AndAttendancePolicy_ShouldWork()
    {
        var (context, _) = CreateDbContext();
        using (context)
        {
            var shiftRepo = new ShiftConfigRepository(context);
            var branchRepo = new BranchRepository(context);

            var branch = new Branch
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                BranchCode = "BR-SC",
                Name = "Chi nhánh SC",
                LateThresholdMinutes = 15,
                IsAttendancePolicyCustomized = true,
                CreatedAt = DateTime.UtcNow
            };
            var config = new ShiftConfig
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                BranchId = branch.Id,
                ShiftCode = "S1",
                ShiftName = "Ca sáng",
                StartTime = new TimeSpan(8, 0, 0),
                EndTime = new TimeSpan(12, 0, 0),
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            await branchRepo.AddAsync(branch);
            await context.ShiftConfigs.AddAsync(config);
            await context.SaveChangesAsync();

            var savedBranch = await branchRepo.GetByIdAsync(branch.Id);
            Assert.NotNull(savedBranch);
            Assert.Equal(15, savedBranch.LateThresholdMinutes);

            // ShiftConfig
            var sc = await shiftRepo.GetByIdAsync(config.Id);
            Assert.NotNull(sc);
            var scList = await shiftRepo.GetByBranchIdAsync(branch.Id);
            Assert.Single(scList);
            var (items, count) = await shiftRepo.GetPagedListAsync(new ShiftConfigQueryFilter { Keyword = "sáng" });
            Assert.Equal(1, count);

            var byCode = await shiftRepo.GetByCodeAsync(branch.Id, "S1");
            Assert.NotNull(byCode);
            var exists = await shiftRepo.ExistsAsync(s => s.ShiftCode == "S1");
            Assert.True(exists);

            var config2 = new ShiftConfig
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                BranchId = branch.Id,
                ShiftCode = "S2",
                ShiftName = "Ca chiều",
                StartTime = new TimeSpan(13, 0, 0),
                EndTime = new TimeSpan(17, 0, 0),
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            await shiftRepo.AddAsync(config2);
            await context.SaveChangesAsync();

            sc.ShiftName = "Ca sáng cập nhật";
            shiftRepo.Update(sc);
            shiftRepo.Delete(config2);
            await context.SaveChangesAsync();

            // AttendancePolicy on Branch
            var pol = await branchRepo.GetByIdAsync(branch.Id);
            Assert.NotNull(pol);
            Assert.Equal(15, pol!.LateThresholdMinutes);

            pol.LateThresholdMinutes = 20;
            branchRepo.Update(pol);
            await context.SaveChangesAsync();

            var updatedPol = await branchRepo.GetByIdAsync(branch.Id);
            Assert.Equal(20, updatedPol!.LateThresholdMinutes);
        }
    }

    [Fact]
    public async Task ScheduleRepository_AndPayslipRepository_ShouldWork()
    {
        var (context, tenant) = CreateDbContext();
        using (context)
        {
            var schedRepo = new ScheduleRepository(context);
            var payRepo = new PayslipRepository(context, tenant);

            var branch = new Branch
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                BranchCode = "BR-PAY",
                Name = "Chi nhánh Pay",
                CreatedAt = DateTime.UtcNow
            };
            var emp = new Employee
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                EmployeeCode = "NV-PAY",
                FullName = "Nhan vien Pay",
                CreatedAt = DateTime.UtcNow
            };
            var sched = new EmployeeSchedule
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                BranchId = branch.Id,
                EmployeeId = emp.Id,
                WorkDate = DateTime.UtcNow.Date,
                PlannedStartTime = new TimeSpan(8, 0, 0),
                PlannedEndTime = new TimeSpan(17, 0, 0),
                Status = ScheduleStatus.Published,
                CreatedAt = DateTime.UtcNow
            };
            var payslip = new Payslip
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                EmployeeId = emp.Id,
                BranchId = branch.Id,
                PeriodMonth = "2026-11",
                TotalEarnings = 10_000_000m,
                NetSalary = 9_500_000m,
                Status = PayslipStatus.Unpaid,
                CreatedAt = DateTime.UtcNow
            };

            await context.Branches.AddAsync(branch);
            await context.Employees.AddAsync(emp);
            await context.EmployeeSchedules.AddAsync(sched);
            await context.Payslips.AddAsync(payslip);
            await context.SaveChangesAsync();

            // Schedule
            var foundSched = await schedRepo.GetByIdAsync(sched.Id);
            Assert.NotNull(foundSched);
            var schedList = await schedRepo.GetListAsync(new ScheduleQueryFilter { BranchId = branch.Id });
            Assert.Single(schedList);

            // Payslip
            var foundPay = await payRepo.GetByIdAsync(payslip.Id);
            Assert.NotNull(foundPay);
            var payByMonth = await payRepo.GetByEmployeeAndPeriodAsync(emp.Id, "2026-11");
            Assert.NotNull(payByMonth);
            var (payItems, payCount) = await payRepo.GetPagedListAsync(new PayslipQueryFilter { PeriodMonth = "2026-11" });
            Assert.Equal(1, payCount);

            // Schedule - additional methods
            var activeScheds = await schedRepo.GetActiveSchedulesForEmployeesAsync(new[] { emp.Id }, DateTime.UtcNow.Date, DateTime.UtcNow.Date.AddDays(1));
            Assert.NotNull(activeScheds);

            var completedScheds = await schedRepo.GetCompletedSchedulesInPeriodAsync(new[] { emp.Id }, DateTime.UtcNow.Date.AddDays(-7), DateTime.UtcNow.Date.AddDays(7));
            Assert.NotNull(completedScheds);

            var empScheds = await schedRepo.GetByEmployeeAndDateRangeAsync(emp.Id, DateTime.UtcNow.Date.AddDays(-1), DateTime.UtcNow.Date.AddDays(1));
            Assert.NotNull(empScheds);

            var sched2 = new EmployeeSchedule
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                BranchId = branch.Id,
                EmployeeId = emp.Id,
                WorkDate = DateTime.UtcNow.Date.AddDays(1),
                PlannedStartTime = new TimeSpan(9, 0, 0),
                PlannedEndTime = new TimeSpan(18, 0, 0),
                Status = ScheduleStatus.Draft,
                CreatedAt = DateTime.UtcNow
            };
            await schedRepo.AddAsync(sched2);
            await schedRepo.AddRangeAsync(new[] {
                new EmployeeSchedule
                {
                    Id = Guid.NewGuid(),
                    TenantId = TenantId,
                    BranchId = branch.Id,
                    EmployeeId = emp.Id,
                    WorkDate = DateTime.UtcNow.Date.AddDays(2),
                    PlannedStartTime = new TimeSpan(8, 0, 0),
                    PlannedEndTime = new TimeSpan(17, 0, 0),
                    Status = ScheduleStatus.Draft,
                    CreatedAt = DateTime.UtcNow
                }
            });
            await context.SaveChangesAsync();

            sched2.Status = ScheduleStatus.Published;
            schedRepo.Update(sched2);
            schedRepo.Delete(sched2);
            await context.SaveChangesAsync();

            // Payslip - additional methods
            var branchPayslips = await payRepo.GetByPeriodAndBranchAsync("2026-11", branch.Id);
            Assert.NotEmpty(branchPayslips);

            var countMonth = await payRepo.GetCountByPeriodMonthAsync("2026-11");
            Assert.True(countMonth >= 1);

            var payslip2 = new Payslip
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                EmployeeId = emp.Id,
                BranchId = branch.Id,
                PeriodMonth = "2026-12",
                TotalEarnings = 12_000_000m,
                NetSalary = 11_000_000m,
                Status = PayslipStatus.Paid,
                CreatedAt = DateTime.UtcNow
            };
            await payRepo.AddAsync(payslip2);
            await payRepo.AddRangeAsync(new[] {
                new Payslip
                {
                    Id = Guid.NewGuid(),
                    TenantId = TenantId,
                    EmployeeId = emp.Id,
                    BranchId = branch.Id,
                    PeriodMonth = "2027-01",
                    TotalEarnings = 12_000_000m,
                    NetSalary = 11_000_000m,
                    Status = PayslipStatus.Paid,
                    CreatedAt = DateTime.UtcNow
                }
            });
            await context.SaveChangesAsync();

            payslip2.TotalEarnings = 13_000_000m;
            payRepo.Update(payslip2);
            payRepo.Delete(payslip2);
            await context.SaveChangesAsync();

        }
    }

    [Fact]
    public async Task OrderRepository_AllMethods_ShouldWork()
    {
        var (context, _) = CreateDbContext();
        using (context)
        {
            var repo = new OrderRepository(context);

            var branch = new Branch
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                BranchCode = "BR-ORD",
                Name = "Chi nhánh Đơn",
                CreatedAt = DateTime.UtcNow
            };
            var cust = new Customer
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                CustomerCode = "KH-ORD",
                Name = "Khach hang ORD",
                CreatedAt = DateTime.UtcNow
            };
            var prod = new Product
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                ProductCode = "SP-ORD",
                Name = "Hang hoa ORD",
                BaseUnit = "Goi",
                CreatedAt = DateTime.UtcNow
            };
            var stock = new BranchStock
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                BranchId = branch.Id,
                ProductId = prod.Id,
                QuantityOnHand = 20,
                CreatedAt = DateTime.UtcNow
            };

            await context.Branches.AddAsync(branch);
            await context.Customers.AddAsync(cust);
            await context.Products.AddAsync(prod);
            await context.BranchStocks.AddAsync(stock);
            await context.SaveChangesAsync();

            // 1. GetBranchByIdAsync
            Assert.NotNull(await repo.GetBranchByIdAsync(branch.Id));

            // 2. GetCustomerByIdAsync
            Assert.NotNull(await repo.GetCustomerByIdAsync(cust.Id));

            // 3. GetProductsByIdsAsync
            var prodDict = await repo.GetProductsByIdsAsync(new[] { prod.Id });
            Assert.Single(prodDict);

            // 4. GetBranchStocksForProductFefoAsync
            var stocks = await repo.GetBranchStocksForProductFefoAsync(branch.Id, prod.Id);
            Assert.Single(stocks);

            // 5. GetOrderWithDetailsAsync, GetOrdersWithFilterAsync, CountOrdersWithFilterAsync, GetAllOrdersForSummaryAsync
            var order = new Order
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                OrderCode = "HD-ORD-01",
                BranchId = branch.Id,
                CustomerId = cust.Id,
                TotalAmount = 50000m,
                FinalAmount = 50000m,
                Status = "COMPLETED",
                OrderDate = DateTime.UtcNow,
                Items = new List<OrderDetail>
                {
                    new OrderDetail
                    {
                        Id = Guid.NewGuid(),
                        ProductId = prod.Id,
                        UnitPrice = 25000m,
                        Qty = 2m,
                        ActualPrice = 25000m
                    }
                }
            };
            var bp = new BranchProduct
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                BranchId = branch.Id,
                ProductId = prod.Id,
                Status = "Active"
            };
            await context.Orders.AddAsync(order);
            await context.BranchProducts.AddAsync(bp);
            await context.SaveChangesAsync();

            var orderDetails = await repo.GetOrderWithDetailsAsync(order.Id);
            Assert.NotNull(orderDetails);
            Assert.NotEmpty(orderDetails!.Items);

            var filteredOrders = await repo.GetOrdersWithFilterAsync(branch.Id, null, null, null, null, null, 1, 10);
            Assert.NotEmpty(filteredOrders);

            var count = await repo.CountOrdersWithFilterAsync(branch.Id, null, null, null, null, null);
            Assert.True(count >= 1);

            var summaryOrders = await repo.GetAllOrdersForSummaryAsync(branch.Id, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1));
            Assert.NotEmpty(summaryOrders);

            var bpMap = await repo.GetBranchProductsByProductIdsAsync(branch.Id, new[] { prod.Id });
            Assert.True(bpMap.ContainsKey(prod.Id));

            var foundStock = await repo.GetBranchStockAsync(branch.Id, prod.Id, null);
            Assert.NotNull(foundStock);
        }
    }

    [Fact]
    public async Task ScheduleApprovalRequestRepository_AllMethods_ShouldWork()
    {
        var (context, _) = CreateDbContext();
        using (context)
        {
            var branch = new Branch
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                BranchCode = "BR-REQ-01",
                Name = "Chi nhanh Yeu Cau",
                CreatedAt = DateTime.UtcNow
            };
            var emp = new MT_GRMS.Domain.Entities.HR.Employee
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                BranchId = branch.Id,
                FullName = "Nguyen Van Yeu Cau",
                EmployeeCode = "EMP-REQ-01",
                Phone = "0987654329",
                Status = EmployeeStatus.Active,
                CreatedAt = DateTime.UtcNow
            };
            var schedule = new EmployeeSchedule
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                BranchId = branch.Id,
                EmployeeId = emp.Id,
                WorkDate = DateTime.UtcNow.Date,
                PlannedStartTime = TimeSpan.FromHours(8),
                PlannedEndTime = TimeSpan.FromHours(16),
                CreatedAt = DateTime.UtcNow
            };
            var request = new ScheduleApprovalRequest
            {
                Id = Guid.NewGuid(),
                TenantId = TenantId,
                BranchId = branch.Id,
                EmployeeId = emp.Id,
                ScheduleId = schedule.Id,
                RequestType = ScheduleRequestType.CheckInIncident,
                Status = ScheduleApprovalStatus.Pending,
                Reason = "Xe bi hong giua duong",
                CreatedAt = DateTime.UtcNow
            };

            await context.Branches.AddAsync(branch);
            await context.Employees.AddAsync(emp);
            await context.EmployeeSchedules.AddAsync(schedule);
            await context.ScheduleApprovalRequests.AddAsync(request);
            await context.SaveChangesAsync();

            var repo = new ScheduleApprovalRequestRepository(context);

            // 1. GetByIdWithDetailsAsync
            var detailed = await repo.GetByIdWithDetailsAsync(request.Id);
            Assert.NotNull(detailed);
            Assert.Equal("Xe bi hong giua duong", detailed!.Reason);

            // 2. GetByScheduleIdAsync
            var bySchedule = await repo.GetByScheduleIdAsync(schedule.Id);
            Assert.Single(bySchedule);

            // 3. GetPendingAsync
            var pending = await repo.GetPendingAsync(branch.Id, ScheduleRequestType.CheckInIncident);
            Assert.Single(pending);

            // 4. GetRequestsAsync
            var filtered = await repo.GetRequestsAsync(branch.Id, ScheduleRequestType.CheckInIncident, ScheduleApprovalStatus.Pending);
            Assert.Single(filtered);

            // 5. HasPendingRequestOfTypeAsync
            var hasPending = await repo.HasPendingRequestOfTypeAsync(schedule.Id, ScheduleRequestType.CheckInIncident);
            Assert.True(hasPending);

            var noPending = await repo.HasPendingRequestOfTypeAsync(schedule.Id, ScheduleRequestType.ShiftSwap);
            Assert.False(noPending);
        }
    }
}
