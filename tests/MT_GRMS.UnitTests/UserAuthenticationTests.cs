using System.Linq.Expressions;
using MT_GRMS.Application.Auth;
using MT_GRMS.Application.Auth.Dtos;
using MT_GRMS.Domain.Entities.Identity;
using Xunit;

namespace MT_GRMS.UnitTests;

public class UserAuthenticationTests
{
    private class FakeUserRepository : IUserRepository
    {
        private readonly List<User> _users = new();

        public Task AddAsync(User user, CancellationToken cancellationToken = default)
        {
            _users.Add(user);
            return Task.CompletedTask;
        }

        public void Delete(User user) => _users.Remove(user);

        public Task<IReadOnlyList<User>> FindAsync(Expression<Func<User, bool>> predicate, CancellationToken cancellationToken = default)
        {
            var compiled = predicate.Compile();
            IReadOnlyList<User> list = _users.Where(compiled).ToList();
            return Task.FromResult(list);
        }

        public Task<IReadOnlyList<User>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            IReadOnlyList<User> list = _users.ToList();
            return Task.FromResult(list);
        }

        public Task<User?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_users.FirstOrDefault(u => u.Id == id));
        }

        public Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_users.FirstOrDefault(u => u.Username == username));
        }

        public void Update(User user) { }
    }

    [Fact]
    public void UserEntity_Should_Have_StringUid_Username_And_Password()
    {
        // GIVEN
        var user = new User
        {
            Id = "usr_admin_01",
            Username = "admin",
            Password = "Admin@123"
        };

        // THEN
        Assert.Equal("usr_admin_01", user.Id);
        Assert.Equal("admin", user.Username);
        Assert.Equal("Admin@123", user.Password);
    }

    [Fact]
    public async Task FakeUserRepository_Should_Add_And_Get_By_Username_Successfully()
    {
        // GIVEN
        var repo = new FakeUserRepository();
        var user = new User
        {
            Id = "usr_super_admin_01",
            Username = "super_admin",
            Password = "SecurePassword@123"
        };

        // WHEN
        await repo.AddAsync(user);
        var retrievedByUsername = await repo.GetByUsernameAsync("super_admin");
        var retrievedById = await repo.GetByIdAsync("usr_super_admin_01");

        // THEN
        Assert.NotNull(retrievedByUsername);
        Assert.Equal("usr_super_admin_01", retrievedByUsername.Id);
        Assert.Equal("super_admin", retrievedByUsername.Username);
        Assert.Equal("SecurePassword@123", retrievedByUsername.Password);

        Assert.NotNull(retrievedById);
        Assert.Equal("super_admin", retrievedById.Username);
    }

    [Fact]
    public async Task AuthService_LoginAsync_Should_Succeed_With_Existing_User()
    {
        // GIVEN
        var repo = new FakeUserRepository();
        var seedUser = new User
        {
            Id = "usr_test_01",
            Username = "testuser",
            Password = "Password@123"
        };
        await repo.AddAsync(seedUser);

        var authService = new AuthService(repo);

        // WHEN
        var response = await authService.LoginAsync(new LoginRequest(
            UsernameOrEmail: "testuser",
            Password: "Password@123",
            TenantCode: "TENANT-01"
        ));

        // THEN
        Assert.NotNull(response);
        Assert.False(string.IsNullOrEmpty(response.AccessToken));
        Assert.Equal("usr_test_01", response.User.Id);
        Assert.Equal("testuser", response.User.Username);
    }
}
