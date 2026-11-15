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

public class EmployeeLifecycleAndBusinessTests
{
    private class FakeUnitOfWork : IUnitOfWork
    {
        public int SaveChangesCount { get; private set; }
        public bool SaveChangesCalled => SaveChangesCount > 0;

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesCount++;
            return Task.FromResult(1);
        }
    }

    private class FakeTenantContext : ITenantContext
    {
        public string CurrentTenantId { get; set; } = "tenant_hr_test";
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
            Task.FromResult(Users.Any(u => u.Phone == phone || (!string.IsNullOrEmpty(email) && u.Email == email)));

        public Task<User> RegisterStoreOwnerAsync(User user, Tenant tenant, Branch branch, CancellationToken cancellationToken = default)
        {
            Users.Add(user);
            return Task.FromResult(user);
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private class FakeEmployeeRepository : IEmployeeRepository
    {
        public List<EmployeeEntity> Employees { get; } = new();
        public List<EmployeeAllowance> RemovedAllowances { get; } = new();
        public HashSet<Guid> EmployeesWithWorkHistory { get; } = new();

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

        public Task<(IReadOnlyList<EmployeeEntity> Items, int TotalCount)> GetPagedListAsync(EmployeeQueryFilter filter, CancellationToken cancellationToken = default)
        {
            var list = Employees.Where(e => !e.IsDeleted).ToList();
            return Task.FromResult<(IReadOnlyList<EmployeeEntity> Items, int TotalCount)>((list, list.Count));
        }

        public Task<string?> GetMaxSequentialCodeAsync(string prefix, int totalLength, CancellationToken cancellationToken = default)
        {
            var max = Employees
                .Where(e => e.EmployeeCode.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(e => e.EmployeeCode)
                .Select(e => e.EmployeeCode)
                .FirstOrDefault();
            return Task.FromResult(max);
        }

        public Task<bool> ExistsAsync(Expression<Func<EmployeeEntity, bool>> predicate, CancellationToken cancellationToken = default) =>
            Task.FromResult(Employees.Where(e => !e.IsDeleted).Any(predicate.Compile()));

        public Task AddAsync(EmployeeEntity employee, CancellationToken cancellationToken = default)
        {
            Employees.Add(employee);
            return Task.CompletedTask;
        }

        public void Update(EmployeeEntity employee) { }
        public void Delete(EmployeeEntity employee) => Employees.Remove(employee);

        public void RemoveAllowances(IEnumerable<EmployeeAllowance> allowances) =>
            RemovedAllowances.AddRange(allowances);

        public void SyncAllowances(EmployeeEntity employee, IEnumerable<EmployeeAllowanceDto> newAllowances, string tenantId)
        {
            employee.Allowances = newAllowances.Select(a => new EmployeeAllowance
            {
                Id = a.Id ?? Guid.NewGuid(),
                EmployeeId = employee.Id,
                TenantId = tenantId,
                Name = a.Name.Trim(),
                Amount = a.Amount
            }).ToList();
        }

        public Task<bool> HasWorkOrPayrollHistoryAsync(Guid employeeId, CancellationToken cancellationToken = default) =>
            Task.FromResult(EmployeesWithWorkHistory.Contains(employeeId));
    }

    private static (EmployeeService Service, FakeEmployeeRepository EmpRepo, FakeUserRepository UserRepo, FakeUnitOfWork Uow, FakeTenantContext TenantCtx) CreateSut()
    {
        var empRepo = new FakeEmployeeRepository();
        var userRepo = new FakeUserRepository();
        var uow = new FakeUnitOfWork();
        var tenantCtx = new FakeTenantContext();
        var service = new EmployeeService(empRepo, userRepo, uow, tenantCtx);
        return (service, empRepo, userRepo, uow, tenantCtx);
    }

    // =========================================================================
    // 1. EMP-BIZ-01: TỰ ĐỘNG SINH MÃ TUẦN TỰ & SANITIZATION (.Trim())
    // =========================================================================

    [Fact]
    public async Task CreateEmployee_EMP_BIZ_01_AutoGeneratesSequentialCodeAndSanitizesStrings()
    {
        var (service, _, _, uow, _) = CreateSut();
        var branchId = Guid.NewGuid();

        var request1 = new CreateEmployeeRequest(
            FullName: "  Nguyễn Văn An  ",
            CitizenId: " 079201001111 ",
            Phone: " 0901112222 ",
            BranchId: branchId,
            Email: "  an.nguyen@mtmart.vn  ",
            Address: "  12 Nguyễn Huệ, Q1  ",
            Note: "  Nhân viên gương mẫu  ",
            SalaryType: "monthly",
            BaseRate: 9000000m,
            BankAccountNumber: " 1900112233 ",
            BankName: " Techcombank ",
            BankAccountHolder: " NGUYEN VAN AN "
        );

        var emp1 = await service.CreateEmployeeAsync(request1);
        Assert.Equal("NV0001", emp1.EmployeeCode);
        Assert.Equal("Nguyễn Văn An", emp1.FullName);
        Assert.Equal("079201001111", emp1.CitizenId);
        Assert.Equal("0901112222", emp1.Phone);
        Assert.Equal("an.nguyen@mtmart.vn", emp1.Email);
        Assert.Equal("12 Nguyễn Huệ, Q1", emp1.Address);
        Assert.Equal("Nhân viên gương mẫu", emp1.Note);
        Assert.Equal("1900112233", emp1.BankAccountNumber);
        Assert.Equal("Techcombank", emp1.BankName);
        Assert.Equal("NGUYEN VAN AN", emp1.BankAccountHolder);
        Assert.Equal("active", emp1.Status);

        var request2 = new CreateEmployeeRequest(
            FullName: "Trần Thị Bình",
            CitizenId: "079201002222",
            Phone: "0901113333",
            BranchId: branchId
        );

        var emp2 = await service.CreateEmployeeAsync(request2);
        Assert.Equal("NV0002", emp2.EmployeeCode);
        Assert.Equal(2, uow.SaveChangesCount);
    }

    // =========================================================================
    // 2. EMP-VAL-06 & EMP-VAL-07: CHẶN TRÙNG LẶP CCCD & SĐT TRONG CÙNG TENANT
    // =========================================================================

    [Fact]
    public async Task CreateEmployee_EMP_VAL_06_DuplicateCitizenId_ThrowsBusinessException()
    {
        var (service, empRepo, _, _, _) = CreateSut();
        empRepo.Employees.Add(new EmployeeEntity
        {
            Id = Guid.NewGuid(),
            EmployeeCode = "NV0001",
            FullName = "Nhân Viên A",
            CitizenId = "079201001234",
            Phone = "0901234567",
            BranchId = Guid.NewGuid()
        });

        var duplicateRequest = new CreateEmployeeRequest(
            FullName: "Nhân Viên B",
            CitizenId: " 079201001234 ",
            Phone: "0988887777",
            BranchId: Guid.NewGuid()
        );

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.CreateEmployeeAsync(duplicateRequest));
        Assert.Equal("DUPLICATE_CITIZEN_ID", ex.ErrorCode);
    }

    [Fact]
    public async Task CreateEmployee_EMP_VAL_07_DuplicatePhone_ThrowsBusinessException()
    {
        var (service, empRepo, _, _, _) = CreateSut();
        empRepo.Employees.Add(new EmployeeEntity
        {
            Id = Guid.NewGuid(),
            EmployeeCode = "NV0001",
            FullName = "Nhân Viên A",
            CitizenId = "079201001234",
            Phone = "0901234567",
            BranchId = Guid.NewGuid()
        });

        var duplicateRequest = new CreateEmployeeRequest(
            FullName: "Nhân Viên B",
            CitizenId: "079201009999",
            Phone: " 0901234567 ",
            BranchId: Guid.NewGuid()
        );

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.CreateEmployeeAsync(duplicateRequest));
        Assert.Equal("DUPLICATE_PHONE", ex.ErrorCode);
    }

    [Fact]
    public async Task CreateEmployee_CustomEmployeeCode_UppercasesAndRejectsDuplicateCode()
    {
        var (service, _, _, _, _) = CreateSut();
        var branchId = Guid.NewGuid();

        var emp1 = await service.CreateEmployeeAsync(new CreateEmployeeRequest(
            FullName: "Nhân Viên Mã Tay",
            CitizenId: "079201007777",
            Phone: "0907778888",
            BranchId: branchId,
            EmployeeCode: " nv_custom-01 ",
            BankAccountHolder: " nguyen van tay "
        ));

        Assert.Equal("NV_CUSTOM-01", emp1.EmployeeCode);
        Assert.Equal("NGUYEN VAN TAY", emp1.BankAccountHolder);

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            service.CreateEmployeeAsync(new CreateEmployeeRequest(
                FullName: "Nhân Viên Trùng Mã",
                CitizenId: "079201008888",
                Phone: "0908889999",
                BranchId: branchId,
                EmployeeCode: "NV_CUSTOM-01"
            )));

        Assert.Equal("EMPLOYEE_CODE_EXISTS", ex.ErrorCode);
    }

    // =========================================================================
    // 3. EMP-BIZ-02: CẬP NHẬT HỒ SƠ, KHÓA MÃ NHÂN VIÊN & ĐỒNG BỘ PHỤ CẤP
    // =========================================================================

    [Fact]
    public async Task UpdateEmployee_EMP_BIZ_02_PreservesEmployeeCodeAndUpdatesAllowancesAndFaceId()
    {
        var (service, empRepo, _, uow, _) = CreateSut();
        var empId = Guid.NewGuid();
        var branchId = Guid.NewGuid();

        empRepo.Employees.Add(new EmployeeEntity
        {
            Id = empId,
            TenantId = "tenant_hr_test",
            EmployeeCode = "NV0005",
            FullName = "Lê Hoàng Cũ",
            CitizenId = "079201005555",
            Phone = "0912345555",
            BranchId = branchId,
            SalaryType = SalaryType.Monthly,
            BaseRate = 7000000m
        });

        var updateRequest = new UpdateEmployeeRequest(
            FullName: "  Lê Hoàng Mới  ",
            CitizenId: "079201005555",
            Phone: "0912345555",
            BranchId: branchId,
            SalaryType: "shift",
            BaseRate: 250000m,
            Allowances: new List<EmployeeAllowanceDto>
            {
                new("Phụ cấp trách nhiệm", 500000m)
            },
            FaceDescriptor: "[0.12, -0.34, 0.56]",
            FaceImageUrls: "[\"https://cdn.mtmart.vn/face1.jpg\"]"
        );

        var updated = await service.UpdateEmployeeAsync(empId, updateRequest);

        Assert.Equal("NV0005", updated.EmployeeCode); // Không bao giờ bị thay đổi
        Assert.Equal("Lê Hoàng Mới", updated.FullName);
        Assert.Equal("shift", updated.SalaryType);
        Assert.Equal(250000m, updated.BaseRate);
        Assert.Single(updated.Allowances);
        Assert.Equal("Phụ cấp trách nhiệm", updated.Allowances[0].Name);
        Assert.NotNull(updated.FaceRegisteredAt);
        Assert.True(uow.SaveChangesCalled);
    }

    [Fact]
    public async Task UpdateEmployee_ConflictCitizenIdWithAnotherEmployee_ThrowsBusinessException()
    {
        var (service, empRepo, _, _, _) = CreateSut();
        var emp1Id = Guid.NewGuid();
        var emp2Id = Guid.NewGuid();
        var branchId = Guid.NewGuid();

        empRepo.Employees.Add(new EmployeeEntity
        {
            Id = emp1Id,
            EmployeeCode = "NV0001",
            FullName = "Nhân Viên 1",
            CitizenId = "079201001111",
            Phone = "0901111111",
            BranchId = branchId
        });
        empRepo.Employees.Add(new EmployeeEntity
        {
            Id = emp2Id,
            EmployeeCode = "NV0002",
            FullName = "Nhân Viên 2",
            CitizenId = "079201002222",
            Phone = "0902222222",
            BranchId = branchId
        });

        var updateRequest = new UpdateEmployeeRequest(
            FullName: "Nhân Viên 2",
            CitizenId: "079201001111", // Đổi sang CCCD của NV1
            Phone: "0902222222",
            BranchId: branchId
        );

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.UpdateEmployeeAsync(emp2Id, updateRequest));
        Assert.Equal("DUPLICATE_CITIZEN_ID", ex.ErrorCode);
    }

    // =========================================================================
    // 4. EMP-BIZ-03: CHUYỂN TRẠNG THÁI LÀM VIỆC & XÓA MỀM BẢO VỆ DỮ LIỆU CÔNG/LƯƠNG
    // =========================================================================

    [Theory]
    [InlineData("inactive", EmployeeStatus.Inactive)]
    [InlineData("active", EmployeeStatus.Active)]
    public async Task UpdateStatus_EMP_BIZ_03_ValidStatus_UpdatesEmployeeStatus(string statusInput, EmployeeStatus expectedStatus)
    {
        var (service, empRepo, _, uow, _) = CreateSut();
        var empId = Guid.NewGuid();
        var entity = new EmployeeEntity
        {
            Id = empId,
            EmployeeCode = "NV0010",
            FullName = "Phạm Văn D",
            CitizenId = "079201001010",
            Phone = "0903334444",
            BranchId = Guid.NewGuid(),
            Status = EmployeeStatus.Active
        };
        empRepo.Employees.Add(entity);

        var ok = await service.UpdateStatusAsync(empId, statusInput);

        Assert.True(ok);
        Assert.Equal(expectedStatus, entity.Status);
        Assert.True(uow.SaveChangesCalled);
    }

    [Fact]
    public async Task UpdateStatus_BR_EMP_05_SynchronizesLinkedUserIsActiveFlag()
    {
        var (service, empRepo, userRepo, _, _) = CreateSut();
        var empId = Guid.NewGuid();
        var user = new User { Id = "user_linked_01", UserName = "nv.test", IsActive = true };
        userRepo.Users.Add(user);

        empRepo.Employees.Add(new EmployeeEntity
        {
            Id = empId,
            EmployeeCode = "NV0015",
            FullName = "Hoàng Văn E",
            CitizenId = "079201001515",
            Phone = "0903335555",
            BranchId = Guid.NewGuid(),
            Status = EmployeeStatus.Active,
            UserId = user.Id
        });

        await service.UpdateStatusAsync(empId, "inactive");
        Assert.False(user.IsActive);

        await service.UpdateStatusAsync(empId, "active");
        Assert.True(user.IsActive);
    }

    [Fact]
    public async Task UpdateStatus_InvalidStatusString_ThrowsArgumentException()
    {
        var (service, empRepo, _, _, _) = CreateSut();
        var empId = Guid.NewGuid();
        empRepo.Employees.Add(new EmployeeEntity
        {
            Id = empId,
            EmployeeCode = "NV0010",
            FullName = "Phạm Văn D",
            CitizenId = "079201001010",
            Phone = "0903334444",
            BranchId = Guid.NewGuid()
        });

        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateStatusAsync(empId, "invalid_status"));
    }

    [Fact]
    public async Task DeleteEmployee_EMP_BIZ_03_WhenHasWorkOrPayrollHistory_BlocksHardDeleteAndTransitionsToInactive()
    {
        var (service, empRepo, userRepo, uow, _) = CreateSut();
        var empId = Guid.NewGuid();
        var linkedUser = new User { Id = "u_work_history_01", UserName = "nv.chamcong", IsActive = true };
        userRepo.Users.Add(linkedUser);

        var entity = new EmployeeEntity
        {
            Id = empId,
            EmployeeCode = "NV0011",
            FullName = "Nhân Viên Đã Chấm Công",
            CitizenId = "079201001122",
            Phone = "0904445555",
            BranchId = Guid.NewGuid(),
            Status = EmployeeStatus.Active,
            IsDeleted = false,
            UserId = linkedUser.Id
        };
        empRepo.Employees.Add(entity);
        empRepo.EmployeesWithWorkHistory.Add(empId);

        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.DeleteEmployeeAsync(empId));

        Assert.Equal("EMPLOYEE_HAS_WORK_HISTORY", ex.ErrorCode);
        Assert.False(entity.IsDeleted); // Vẫn giữ trong danh sách nhân viên (không xóa ẩn)
        Assert.Equal(EmployeeStatus.Inactive, entity.Status); // Đã chuyển sang trạng thái Ngừng làm việc ("Đã nghỉ")
        Assert.False(linkedUser.IsActive); // Khóa tài khoản đăng nhập liên kết
        Assert.True(uow.SaveChangesCalled);
    }

    [Fact]
    public async Task DeleteEmployee_WhenNoWorkHistory_SoftDeletesAndRemovesAllowances()
    {
        var (service, empRepo, _, uow, _) = CreateSut();
        var empId = Guid.NewGuid();
        var entity = new EmployeeEntity
        {
            Id = empId,
            EmployeeCode = "NV0012",
            FullName = "Nhân Viên Mới Tạo Nhầm",
            CitizenId = "079201001133",
            Phone = "0904446666",
            BranchId = Guid.NewGuid(),
            Status = EmployeeStatus.Active,
            Allowances = new List<EmployeeAllowance>
            {
                new() { Id = Guid.NewGuid(), EmployeeId = empId, Name = "Ăn trưa", Amount = 500000m }
            }
        };
        empRepo.Employees.Add(entity);

        var deleted = await service.DeleteEmployeeAsync(empId);

        Assert.True(deleted);
        Assert.True(entity.IsDeleted);
        Assert.Equal(EmployeeStatus.Inactive, entity.Status);
        Assert.Single(empRepo.RemovedAllowances);
        Assert.True(uow.SaveChangesCalled);
    }

    // =========================================================================
    // 5. TÀI KHOẢN ĐĂNG NHẬP IDENTITY (CREATE / UPDATE / DUPLICATE CHECK)
    // =========================================================================

    [Fact]
    public async Task CreateIdentityAccount_ValidRequest_LinksUserToEmployeeAndNormalizesEmployeeRoleToStaff()
    {
        var (service, empRepo, userRepo, uow, _) = CreateSut();
        var empId = Guid.NewGuid();
        var entity = new EmployeeEntity
        {
            Id = empId,
            TenantId = "tenant_hr_test",
            EmployeeCode = "NV0020",
            FullName = "Đặng Thu Thảo",
            CitizenId = "079201002020",
            Phone = "0905556666",
            BranchId = Guid.NewGuid()
        };
        empRepo.Employees.Add(entity);

        var userId = await service.CreateIdentityAccountAsync(
            empId,
            new CreateIdentityAccountRequest("thao.dang", "Staff@123", "EMPLOYEE"));

        Assert.False(string.IsNullOrWhiteSpace(userId));
        Assert.Equal(userId, entity.UserId);
        Assert.Single(userRepo.Users);
        Assert.Equal("STAFF", userRepo.Users[0].TenantUsers.First().Role);
        Assert.True(uow.SaveChangesCalled);
    }

    [Fact]
    public async Task CreateIdentityAccount_DuplicateUsername_ThrowsBusinessException()
    {
        var (service, empRepo, userRepo, _, _) = CreateSut();
        var empId = Guid.NewGuid();
        empRepo.Employees.Add(new EmployeeEntity
        {
            Id = empId,
            EmployeeCode = "NV0021",
            FullName = "Vũ Văn Nam",
            CitizenId = "079201002121",
            Phone = "0905557777",
            BranchId = Guid.NewGuid()
        });
        userRepo.Users.Add(new User { Id = "u_existing", UserName = "nam.vu" });

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            service.CreateIdentityAccountAsync(empId, new CreateIdentityAccountRequest("nam.vu", "Staff@123", "STAFF")));

        Assert.Equal("DUPLICATE_USERNAME", ex.ErrorCode);
    }
}

