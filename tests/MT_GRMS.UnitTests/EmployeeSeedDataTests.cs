using Microsoft.EntityFrameworkCore;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Infrastructure.Persistence;
using Xunit;

namespace MT_GRMS.UnitTests;

public class EmployeeSeedDataTests
{
    private AppDbContext CreateInMemoryDbContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;

        return new AppDbContext(options);
    }

    [Fact]
    public async Task SeedAsync_WhenCalled_CreatesEmployeesWithCorrectAttributesAndAllowances()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        await using var context = CreateInMemoryDbContext(dbName);

        var tenantId = "tenant_test_hr";
        var branch = new Branch
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BranchCode = "BR-HCM-01",
            Name = "Chi nhánh Quận 1 Test",
            IsActive = true
        };
        await context.Branches.AddAsync(branch);
        await context.SaveChangesAsync();

        // Act
        await EmployeeSeedData.SeedAsync(context, tenantId);

        // Assert
        var employees = await context.Employees
            .IgnoreQueryFilters()
            .Include(e => e.Allowances)
            .Where(e => e.TenantId == tenantId)
            .ToListAsync();

        Assert.Equal(12, employees.Count);

        // Kiểm tra nhân viên NV001 (Cửa hàng trưởng)
        var manager = employees.FirstOrDefault(e => e.EmployeeCode == "NV001");
        Assert.NotNull(manager);
        Assert.Equal("Nguyễn Văn An", manager.FullName);
        Assert.Equal("Nguyễn Văn An", manager.FullName);
        Assert.Equal(16000000.00m, manager.BaseRate);
        Assert.Equal(SalaryType.Monthly, manager.SalaryType);
        Assert.Equal(EmployeeStatus.Active, manager.Status);
        Assert.Equal(3, manager.Allowances.Count);

        // Kiểm tra nhân viên NV009 (Lương theo giờ - Part-time)
        var partTime = employees.FirstOrDefault(e => e.EmployeeCode == "NV009");
        Assert.NotNull(partTime);
        Assert.Equal(SalaryType.Hourly, partTime.SalaryType);
        Assert.Equal(28000.00m, partTime.BaseRate);

        // Kiểm tra nhân viên NV010 (Lương theo ca - Thời vụ)
        var shiftWorker = employees.FirstOrDefault(e => e.EmployeeCode == "NV010");
        Assert.NotNull(shiftWorker);
        Assert.Equal(SalaryType.Shift, shiftWorker.SalaryType);
        Assert.Equal(250000.00m, shiftWorker.BaseRate);

        // Kiểm tra nhân viên NV012 (Đã thôi việc)
        var resigned = employees.FirstOrDefault(e => e.EmployeeCode == "NV012");
        Assert.NotNull(resigned);
        Assert.Equal(EmployeeStatus.Inactive, resigned.Status);
    }

    [Fact]
    public async Task SeedAsync_WhenCalledTwice_IsIdempotentAndDoesNotDuplicate()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        await using var context = CreateInMemoryDbContext(dbName);

        var tenantId = "tenant_test_idempotent";
        var branch = new Branch
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BranchCode = "BR-HCM-01",
            Name = "Chi nhánh Quận 1 Test",
            IsActive = true
        };
        await context.Branches.AddAsync(branch);
        await context.SaveChangesAsync();

        // Act - Chạy 2 lần liên tiếp
        await EmployeeSeedData.SeedAsync(context, tenantId);
        await EmployeeSeedData.SeedAsync(context, tenantId);

        // Assert
        var count = await context.Employees
            .IgnoreQueryFilters()
            .Where(e => e.TenantId == tenantId)
            .CountAsync();

        Assert.Equal(12, count);
    }
}

