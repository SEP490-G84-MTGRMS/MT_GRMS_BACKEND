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
        Assert.StartsWith("mt_jwt_", response.AccessToken);
        Assert.StartsWith("mt_refresh_", response.RefreshToken);
        Assert.Equal("usr_test_01", response.User.Id);
        Assert.Equal("testuser", response.User.Username);
        Assert.Equal("ADMIN", response.User.Role);
        Assert.Equal("ADMIN", response.User.RoleName);
        Assert.Equal("TENANT-01", response.User.TenantId);
    }

    [Fact]
    public async Task AuthService_LoginAsync_Should_Throw_Unauthorized_When_Password_Mismatch()
    {
        // GIVEN
        var repo = new FakeUserRepository();
        await repo.AddAsync(new User
        {
            Id = "usr_admin_01",
            Username = "admin",
            Password = "CorrectPassword@123"
        });

        var authService = new AuthService(repo);

        // WHEN & THEN
        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            authService.LoginAsync(new LoginRequest("admin", "WrongPassword@999", null))
        );

        Assert.Equal("Tài khoản hoặc mật khẩu không chính xác.", ex.Message);
    }

    [Fact]
    public async Task AuthService_LoginAsync_Should_Fallback_To_DefaultTenant_When_TenantCode_Null()
    {
        // GIVEN
        var repo = new FakeUserRepository();
        await repo.AddAsync(new User
        {
            Id = "usr_admin_01",
            Username = "admin",
            Password = "Password@123"
        });

        var authService = new AuthService(repo);

        // WHEN
        var response = await authService.LoginAsync(new LoginRequest("admin", "Password@123", TenantCode: null));

        // THEN
        Assert.Equal("DEFAULT", response.User.TenantId);
        Assert.Equal("usr_admin_01", response.User.Id);
    }

    [Fact]
    public async Task AuthService_LoginAsync_Should_Throw_Unauthorized_When_User_Not_Found()
    {
        // GIVEN
        var repo = new FakeUserRepository();
        var authService = new AuthService(repo);

        // WHEN & THEN (Không có user trong DB -> Phải ném UnauthorizedAccessException)
        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            authService.LoginAsync(new LoginRequest("non_existing_user", "AnyPassword@123", "TENANT-HN-01"))
        );

        Assert.Equal("Tài khoản hoặc mật khẩu không chính xác.", ex.Message);
    }

    [Fact]
    public async Task AuthService_LoginAsync_Should_Throw_ArgumentException_When_Input_Is_Empty()
    {
        // GIVEN
        var repo = new FakeUserRepository();
        var authService = new AuthService(repo);

        // WHEN & THEN
        await Assert.ThrowsAsync<ArgumentException>(() =>
            authService.LoginAsync(new LoginRequest("", "Pass123", null))
        );

        await Assert.ThrowsAsync<ArgumentException>(() =>
            authService.LoginAsync(new LoginRequest("admin", "", null))
        );
    }

    [Fact]
    public async Task AuthService_LoginAsync_Should_Set_Token_ExpiresAt_To_Two_Hours()
    {
        // GIVEN
        var repo = new FakeUserRepository();
        await repo.AddAsync(new User { Id = "usr_01", Username = "user01", Password = "123" });
        var authService = new AuthService(repo);

        // WHEN
        var before = DateTime.UtcNow;
        var response = await authService.LoginAsync(new LoginRequest("user01", "123", null));
        var after = DateTime.UtcNow;

        // THEN
        Assert.InRange(response.ExpiresAt, before.AddHours(2), after.AddHours(2).AddSeconds(5));
    }

    [Fact]
    public async Task AuthService_RefreshTokenAsync_Should_Return_New_Tokens()
    {
        // GIVEN
        var repo = new FakeUserRepository();
        var authService = new AuthService(repo);

        // WHEN
        var response = await authService.RefreshTokenAsync(new RefreshTokenRequest("mt_refresh_token_sample"));

        // THEN
        Assert.NotNull(response);
        Assert.StartsWith("mt_jwt_", response.AccessToken);
        Assert.StartsWith("mt_refresh_", response.RefreshToken);
        Assert.Equal("refreshed_user", response.User.Username);
        Assert.Equal("ADMIN", response.User.Role);
    }

    [Fact]
    public void UserInfo_Should_Expose_Role_Property_Matching_RoleName()
    {
        // GIVEN
        var userInfo = new UserInfo(
            Id: "usr_01",
            Username: "admin",
            FullName: "Admin User",
            Email: "admin@mtgrms.vn",
            RoleName: "MANAGER",
            BranchId: null,
            TenantId: "TENANT-HCM-01"
        );

        // THEN
        Assert.Equal("MANAGER", userInfo.RoleName);
        Assert.Equal("MANAGER", userInfo.Role);
    }
}
