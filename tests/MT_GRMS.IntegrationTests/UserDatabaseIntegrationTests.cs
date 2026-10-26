using Microsoft.EntityFrameworkCore;
using MT_GRMS.Domain.Entities.Identity;
using MT_GRMS.Infrastructure.Persistence;
using MT_GRMS.Infrastructure.Repositories;
using Xunit;

namespace MT_GRMS.IntegrationTests;

public class UserDatabaseIntegrationTests
{
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
