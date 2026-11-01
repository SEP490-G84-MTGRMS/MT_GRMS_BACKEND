using System.Linq.Expressions;
using MT_GRMS.Application.Auth;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.HR.Employees;
using MT_GRMS.Application.HR.Employees.Dtos;
using MT_GRMS.Domain.Entities.HR;
using MT_GRMS.Domain.Entities.Identity;
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

        public Task<(IReadOnlyList<Employee> Items, int TotalCount)> GetPagedListAsync(EmployeeQueryFilter filter, CancellationToken cancellationToken = default)
        {
            var query = Employees.Where(e => !e.IsDeleted);
            if (!string.IsNullOrWhiteSpace(filter.Keyword))
            {
                var kw = filter.Keyword.ToLower();
                query = query.Where(e => e.FullName.ToLower().Contains(kw) || e.EmployeeCode.ToLower().Contains(kw) || e.Phone.Contains(kw));
            }
            if (filter.BranchId.HasValue && filter.BranchId.Value != Guid.Empty)
            {
                query = query.Where(e => e.BranchId == filter.BranchId.Value);
            }
            if (!string.IsNullOrWhiteSpace(filter.Status))
            {
                query = query.Where(e => string.Equals(e.Status, filter.Status, StringComparison.OrdinalIgnoreCase));
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
            Status = "active",
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
        Assert.Equal("inactive", employee.Status);
    }
}
