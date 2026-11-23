using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Entities.Identity;
using MT_GRMS.Domain.Entities.HR;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Entities.System;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Infrastructure.Persistence;
using MT_GRMS.Infrastructure.Repositories;
using Xunit;

namespace MT_GRMS.IntegrationTests;

public class UserDatabaseIntegrationTests
{
    [Theory]
    [InlineData("role")]
    [InlineData("membership")]
    [InlineData("branch")]
    [InlineData("password")]
    [InlineData("inactive")]
    public async Task SecurityChanges_RevokeBothTokens(string change)
    {
        using var db = CreateDbContext();
        var user = new User { Id = "session-user", SecurityStamp = "old", RefreshToken = "refresh", RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(1) };
        var membership = new TenantUser { UserId = user.Id, TenantId = "tenant-a", Role = "STORE_OWNER" };
        var employee = new MT_GRMS.Domain.Entities.HR.Employee { UserId = user.Id, TenantId = "tenant-a", BranchId = Guid.NewGuid() };
        db.Users.Add(user);
        db.TenantUsers.Add(membership);
        db.Employees.Add(employee);
        await db.SaveChangesAsync();
        switch (change)
        {
            case "role": membership.Role = "STAFF"; break;
            case "membership": membership.IsActive = false; break;
            case "branch": employee.BranchId = Guid.NewGuid(); break;
            case "password": user.PasswordHash = "new-hash"; break;
            case "inactive": user.IsActive = false; break;
        }
        await db.SaveChangesAsync();
        Assert.NotEqual("old", user.SecurityStamp);
        Assert.Null(user.RefreshToken);
        Assert.Null(user.RefreshTokenExpiryTime);
    }

    [Theory]
    [InlineData("valid", true)]
    [InlineData("foreign", false)]
    [InlineData("inactive", false)]
    [InlineData("deleted", false)]
    [InlineData("employee-inactive", false)]
    public async Task AssignmentLookup_RequiresActiveBranchInSameTenant(string scenario, bool allowed)
    {
        using var db = CreateDbContext();
        var branch = new MT_GRMS.Domain.Entities.SaaS.Branch
        {
            TenantId = scenario == "foreign" ? "tenant-b" : "tenant-a",
            IsActive = scenario != "inactive", IsDeleted = scenario == "deleted"
        };
        db.Branches.Add(branch);
        db.Employees.Add(new MT_GRMS.Domain.Entities.HR.Employee
        {
            TenantId = "tenant-a", UserId = "staff", BranchId = branch.Id,
            Status = scenario == "employee-inactive" ? MT_GRMS.Domain.Enums.EmployeeStatus.Inactive : MT_GRMS.Domain.Enums.EmployeeStatus.Active
        });
        await db.SaveChangesAsync();
        var result = await new UserRepository(db).GetEmployeeBranchIdAsync("staff", "tenant-a");
        if (allowed) Assert.Equal(branch.Id, result);
        else Assert.Null(result);
    }

    private AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"MT_GRMS_IntTestDb_{Guid.NewGuid()}")
            .Options;

        return new AppDbContext(options);
    }

    [Fact]
    public async Task UserRepository_Crud_And_Queries_Should_Work_Correctly()
    {
        using var context = CreateDbContext();
        var repo = new UserRepository(context);

        var user = new User
        {
            Id = "usr_01",
            UserName = "johndoe",
            Email = "john@example.com",
            NormalizedEmail = "john@example.com",
            Phone = "0912 345 678",
            NormalizedPhone = "0912345678",
            Password = "Hash"
        };

        await repo.AddAsync(user);
        await repo.SaveChangesAsync();

        // GetByIdAsync
        var byId = await repo.GetByIdAsync("usr_01");
        Assert.NotNull(byId);
        Assert.Equal("johndoe", byId.UserName);

        // GetByUsernameAsync by UserName
        var byUser = await repo.GetByUsernameAsync("JohnDoe");
        Assert.NotNull(byUser);

        // GetByUsernameAsync by Email
        var byEmail = await repo.GetByUsernameAsync("john@example.com");
        Assert.NotNull(byEmail);

        // GetByUsernameAsync by Phone (with spaces/formatting)
        var byPhone = await repo.GetByUsernameAsync("0912.345.678");
        Assert.NotNull(byPhone);

        // FindAsync
        var foundList = await repo.FindAsync(u => u.Email.Contains("john"));
        Assert.Single(foundList);

        // GetAllAsync
        var all = await repo.GetAllAsync();
        Assert.Single(all);

        // Update
        user.FullName = "John Doe";
        repo.Update(user);
        await repo.SaveChangesAsync();
        var updated = await repo.GetByIdAsync("usr_01");
        Assert.Equal("John Doe", updated!.FullName);

        // Delete
        repo.Delete(user);
        await repo.SaveChangesAsync();
        var deleted = await repo.GetByIdAsync("usr_01");
        Assert.Null(deleted);
    }

    [Fact]
    public async Task UserRepository_ExistsByPhoneOrEmailAsync_Should_Handle_All_Branches()
    {
        using var context = CreateDbContext();
        var repo = new UserRepository(context);

        var user = new User
        {
            Id = "usr_02",
            UserName = "0987654321",
            Email = "alice@test.com",
            NormalizedEmail = "alice@test.com",
            Phone = "0987-654-321",
            NormalizedPhone = "0987654321",
            Password = "Hash"
        };
        await repo.AddAsync(user);
        await repo.SaveChangesAsync();

        // Match by phone with 84 prefix
        bool exists84 = await repo.ExistsByPhoneOrEmailAsync("84987654321", null);
        Assert.True(exists84);

        // Match by normalized phone
        bool existsPhone = await repo.ExistsByPhoneOrEmailAsync("0987654321", null);
        Assert.True(existsPhone);

        // Match by email
        bool existsEmail = await repo.ExistsByPhoneOrEmailAsync("0111111111", "alice@test.com");
        Assert.True(existsEmail);

        // Neither exists
        bool notFound = await repo.ExistsByPhoneOrEmailAsync("0333333333", "nobody@test.com");
        Assert.False(notFound);

        // Empty/whitespace email check
        bool emptyEmail = await repo.ExistsByPhoneOrEmailAsync("0333333333", "   ");
        Assert.False(emptyEmail);
    }

    [Fact]
    public async Task UserRepository_RegisterStoreOwnerAsync_Should_Persist_Entities_With_And_Without_AuditLog()
    {
        using var context = CreateDbContext();
        var repo = new UserRepository(context);

        var tenant1 = new Tenant { Id = "tenant_01", Name = "Store 1", Code = "ST1" };
        var branch1 = new Branch { Id = Guid.NewGuid(), TenantId = "tenant_01", Name = "Main Branch", BranchCode = "BR01" };
        var owner1 = new User { Id = "usr_owner_01", UserName = "owner1", Password = "Hash" };

        // Without audit log
        var res1 = await repo.RegisterStoreOwnerAsync(owner1, tenant1, branch1);
        Assert.NotNull(res1);
        Assert.Equal("usr_owner_01", res1.Id);

        var tenant2 = new Tenant { Id = "tenant_02", Name = "Store 2", Code = "ST2" };
        var branch2 = new Branch { Id = Guid.NewGuid(), TenantId = "tenant_02", Name = "Branch 2", BranchCode = "BR02" };
        var owner2 = new User { Id = "usr_owner_02", UserName = "owner2", Password = "Hash" };
        var audit = new AuditLog { Id = Guid.NewGuid(), ActionType = "REGISTER", TargetEntity = "User" };

        // With audit log
        var res2 = await repo.RegisterStoreOwnerAsync(owner2, tenant2, branch2, audit);
        Assert.NotNull(res2);
        Assert.Equal("usr_owner_02", res2.Id);
    }

    [Fact]
    public async Task RegisterStoreOwner_WithExistingSession_UsesNewTenantForBranchAndCatalog()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var session = new TenantContext();
        session.SetContext("existing-store", Guid.NewGuid(), null);
        session.SetRole("STORE_OWNER");
        using var context = new AppDbContext(options, session);
        context.MasterProducts.Add(new MasterProduct { Name = "Gạo", BaseUnit = "Kg", IsActive = true });
        await context.SaveChangesAsync();

        var tenant = new Tenant { Id = "new-store", Name = "Cửa hàng mới", Code = "NEW" };
        var branch = new Branch { TenantId = tenant.Id, BranchCode = "BR-01", Name = "Chi nhánh chính", IsHeadquarter = true, IsActive = true };
        var owner = new User { Id = "new-owner", UserName = "new-owner" };
        await new UserRepository(context).RegisterStoreOwnerAsync(owner, tenant, branch);
        context.ChangeTracker.Clear();

        Assert.Empty(await context.Branches.ToListAsync());
        var savedBranch = await context.Branches.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(tenant.Id, savedBranch.TenantId);
        Assert.True(savedBranch.IsHeadquarter);
        Assert.Equal(tenant.Id, (await context.TenantUsers.IgnoreQueryFilters().SingleAsync()).TenantId);
        Assert.Equal(tenant.Id, (await context.Products.IgnoreQueryFilters().SingleAsync()).TenantId);
        Assert.Equal(tenant.Id, (await context.ProductUnits.IgnoreQueryFilters().SingleAsync()).TenantId);
        Assert.Equal(tenant.Id, (await context.Categories.IgnoreQueryFilters().SingleAsync()).TenantId);
        var assignment = await context.BranchProducts.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(tenant.Id, assignment.TenantId);
        Assert.Equal(savedBranch.Id, assignment.BranchId);

        context.Branches.Add(new Branch { TenantId = tenant.Id, BranchCode = "BR-02", Name = "Chi nhánh khác" });
        await context.SaveChangesAsync();
        Assert.Equal("existing-store", (await context.Branches.SingleAsync()).TenantId);
    }

    [Fact]
    public async Task UserRepository_GetEmployeeBranchIdAsync_Should_Handle_Valid_And_Edge_Cases()
    {
        using var context = CreateDbContext();
        var repo = new UserRepository(context);

        var branchId = Guid.NewGuid();
        var branch = new Branch { Id = branchId, TenantId = "tenant_emp", BranchCode = "BR1", Name = "Main branch", IsActive = true };
        context.Branches.Add(branch);
        var emp = new Employee
        {
            Id = Guid.NewGuid(),
            UserId = "emp_user_01",
            TenantId = "tenant_emp",
            BranchId = branchId,
            FullName = "Employee One",
            EmployeeCode = "EMP01",
            Status = EmployeeStatus.Active,
            IsDeleted = false
        };
        await context.Employees.AddAsync(emp);
        await context.SaveChangesAsync();

        // Null / whitespace inputs
        var nullUser = await repo.GetEmployeeBranchIdAsync("", "tenant_emp");
        Assert.Null(nullUser);
        var nullTenant = await repo.GetEmployeeBranchIdAsync("emp_user_01", "");
        Assert.Null(nullTenant);

        // Not found
        var notFound = await repo.GetEmployeeBranchIdAsync("unknown", "tenant_emp");
        Assert.Null(notFound);

        // Found active employee
        var found = await repo.GetEmployeeBranchIdAsync("emp_user_01", "tenant_emp");
        Assert.Equal(branchId, found);

        branch.IsActive = false;
        await context.SaveChangesAsync();
        Assert.Null(await repo.GetEmployeeBranchIdAsync("emp_user_01", "tenant_emp"));

        // Deleted or inactive employee
        branch.IsActive = true;
        emp.Status = EmployeeStatus.Inactive;
        await context.SaveChangesAsync();
        var terminated = await repo.GetEmployeeBranchIdAsync("emp_user_01", "tenant_emp");
        Assert.Null(terminated);
    }
}
