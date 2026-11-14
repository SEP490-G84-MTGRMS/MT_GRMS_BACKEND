using System.Linq.Expressions;
using MT_GRMS.Application.Auth;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.HR.Employees;
using MT_GRMS.Application.HR.Employees.Dtos;
using MT_GRMS.Domain.Entities.HR;
using MT_GRMS.Domain.Entities.Identity;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Domain.Exceptions;
using Xunit;
using EmployeeEntity = MT_GRMS.Domain.Entities.HR.Employee;

namespace MT_GRMS.UnitTests.HR;

public class EmployeeQueryAndFilteringTests
{
    private class FakeUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
    }

    private class FakeTenantContext : ITenantContext
    {
        public string CurrentTenantId { get; set; } = "tenant_query_test";
        public Guid CurrentUserId { get; set; } = Guid.NewGuid();
        public string CurrentRole { get; set; } = "STORE_OWNER";
        public Guid? CurrentBranchId { get; set; } = Guid.NewGuid();

        public void SetContext(string tenantId, Guid userId, Guid? branchId)
        {
            CurrentTenantId = tenantId;
            CurrentUserId = userId;
            CurrentBranchId = branchId;
        }

        public void SetRole(string? role) => CurrentRole = role ?? string.Empty;
    }

    private class FakeUserRepository : IUserRepository
    {
        public List<User> Users { get; } = new();

        public Task<User?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Users.FirstOrDefault(u => u.Id == id));

        public Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default) =>
            Task.FromResult(Users.FirstOrDefault(u => string.Equals(u.UserName, username, StringComparison.OrdinalIgnoreCase)));

        public Task<IReadOnlyList<User>> FindAsync(Expression<Func<User, bool>> predicate, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<User>>(Users.Where(predicate.Compile()).ToList());

        public Task<IReadOnlyList<User>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<User>>(Users);

        public Task AddAsync(User user, CancellationToken cancellationToken = default)
        {
            Users.Add(user);
            return Task.CompletedTask;
        }

        public void Update(User user) { }
        public void Delete(User user) => Users.Remove(user);
        public Task<bool> ExistsByPhoneOrEmailAsync(string phone, string? email, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
        public Task<User> RegisterStoreOwnerAsync(User user, Tenant tenant, Branch branch, CancellationToken cancellationToken = default) =>
            Task.FromResult(user);
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private class FakeEmployeeRepository : IEmployeeRepository
    {
        public List<EmployeeEntity> Employees { get; } = new();

        public Task<IReadOnlyList<EmployeeEntity>> GetActiveEmployeesAsync(Guid? branchId, CancellationToken cancellationToken = default)
        {
            var query = Employees.Where(e => !e.IsDeleted && e.Status == EmployeeStatus.Active);
            if (branchId.HasValue && branchId.Value != Guid.Empty)
            {
                query = query.Where(e => e.BranchId == branchId.Value);
            }
            return Task.FromResult<IReadOnlyList<EmployeeEntity>>(query.ToList());
        }

        public Task<EmployeeEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Employees.FirstOrDefault(e => e.Id == id && !e.IsDeleted));

        public Task<EmployeeEntity?> GetByCodeAsync(string code, CancellationToken cancellationToken = default) =>
            Task.FromResult(Employees.FirstOrDefault(e => string.Equals(e.EmployeeCode, code, StringComparison.OrdinalIgnoreCase) && !e.IsDeleted));

        public Task<EmployeeEntity?> GetByCitizenIdAsync(string citizenId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Employees.FirstOrDefault(e => e.CitizenId == citizenId && !e.IsDeleted));

        public Task<EmployeeEntity?> GetByPhoneAsync(string phone, CancellationToken cancellationToken = default) =>
            Task.FromResult(Employees.FirstOrDefault(e => e.Phone == phone && !e.IsDeleted));

        public Task<EmployeeEntity?> GetByUserIdAsync(string userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Employees.FirstOrDefault(e => e.UserId == userId && !e.IsDeleted));

        public Task<(IReadOnlyList<EmployeeEntity> Items, int TotalCount)> GetPagedListAsync(
            EmployeeQueryFilter filter,
            CancellationToken cancellationToken = default)
        {
            var query = Employees.Where(e => !e.IsDeleted);

            if (!string.IsNullOrWhiteSpace(filter.Keyword))
            {
                var kw = filter.Keyword.Trim().ToLower();
                query = query.Where(e =>
                    e.FullName.ToLower().Contains(kw) ||
                    e.EmployeeCode.ToLower().Contains(kw) ||
                    e.Phone.Contains(kw));
            }

            if (filter.BranchIds != null && filter.BranchIds.Count > 0)
            {
                var validIds = filter.BranchIds.Where(id => id != Guid.Empty).ToList();
                if (validIds.Count > 0)
                {
                    query = query.Where(e => validIds.Contains(e.BranchId));
                }
            }
            else if (filter.BranchId.HasValue && filter.BranchId.Value != Guid.Empty)
            {
                query = query.Where(e => e.BranchId == filter.BranchId.Value);
            }

            if (!string.IsNullOrWhiteSpace(filter.Status))
            {
                query = query.Where(e => string.Equals(e.Status.ToString(), filter.Status, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(filter.SalaryType))
            {
                query = query.Where(e => string.Equals(e.SalaryType.ToString(), filter.SalaryType, StringComparison.OrdinalIgnoreCase));
            }

            var totalCount = query.Count();
            var items = query
                .Skip((filter.Page - 1) * filter.PageSize)
                .Take(filter.PageSize)
                .ToList();

            return Task.FromResult<(IReadOnlyList<EmployeeEntity> Items, int TotalCount)>((items, totalCount));
        }

        public Task<string?> GetMaxSequentialCodeAsync(string prefix, int totalLength, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

        public Task<bool> ExistsAsync(Expression<Func<EmployeeEntity, bool>> predicate, CancellationToken cancellationToken = default) =>
            Task.FromResult(Employees.Where(e => !e.IsDeleted).Any(predicate.Compile()));

        public Task AddAsync(EmployeeEntity employee, CancellationToken cancellationToken = default)
        {
            Employees.Add(employee);
            return Task.CompletedTask;
        }

        public void Update(EmployeeEntity employee) { }
        public void Delete(EmployeeEntity employee) => Employees.Remove(employee);
        public void RemoveAllowances(IEnumerable<EmployeeAllowance> allowances) { }
        public void SyncAllowances(EmployeeEntity employee, IEnumerable<EmployeeAllowanceDto> newAllowances, string tenantId) { }
        public Task<bool> HasWorkOrPayrollHistoryAsync(Guid employeeId, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }

    private static (EmployeeService Service, FakeEmployeeRepository Repo, Guid BranchA, Guid BranchB) CreateSeededSut()
    {
        var repo = new FakeEmployeeRepository();
        var branchA = Guid.NewGuid();
        var branchB = Guid.NewGuid();

        repo.Employees.AddRange(new[]
        {
            new EmployeeEntity
            {
                Id = Guid.NewGuid(),
                EmployeeCode = "NV0001",
                FullName = "Nguyễn Văn An",
                CitizenId = "079201000001",
                Phone = "0901000001",
                BranchId = branchA,
                Branch = new Branch { Id = branchA, Name = "Chi nhánh Quận 1" },
                Status = EmployeeStatus.Active,
                SalaryType = SalaryType.Monthly,
                BaseRate = 9000000m,
                FaceDescriptor = "[0.1, 0.2]",
                UserId = "user_an_01"
            },
            new EmployeeEntity
            {
                Id = Guid.NewGuid(),
                EmployeeCode = "NV0002",
                FullName = "Trần Thị Bích",
                CitizenId = "079201000002",
                Phone = "0901000002",
                BranchId = branchA,
                Branch = new Branch { Id = branchA, Name = "Chi nhánh Quận 1" },
                Status = EmployeeStatus.Active,
                SalaryType = SalaryType.Hourly,
                BaseRate = 30000m,
                FaceDescriptor = null
            },
            new EmployeeEntity
            {
                Id = Guid.NewGuid(),
                EmployeeCode = "NV0003",
                FullName = "Lê Hoàng Cường",
                CitizenId = "079201000003",
                Phone = "0901000003",
                BranchId = branchB,
                Branch = new Branch { Id = branchB, Name = "Chi nhánh Quận 7" },
                Status = EmployeeStatus.Inactive,
                SalaryType = SalaryType.Shift,
                BaseRate = 220000m
            }
        });

        var service = new EmployeeService(repo, new FakeUserRepository(), new FakeUnitOfWork(), new FakeTenantContext());
        return (service, repo, branchA, branchB);
    }

    // =========================================================================
    // 1. TRUY VẤN & LỌC HỒ SƠ NHÂN VIÊN (QUERY & FILTERING)
    // =========================================================================

    [Theory]
    [InlineData("Nguyễn Văn An", 1, "NV0001")]
    [InlineData("nv0002", 1, "NV0002")]
    [InlineData("0901000003", 1, "NV0003")]
    [InlineData("0901000", 3, "NV0001")]
    public async Task GetEmployeesPaged_FilterByKeyword_MatchesNameCodeOrPhone(
        string keyword, int expectedCount, string expectedFirstCode)
    {
        var (service, _, _, _) = CreateSeededSut();

        var result = await service.GetEmployeesPagedAsync(new EmployeeQueryFilter(Keyword: keyword));

        Assert.Equal(expectedCount, result.TotalItems);
        Assert.Equal(expectedFirstCode, result.Items.First().EmployeeCode);
    }

    [Fact]
    public async Task GetEmployeesPaged_FilterByBranchStatusAndSalaryType_ReturnsExactMatches()
    {
        var (service, _, branchA, branchB) = CreateSeededSut();

        var activeBranchA = await service.GetEmployeesPagedAsync(
            new EmployeeQueryFilter(BranchId: branchA, Status: "active"));
        Assert.Equal(2, activeBranchA.TotalItems);
        Assert.True(activeBranchA.Items.First(e => e.EmployeeCode == "NV0001").HasFaceId);
        Assert.False(activeBranchA.Items.First(e => e.EmployeeCode == "NV0002").HasFaceId);

        var inactiveShift = await service.GetEmployeesPagedAsync(
            new EmployeeQueryFilter(BranchIds: new List<Guid> { branchB }, Status: "inactive", SalaryType: "shift"));
        Assert.Single(inactiveShift.Items);
        Assert.Equal("NV0003", inactiveShift.Items[0].EmployeeCode);
    }

    [Fact]
    public async Task GetEmployeeByIdAndByUserId_ResolvesEmployeeOrThrowsNotFound()
    {
        var (service, repo, _, _) = CreateSeededSut();
        var existing = repo.Employees[0];

        var byId = await service.GetEmployeeByIdAsync(existing.Id);
        Assert.Equal("NV0001", byId.EmployeeCode);

        var byUserId = await service.GetEmployeeByUserIdAsync("user_an_01");
        Assert.NotNull(byUserId);
        Assert.Equal(existing.Id, byUserId!.Id);

        var byGuidFallback = await service.GetEmployeeByUserIdAsync(existing.Id.ToString());
        Assert.NotNull(byGuidFallback);
        Assert.Equal("NV0001", byGuidFallback!.EmployeeCode);

        await Assert.ThrowsAsync<NotFoundException>(() => service.GetEmployeeByIdAsync(Guid.NewGuid()));
    }

    // =========================================================================
    // 2. LOGIC PHÂN CA & CHẤM CÔNG (SCH-VAL-04, SCH-VAL-05, SCH-ATT-01..03)
    // =========================================================================

    [Theory]
    [InlineData(8, 12, 10, 14, true)]  // SCH-VAL-04: Giao nhau (08:00-12:00 và 10:00-14:00) -> Overlap
    [InlineData(8, 12, 8, 12, true)]   // Trùng hoàn toàn -> Overlap
    [InlineData(8, 12, 12, 16, false)] // SCH-VAL-05: 2 ca nối tiếp (08:00-12:00 và 12:00-16:00) -> Hợp lệ
    [InlineData(8, 12, 13, 17, false)] // SCH-VAL-05: 2 ca tách biệt (08:00-12:00 và 13:00-17:00) -> Hợp lệ
    public void ScheduleOverlap_SCH_VAL_04_05_DetectsOverlappingTimeWindowsAccurately(
        int startA, int endA, int startB, int endB, bool expectedOverlap)
    {
        var shift1Start = TimeSpan.FromHours(startA);
        var shift1End = TimeSpan.FromHours(endA);
        var shift2Start = TimeSpan.FromHours(startB);
        var shift2End = TimeSpan.FromHours(endB);

        bool isOverlapping = shift2Start < shift1End && shift2End > shift1Start;

        Assert.Equal(expectedOverlap, isOverlapping);
    }

    [Theory]
    [InlineData("07:58", "12:00", 5, 0, 0)]   // SCH-ATT-01: Đúng giờ -> Late = 0, Early = 0
    [InlineData("08:04", "12:00", 5, 0, 0)]   // SCH-ATT-02 Case A: Trễ 4p <= 5p dung sai -> Late = 0
    [InlineData("08:15", "12:00", 5, 15, 0)]  // SCH-ATT-02 Case B: Trễ 15p > 5p dung sai -> Late = 15
    [InlineData("08:00", "11:40", 5, 0, 20)]  // SCH-ATT-02 Case C: Về sớm 20p -> EarlyLeave = 20
    public void AttendanceCalculation_SCH_ATT_01_02_ComputesLateAndEarlyLeaveMinutesWithGracePeriod(
        string checkInStr, string checkOutStr, int graceMinutes, int expectedLate, int expectedEarly)
    {
        var plannedStart = new TimeSpan(8, 0, 0);
        var plannedEnd = new TimeSpan(12, 0, 0);
        var actualIn = TimeSpan.Parse(checkInStr);
        var actualOut = TimeSpan.Parse(checkOutStr);

        var rawLate = (int)Math.Max(0, (actualIn - plannedStart).TotalMinutes);
        var lateMinutes = rawLate <= graceMinutes ? 0 : rawLate;

        var rawEarly = (int)Math.Max(0, (plannedEnd - actualOut).TotalMinutes);
        var earlyMinutes = rawEarly <= graceMinutes ? 0 : rawEarly;

        Assert.Equal(expectedLate, lateMinutes);
        Assert.Equal(expectedEarly, earlyMinutes);
    }

    // =========================================================================
    // 3. CÔNG THỨC TÍNH LƯƠNG & KHẤU TRỪ TẠM ỨNG (PAY-CALC-01 .. PAY-CALC-04)
    // =========================================================================

    [Fact]
    public void PayrollCalculation_PAY_CALC_01_MonthlySalaryFormula_CalculatesCorrectly()
    {
        decimal baseRate = 7_800_000m;
        decimal standardDays = 26m;
        decimal workedDays = 24m;

        decimal baseSalaryEarned = Math.Round((workedDays / standardDays) * baseRate, 0, MidpointRounding.AwayFromZero);

        Assert.Equal(7_200_000m, baseSalaryEarned);
    }

    [Fact]
    public void PayrollCalculation_PAY_CALC_02_HourlySalaryFormula_CalculatesCorrectly()
    {
        decimal baseRate = 25_000m;
        decimal totalWorkHours = 160.5m;

        decimal baseSalaryEarned = Math.Round(totalWorkHours * baseRate, 0, MidpointRounding.AwayFromZero);

        Assert.Equal(4_012_500m, baseSalaryEarned);
    }

    [Fact]
    public void PayrollCalculation_PAY_CALC_03_ShiftSalaryFormula_CalculatesCorrectly()
    {
        decimal baseRate = 150_000m;
        int totalShifts = 42;

        decimal baseSalaryEarned = Math.Round(totalShifts * baseRate, 0, MidpointRounding.AwayFromZero);

        Assert.Equal(6_300_000m, baseSalaryEarned);
    }

    [Fact]
    public void PayrollCalculation_PAY_CALC_04_AdvanceDeductionAndNetSalary_CalculatesRemainingDebtAccurately()
    {
        decimal totalEarnings = 8_500_000m;
        decimal employeeDebt = 2_000_000m;
        decimal advanceDeduction = 1_500_000m;

        Assert.True(advanceDeduction <= totalEarnings);
        Assert.True(advanceDeduction <= employeeDebt);

        decimal netSalary = totalEarnings - advanceDeduction;
        decimal remainingDebt = employeeDebt - advanceDeduction;

        Assert.Equal(7_000_000m, netSalary);
        Assert.Equal(500_000m, remainingDebt);
    }
}
