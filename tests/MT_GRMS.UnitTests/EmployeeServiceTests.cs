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

namespace MT_GRMS.UnitTests;

public class EmployeeServiceTests
{
    private class FakeUnitOfWork : IUnitOfWork
    {
        public bool SaveChangesCalled { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesCalled = true;
            return Task.FromResult(1);
        }
    }

    private class FakeTenantContext : ITenantContext
    {
        public string CurrentTenantId { get; set; } = "tenant_test_01";
        public Guid CurrentUserId { get; set; } = Guid.NewGuid();
        public string CurrentRole { get; set; } = "STORE_OWNER";
        public Guid? CurrentBranchId { get; set; } = Guid.NewGuid();

        public void SetContext(string tenantId, Guid userId, Guid? branchId)
        {
            CurrentTenantId = tenantId;
            CurrentUserId = userId;
            CurrentBranchId = branchId;
        }

        public void SetRole(string? role)
        {
            CurrentRole = role ?? string.Empty;
        }
    }

    private class FakeUserRepository : IUserRepository
    {
        public List<User> Users { get; } = new();

        public Task<User?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Users.FirstOrDefault(u => u.Id == id));
        }

        public Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Users.FirstOrDefault(u => string.Equals(u.UserName, username, StringComparison.OrdinalIgnoreCase)));
        }

        public Task<IReadOnlyList<User>> FindAsync(Expression<Func<User, bool>> predicate, CancellationToken cancellationToken = default)
        {
            var compiled = predicate.Compile();
            return Task.FromResult<IReadOnlyList<User>>(Users.Where(compiled).ToList());
        }

        public Task<IReadOnlyList<User>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<User>>(Users);
        }

        public Task AddAsync(User user, CancellationToken cancellationToken = default)
        {
            Users.Add(user);
            return Task.CompletedTask;
        }

        public void Update(User user) { }
        public void Delete(User user) { Users.Remove(user); }
        public Task<bool> ExistsByPhoneOrEmailAsync(string phone, string? email, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Users.Any(u => u.Phone == phone || (!string.IsNullOrEmpty(email) && u.Email == email)));
        }
        public Task<User> RegisterStoreOwnerAsync(User user, Tenant tenant, Branch branch, CancellationToken cancellationToken = default)
        {
            Users.Add(user);
            return Task.FromResult(user);
        }
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private class FakeEmployeeRepository : IEmployeeRepository
    {
        public List<Employee> Employees { get; } = new();

        public Task<Employee?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Employees.FirstOrDefault(e => e.Id == id && !e.IsDeleted));
        }

        public Task<Employee?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Employees.FirstOrDefault(e => string.Equals(e.EmployeeCode, code, StringComparison.OrdinalIgnoreCase) && !e.IsDeleted));
        }

        public Task<Employee?> GetByCitizenIdAsync(string citizenId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Employees.FirstOrDefault(e => e.CitizenId == citizenId && !e.IsDeleted));
        }

        public Task<Employee?> GetByPhoneAsync(string phone, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Employees.FirstOrDefault(e => e.Phone == phone && !e.IsDeleted));
        }

        public Task<Employee?> GetByUserIdAsync(string userId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Employees.FirstOrDefault(e => e.UserId == userId && !e.IsDeleted));
        }

        public Task<(IReadOnlyList<Employee> Items, int TotalCount)> GetPagedListAsync(EmployeeQueryFilter filter, CancellationToken cancellationToken = default)
        {
            var query = Employees.Where(e => !e.IsDeleted);
            if (!string.IsNullOrWhiteSpace(filter.Keyword))
            {
                var kw = filter.Keyword.ToLower();
                query = query.Where(e => e.FullName.ToLower().Contains(kw) || e.EmployeeCode.ToLower().Contains(kw) || e.Phone.Contains(kw));
            }
            if (filter.BranchIds != null && filter.BranchIds.Count > 0)
            {
                var validBranchIds = filter.BranchIds.Where(id => id != Guid.Empty).ToList();
                if (validBranchIds.Count > 0)
                {
                    query = query.Where(e => validBranchIds.Contains(e.BranchId));
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

            var list = query.ToList();
            return Task.FromResult<(IReadOnlyList<Employee> Items, int TotalCount)>((list, list.Count));
        }

        public Task<string?> GetMaxSequentialCodeAsync(string prefix, int totalLength, CancellationToken cancellationToken = default)
        {
            var max = Employees
                .Where(e => e.EmployeeCode.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && e.EmployeeCode.Length == totalLength)
                .OrderByDescending(e => e.EmployeeCode)
                .Select(e => e.EmployeeCode)
                .FirstOrDefault();

            return Task.FromResult(max);
        }

        public Task<bool> ExistsAsync(Expression<Func<Employee, bool>> predicate, CancellationToken cancellationToken = default)
        {
            var compiled = predicate.Compile();
            return Task.FromResult(Employees.Where(e => !e.IsDeleted).Any(compiled));
        }

        public Task AddAsync(Employee employee, CancellationToken cancellationToken = default)
        {
            Employees.Add(employee);
            return Task.CompletedTask;
        }

        public void Update(Employee employee) { }
        public void Delete(Employee employee) { Employees.Remove(employee); }

        public List<EmployeeAllowance> RemovedAllowances { get; } = new();

        public void RemoveAllowances(IEnumerable<EmployeeAllowance> allowances)
        {
            RemovedAllowances.AddRange(allowances);
        }

        public void SyncAllowances(Employee employee, IEnumerable<EmployeeAllowanceDto> newAllowances, string tenantId) { }

        public HashSet<Guid> EmployeesWithWorkHistory { get; } = new();

        public Task<bool> HasWorkOrPayrollHistoryAsync(Guid employeeId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(EmployeesWithWorkHistory.Contains(employeeId));
        }
    }

    [Fact]
    public async Task CreateEmployee_WhenNoEmployees_ShouldGenerateCode_NV0001()
    {
        // Arrange
        var empRepo = new FakeEmployeeRepository();
        var userRepo = new FakeUserRepository();
        var uow = new FakeUnitOfWork();
        var tenantCtx = new FakeTenantContext();
        var service = new EmployeeService(empRepo, userRepo, uow, tenantCtx);

        var request = new CreateEmployeeRequest(
            FullName: "Nguyễn Văn A",
            CitizenId: "001202001111",
            Phone: "0987654321",
            BranchId: Guid.NewGuid(),
            SalaryType: "monthly",
            BaseRate: 8500000m
        );

        // Act
        var result = await service.CreateEmployeeAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("NV0001", result.EmployeeCode);
        Assert.Equal("Nguyễn Văn A", result.FullName);
        Assert.Equal("active", result.Status);
        Assert.Equal(8500000m, result.BaseRate);
        Assert.True(uow.SaveChangesCalled);
    }

    [Fact]
    public async Task CreateEmployee_WhenExistingCodesExist_ShouldIncrementCodeSequentially()
    {
        // Arrange
        var empRepo = new FakeEmployeeRepository();
        empRepo.Employees.Add(new Employee
        {
            EmployeeCode = "NV0008",
            FullName = "Nhân viên cũ",
            CitizenId = "001200000008",
            Phone = "0987000008",
            BranchId = Guid.NewGuid()
        });

        var userRepo = new FakeUserRepository();
        var uow = new FakeUnitOfWork();
        var tenantCtx = new FakeTenantContext();
        var service = new EmployeeService(empRepo, userRepo, uow, tenantCtx);

        var request = new CreateEmployeeRequest(
            FullName: "Trần Thị B",
            CitizenId: "001202002222",
            Phone: "0987654322",
            BranchId: Guid.NewGuid(),
            BaseRate: 9000000m
        );

        // Act
        var result = await service.CreateEmployeeAsync(request);

        // Assert
        Assert.Equal("NV0009", result.EmployeeCode);
    }

    [Fact]
    public async Task CreateEmployee_DuplicateCitizenId_ShouldThrowBusinessException()
    {
        // Arrange
        var empRepo = new FakeEmployeeRepository();
        empRepo.Employees.Add(new Employee
        {
            EmployeeCode = "NV0001",
            FullName = "Lê Văn C",
            CitizenId = "001202003333",
            Phone = "0987654333",
            BranchId = Guid.NewGuid()
        });

        var userRepo = new FakeUserRepository();
        var uow = new FakeUnitOfWork();
        var tenantCtx = new FakeTenantContext();
        var service = new EmployeeService(empRepo, userRepo, uow, tenantCtx);

        var request = new CreateEmployeeRequest(
            FullName: "Lê Văn D",
            CitizenId: "001202003333", // Trùng CCCD
            Phone: "0987654334",
            BranchId: Guid.NewGuid()
        );

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.CreateEmployeeAsync(request));
        Assert.Equal("DUPLICATE_CITIZEN_ID", ex.ErrorCode);
    }

    [Fact]
    public async Task CreateEmployee_DuplicatePhone_ShouldThrowBusinessException()
    {
        // Arrange
        var empRepo = new FakeEmployeeRepository();
        empRepo.Employees.Add(new Employee
        {
            EmployeeCode = "NV0001",
            FullName = "Lê Văn C",
            CitizenId = "001202003333",
            Phone = "0987654333",
            BranchId = Guid.NewGuid()
        });

        var userRepo = new FakeUserRepository();
        var uow = new FakeUnitOfWork();
        var tenantCtx = new FakeTenantContext();
        var service = new EmployeeService(empRepo, userRepo, uow, tenantCtx);

        var request = new CreateEmployeeRequest(
            FullName: "Lê Văn D",
            CitizenId: "001202004444",
            Phone: "0987654333", // Trùng Phone
            BranchId: Guid.NewGuid()
        );

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.CreateEmployeeAsync(request));
        Assert.Equal("DUPLICATE_PHONE", ex.ErrorCode);
    }

    [Fact]
    public async Task CreateIdentityAccount_ValidRequest_ShouldCreateUserAndLinkEmployee()
    {
        // Arrange
        var empId = Guid.NewGuid();
        var empRepo = new FakeEmployeeRepository();
        var employee = new Employee
        {
            Id = empId,
            EmployeeCode = "NV0001",
            FullName = "Nguyễn Thu Ngân",
            CitizenId = "001202005555",
            Phone = "0987654555",
            Email = "thungan@mtgrms.vn",
            BranchId = Guid.NewGuid(),
            UserId = null
        };
        empRepo.Employees.Add(employee);

        var userRepo = new FakeUserRepository();
        var uow = new FakeUnitOfWork();
        var tenantCtx = new FakeTenantContext();
        var service = new EmployeeService(empRepo, userRepo, uow, tenantCtx);

        var request = new CreateIdentityAccountRequest(
            Username: "thungan_01",
            Password: "SecurePassword123",
            Role: "CASHIER"
        );

        // Act
        var userId = await service.CreateIdentityAccountAsync(empId, request);

        // Assert
        Assert.False(string.IsNullOrWhiteSpace(userId));
        Assert.Equal(userId, employee.UserId);

        var createdUser = userRepo.Users.FirstOrDefault(u => u.Id == userId);
        Assert.NotNull(createdUser);
        Assert.Equal("thungan_01", createdUser.UserName);
        Assert.Single(createdUser.TenantUsers);
        Assert.Equal("CASHIER", createdUser.TenantUsers.First().Role);
    }

    [Fact]
    public async Task CreateIdentityAccount_WhenEmployeeAlreadyHasUser_ShouldThrowConflict()
    {
        // Arrange
        var empId = Guid.NewGuid();
        var empRepo = new FakeEmployeeRepository();
        var employee = new Employee
        {
            Id = empId,
            EmployeeCode = "NV0001",
            FullName = "Đã có tài khoản",
            CitizenId = "001202006666",
            Phone = "0987654666",
            BranchId = Guid.NewGuid(),
            UserId = "user_existing_123"
        };
        empRepo.Employees.Add(employee);

        var userRepo = new FakeUserRepository();
        var uow = new FakeUnitOfWork();
        var tenantCtx = new FakeTenantContext();
        var service = new EmployeeService(empRepo, userRepo, uow, tenantCtx);

        var request = new CreateIdentityAccountRequest("new_user", "pass123", "STAFF");

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BusinessException>(() => service.CreateIdentityAccountAsync(empId, request));
        Assert.Equal("CONFLICT", ex.ErrorCode);
    }

    [Fact]
    public async Task DeleteEmployee_ShouldSoftDelete()
    {
        // Arrange
        var empId = Guid.NewGuid();
        var empRepo = new FakeEmployeeRepository();
        var employee = new Employee
        {
            Id = empId,
            EmployeeCode = "NV0001",
            FullName = "Nguyễn Nghỉ Việc",
            CitizenId = "001202007777",
            Phone = "0987654777",
            BranchId = Guid.NewGuid(),
            Status = EmployeeStatus.Active,
            IsDeleted = false
        };
        empRepo.Employees.Add(employee);

        var userRepo = new FakeUserRepository();
        var uow = new FakeUnitOfWork();
        var tenantCtx = new FakeTenantContext();
        var service = new EmployeeService(empRepo, userRepo, uow, tenantCtx);

        // Act
        var result = await service.DeleteEmployeeAsync(empId);

        // Assert
        Assert.True(result);
        Assert.True(employee.IsDeleted);
        Assert.Equal(EmployeeStatus.Inactive, employee.Status);
    }

    [Fact]
    public async Task CreateEmployee_WithAllowancesList_ShouldStoreAndReturnAllowances()
    {
        // Arrange
        var empRepo = new FakeEmployeeRepository();
        var userRepo = new FakeUserRepository();
        var uow = new FakeUnitOfWork();
        var tenantCtx = new FakeTenantContext();
        var service = new EmployeeService(empRepo, userRepo, uow, tenantCtx);

        var allowances = new List<EmployeeAllowanceDto>
        {
            new("Phụ cấp ăn trưa", 650000m),
            new("Phụ cấp xăng xe", 300000m),
            new("Phụ cấp trách nhiệm", 1000000m)
        };

        var request = new CreateEmployeeRequest(
            FullName: "Lê Phụ Cấp",
            CitizenId: "001202008888",
            Phone: "0987654888",
            BranchId: Guid.NewGuid(),
            Allowances: allowances
        );

        // Act
        var result = await service.CreateEmployeeAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(3, result.Allowances.Count);
        Assert.Equal("Phụ cấp ăn trưa", result.Allowances[0].Name);
        Assert.Equal(650000m, result.Allowances[0].Amount);
        Assert.Equal("Phụ cấp trách nhiệm", result.Allowances[2].Name);
        Assert.Equal(1000000m, result.Allowances[2].Amount);
    }

    [Theory]
    [InlineData("", "001202009999", "0987654999", 5000000)]
    [InlineData("Nguyen Van Test", "", "0987654999", 5000000)]
    [InlineData("Nguyen Van Test", "001202009999", "", 5000000)]
    [InlineData("Nguyen Van Test", "001202009999", "0987654999", -1)]
    public async Task CreateEmployee_InvalidInput_ShouldThrowArgumentException(string fullName, string citizenId, string phone, decimal baseRate)
    {
        // Arrange
        var empRepo = new FakeEmployeeRepository();
        var userRepo = new FakeUserRepository();
        var uow = new FakeUnitOfWork();
        var tenantCtx = new FakeTenantContext();
        var service = new EmployeeService(empRepo, userRepo, uow, tenantCtx);

        var request = new CreateEmployeeRequest(
            FullName: fullName,
            CitizenId: citizenId,
            Phone: phone,
            BranchId: Guid.NewGuid(),
            BaseRate: baseRate
        );

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateEmployeeAsync(request));
    }

    [Fact]
    public async Task CreateEmployee_EmptyBranchId_ShouldThrowArgumentException()
    {
        // Arrange
        var empRepo = new FakeEmployeeRepository();
        var userRepo = new FakeUserRepository();
        var uow = new FakeUnitOfWork();
        var tenantCtx = new FakeTenantContext();
        var service = new EmployeeService(empRepo, userRepo, uow, tenantCtx);

        var request = new CreateEmployeeRequest(
            FullName: "Nguyen Van Valid",
            CitizenId: "001202009999",
            Phone: "0987654999",
            BranchId: Guid.Empty,
            BaseRate: 5000000
        );

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateEmployeeAsync(request));
    }

    [Theory]
    [InlineData("", "password123")]
    [InlineData("valid_user", "")]
    public async Task CreateIdentityAccount_InvalidInput_ShouldThrowArgumentException(string username, string password)
    {
        // Arrange
        var empRepo = new FakeEmployeeRepository();
        var userRepo = new FakeUserRepository();
        var uow = new FakeUnitOfWork();
        var tenantCtx = new FakeTenantContext();
        var service = new EmployeeService(empRepo, userRepo, uow, tenantCtx);

        var request = new CreateIdentityAccountRequest(username, password, "STAFF");

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateIdentityAccountAsync(Guid.NewGuid(), request));
    }

    [Fact]
    public async Task CreateEmployee_WithNewFields_ShouldMapAllFieldsCorrectly()
    {
        // Arrange
        var empRepo = new FakeEmployeeRepository();
        var userRepo = new FakeUserRepository();
        var uow = new FakeUnitOfWork();
        var tenantCtx = new FakeTenantContext();
        var service = new EmployeeService(empRepo, userRepo, uow, tenantCtx);

        var startDate = new DateTime(2025, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var request = new CreateEmployeeRequest(
            FullName: "Lê Văn Hùng",
            CitizenId: "001202008888",
            Phone: "0987654888",
            BranchId: Guid.NewGuid(),
            Email: "hung.le@mtgrms.vn",
            Address: "123 Đường Số 7, Q. Bình Tân, TP.HCM",
            StartDate: startDate,
            Note: "Nhân viên bán hàng chính thức",
            SalaryType: "shift",
            BaseRate: 250000m,
            OtWeekday: "150%",
            OtSunday: "200%",
            BankAccountNumber: "0123456789",
            BankName: "Vietcombank",
            BankAccountHolder: "LE VAN HUNG",
            AvatarUrl: "https://example.com/avatar.jpg"
        );

        // Act
        var result = await service.CreateEmployeeAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("123 Đường Số 7, Q. Bình Tân, TP.HCM", result.Address);
        Assert.Equal(startDate, result.StartDate);
        Assert.Equal("Nhân viên bán hàng chính thức", result.Note);
        Assert.Equal("shift", result.SalaryType);
        Assert.Equal(250000m, result.BaseRate);
        Assert.Equal("150%", result.OtWeekday);
        Assert.Equal("200%", result.OtSunday);
        Assert.Equal("0123456789", result.BankAccountNumber);
        Assert.Equal("Vietcombank", result.BankName);
        Assert.Equal("LE VAN HUNG", result.BankAccountHolder);
        Assert.Equal("https://example.com/avatar.jpg", result.AvatarUrl);
    }

    [Fact]
    public async Task UpdateEmployee_WithNewFields_ShouldUpdateSuccessfully()
    {
        // Arrange
        var branchId = Guid.NewGuid();
        var existing = new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeCode = "NV0010",
            FullName = "Nguyễn Văn Ban Đầu",
            CitizenId = "001202007777",
            Phone = "0987654777",
            BranchId = branchId,
            SalaryType = SalaryType.Monthly,
            BaseRate = 8000000m
        };

        var empRepo = new FakeEmployeeRepository();
        empRepo.Employees.Add(existing);
        var userRepo = new FakeUserRepository();
        var uow = new FakeUnitOfWork();
        var tenantCtx = new FakeTenantContext();
        var service = new EmployeeService(empRepo, userRepo, uow, tenantCtx);

        var updatedDate = new DateTime(2025, 4, 15, 0, 0, 0, DateTimeKind.Utc);
        var updateRequest = new UpdateEmployeeRequest(
            FullName: "Nguyễn Văn Đã Đổi",
            CitizenId: "001202007777",
            Phone: "0987654777",
            BranchId: branchId,
            Address: "456 CMT8, Q.3, TP.HCM",
            StartDate: updatedDate,
            Note: "Chuyển sang ca làm việc linh hoạt",
            SalaryType: "shift",
            BaseRate: 280000m,
            OtWeekday: "160%",
            OtSunday: "220%",
            BankAccountNumber: "9876543210",
            BankName: "MBBank",
            BankAccountHolder: "NGUYEN VAN DA DOI",
            AvatarUrl: "https://example.com/new-avatar.jpg"
        );

        // Act
        var result = await service.UpdateEmployeeAsync(existing.Id, updateRequest);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Nguyễn Văn Đã Đổi", result.FullName);
        Assert.Equal("456 CMT8, Q.3, TP.HCM", result.Address);
        Assert.Equal(updatedDate, result.StartDate);
        Assert.Equal("Chuyển sang ca làm việc linh hoạt", result.Note);
        Assert.Equal("shift", result.SalaryType);
        Assert.Equal(280000m, result.BaseRate);
        Assert.Equal("160%", result.OtWeekday);
        Assert.Equal("220%", result.OtSunday);
        Assert.Equal("9876543210", result.BankAccountNumber);
        Assert.Equal("MBBank", result.BankName);
        Assert.Equal("NGUYEN VAN DA DOI", result.BankAccountHolder);
    }

    [Fact]
    public async Task GetEmployeesPagedAsync_WithSalaryTypeFilter_ShouldReturnFilteredResults()
    {
        // Arrange
        var branchId = Guid.NewGuid();
        var empRepo = new FakeEmployeeRepository();
        empRepo.Employees.Add(new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeCode = "NV0001",
            FullName = "NV Theo Thang",
            CitizenId = "001200000001",
            Phone = "0987000001",
            BranchId = branchId,
            SalaryType = SalaryType.Monthly,
            BaseRate = 9000000m
        });
        empRepo.Employees.Add(new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeCode = "NV0002",
            FullName = "NV Theo Ca",
            CitizenId = "001200000002",
            Phone = "0987000002",
            BranchId = branchId,
            SalaryType = SalaryType.Shift,
            BaseRate = 250000m
        });

        var userRepo = new FakeUserRepository();
        var uow = new FakeUnitOfWork();
        var tenantCtx = new FakeTenantContext();
        var service = new EmployeeService(empRepo, userRepo, uow, tenantCtx);

        // Act - Filter by Shift
        var filter = new EmployeeQueryFilter(SalaryType: "Shift");
        var result = await service.GetEmployeesPagedAsync(filter);

        // Assert
        Assert.Single(result.Items);
        Assert.Equal("NV0002", result.Items.First().EmployeeCode);
        Assert.Equal("shift", result.Items.First().SalaryType);
    }

    [Fact]
    public async Task GetEmployeesPagedAsync_WithMultipleBranchIds_ShouldFilterCorrectly()
    {
        // Arrange
        var branch1 = Guid.NewGuid();
        var branch2 = Guid.NewGuid();
        var branch3 = Guid.NewGuid();
        var empRepo = new FakeEmployeeRepository();
        empRepo.Employees.Add(new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeCode = "NV0001",
            FullName = "NV Chi Nhanh 1",
            CitizenId = "001200000001",
            Phone = "0987000001",
            BranchId = branch1,
            SalaryType = SalaryType.Monthly
        });
        empRepo.Employees.Add(new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeCode = "NV0002",
            FullName = "NV Chi Nhanh 2",
            CitizenId = "001200000002",
            Phone = "0987000002",
            BranchId = branch2,
            SalaryType = SalaryType.Shift
        });
        empRepo.Employees.Add(new Employee
        {
            Id = Guid.NewGuid(),
            EmployeeCode = "NV0003",
            FullName = "NV Chi Nhanh 3",
            CitizenId = "001200000003",
            Phone = "0987000003",
            BranchId = branch3,
            SalaryType = SalaryType.Hourly
        });

        var userRepo = new FakeUserRepository();
        var uow = new FakeUnitOfWork();
        var tenantCtx = new FakeTenantContext();
        var service = new EmployeeService(empRepo, userRepo, uow, tenantCtx);

        // Act - Filter by branch1 and branch2
        var filter = new EmployeeQueryFilter(BranchIds: new List<Guid> { branch1, branch2 });
        var result = await service.GetEmployeesPagedAsync(filter);

        // Assert
        Assert.Equal(2, result.Items.Count);
        Assert.Contains(result.Items, e => e.EmployeeCode == "NV0001");
        Assert.Contains(result.Items, e => e.EmployeeCode == "NV0002");
        Assert.DoesNotContain(result.Items, e => e.EmployeeCode == "NV0003");
    }

    [Fact]
    public async Task DeleteEmployee_WhenHasWorkOrPayrollHistory_ShouldThrowBusinessException()
    {
        // Arrange
        var empRepo = new FakeEmployeeRepository();
        var userRepo = new FakeUserRepository();
        var uow = new FakeUnitOfWork();
        var tenantCtx = new FakeTenantContext();
        var service = new EmployeeService(empRepo, userRepo, uow, tenantCtx);

        var employeeId = Guid.NewGuid();
        var employee = new Employee
        {
            Id = employeeId,
            TenantId = "tenant_test_01",
            EmployeeCode = "NV0001",
            FullName = "Nguyễn Văn Đã Đi Làm",
            CitizenId = "001202009988",
            Phone = "0987654321",
            BranchId = Guid.NewGuid(),
            Status = EmployeeStatus.Active
        };
        await empRepo.AddAsync(employee);
        empRepo.EmployeesWithWorkHistory.Add(employeeId);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            service.DeleteEmployeeAsync(employeeId));

        Assert.Equal("EMPLOYEE_HAS_WORK_HISTORY", ex.ErrorCode);
        Assert.Contains("không thể xóa", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(employee.IsDeleted);
    }

    [Fact]
    public async Task DeleteEmployee_WhenNoWorkHistory_ShouldSoftDeleteSuccessfully()
    {
        // Arrange
        var empRepo = new FakeEmployeeRepository();
        var userRepo = new FakeUserRepository();
        var uow = new FakeUnitOfWork();
        var tenantCtx = new FakeTenantContext();
        var service = new EmployeeService(empRepo, userRepo, uow, tenantCtx);

        var employeeId = Guid.NewGuid();
        var employee = new Employee
        {
            Id = employeeId,
            TenantId = "tenant_test_01",
            EmployeeCode = "NV0002",
            FullName = "Trần Thị Mới Đăng Ký",
            CitizenId = "001202009977",
            Phone = "0912345678",
            BranchId = Guid.NewGuid(),
            Status = EmployeeStatus.Active,
            Allowances = new List<EmployeeAllowance>
            {
                new()
                {
                    EmployeeId = employeeId,
                    TenantId = "tenant_test_01",
                    Name = "Phụ cấp ăn trưa",
                    Amount = 650000m
                }
            }
        };
        await empRepo.AddAsync(employee);

        // Act
        var result = await service.DeleteEmployeeAsync(employeeId);

        // Assert
        Assert.True(result);
        Assert.True(employee.IsDeleted);
        Assert.Equal(EmployeeStatus.Inactive, employee.Status);
        Assert.Single(empRepo.RemovedAllowances);
        Assert.Equal("Phụ cấp ăn trưa", empRepo.RemovedAllowances[0].Name);
        Assert.True(uow.SaveChangesCalled);
    }
}
