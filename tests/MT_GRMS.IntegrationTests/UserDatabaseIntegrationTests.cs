using Microsoft.EntityFrameworkCore;
using MT_GRMS.Domain.Entities.Identity;
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
    public async Task UserRepository_With_AppDbContext_Should_Persist_User_With_StringUid()
    {
        // GIVEN
        using var context = CreateDbContext();
        var repository = new UserRepository(context);

        var newUser = new User
        {
            Id = "usr_super_admin_01",
            Username = "admin",
            Password = "Admin@123"
        };

        // WHEN
        await repository.AddAsync(newUser);
        await context.SaveChangesAsync();

        var foundUser = await repository.GetByUsernameAsync("admin");

        // THEN
        Assert.NotNull(foundUser);
        Assert.Equal("usr_super_admin_01", foundUser.Id);
        Assert.Equal("admin", foundUser.Username);
        Assert.Equal("Admin@123", foundUser.Password);
    }
}
