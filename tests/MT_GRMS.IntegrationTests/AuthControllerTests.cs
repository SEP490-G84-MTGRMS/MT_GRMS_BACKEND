using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using MT_GRMS.API.Controllers;
using MT_GRMS.Application.Auth;
using MT_GRMS.Application.Auth.Dtos;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Entities.Identity;
using System.Linq.Expressions;
using Xunit;

namespace MT_GRMS.IntegrationTests;

public class AuthControllerTests
{
    private class FakeUserRepository : IUserRepository
    {
        private readonly List<User> _users = new();

        public Task AddAsync(User user, CancellationToken cancellationToken = default)
        {
            _users.Add(user);
            return Task.CompletedTask;
        }

        public Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_users.FirstOrDefault(u => u.Username == username));
        }

        public Task<User?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult(_users.FirstOrDefault(u => u.Id == id));

        public Task<IReadOnlyList<User>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<User>>(_users.ToList());

        public Task<IReadOnlyList<User>> FindAsync(Expression<Func<User, bool>> predicate, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<User>>(_users.Where(predicate.Compile()).ToList());

        public void Update(User user) { }
        public void Delete(User user) => _users.Remove(user);
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> ExistsByPhoneOrEmailAsync(string phone, string? email, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_users.Any(u => u.Phone == phone || (!string.IsNullOrEmpty(email) && u.Email == email)));
        }

        public Task<User> RegisterStoreOwnerAsync(User user, MT_GRMS.Domain.Entities.SaaS.Tenant tenant, MT_GRMS.Domain.Entities.SaaS.Branch branch, CancellationToken cancellationToken = default)
        {
            _users.Add(user);
            return Task.FromResult(user);
        }
    }

    [Fact]
    public async Task AuthController_Login_Should_Return_BadRequest_400_When_Input_Empty()
    {
        // GIVEN
        var repo = new FakeUserRepository();
        var authService = new AuthService(repo);
        var controller = new AuthController(authService);

        // WHEN
        var result = await controller.Login(new LoginRequest("", "", null), CancellationToken.None);

        // THEN (HTTP 400 Bad Request)
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(400, badRequestResult.StatusCode);

        var apiResponse = Assert.IsType<ApiResponse<object>>(badRequestResult.Value);
        Assert.False(apiResponse.Success);
        Assert.Equal("BAD_REQUEST", apiResponse.ErrorCode);
    }

    [Fact]
    public async Task AuthController_Login_Should_Require_Otp_Without_Issuing_Session()
    {
        // GIVEN
        var repo = new FakeUserRepository();
        await repo.AddAsync(new User
        {
            Id = "usr_super_admin_01",
            Username = "admin",
            Password = "Admin@123"
        });
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Development"
        }).Build();
        var authService = new AuthService(repo, config);
        var controller = new AuthController(authService);

        // WHEN
        var result = await controller.Login(new LoginRequest("admin", "Admin@123", "TENANT-HCM-01"), CancellationToken.None);

        // THEN (HTTP 200 OK)
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(200, okResult.StatusCode);

        var apiResponse = Assert.IsType<ApiResponse<SendLoginOtpResponse>>(okResult.Value);
        Assert.True(apiResponse.Success);
        Assert.NotNull(apiResponse.Data);
        Assert.True(apiResponse.Data.RequiresOtp);
        Assert.NotEmpty(apiResponse.Data.TempToken);
    }

    [Fact]
    public async Task AuthController_Login_Should_Reject_When_Email_Unavailable_In_Production()
    {
        var repo = new FakeUserRepository();
        await repo.AddAsync(new User { Username = "admin", Password = "Admin@123" });
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Production"
        }).Build();
        var controller = new AuthController(new AuthService(repo, config));

        var result = await controller.Login(new LoginRequest("admin", "Admin@123", null), CancellationToken.None);

        var unavailable = Assert.IsType<ObjectResult>(result);
        Assert.Equal(503, unavailable.StatusCode);
        var response = Assert.IsType<ApiResponse<object>>(unavailable.Value);
        Assert.False(response.Success);
        Assert.Equal("EMAIL_UNAVAILABLE", response.ErrorCode);
    }

    [Fact]
    public async Task AuthController_Logout_Should_Return_Ok_200_With_Success_Message()
    {
        // GIVEN
        var repo = new FakeUserRepository();
        var authService = new AuthService(repo);
        var controller = new AuthController(authService);

        // ACT
        var result = await controller.Logout();

        // THEN (HTTP 200 OK)
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(200, okResult.StatusCode);

        var apiResponse = Assert.IsType<ApiResponse<object>>(okResult.Value);
        Assert.True(apiResponse.Success);
        Assert.Equal("Đăng xuất thành công.", apiResponse.Message);
    }

    [Fact]
    public async Task AuthController_Register_Should_Return_Ok_200_When_Valid()
    {
        // GIVEN
        var repo = new FakeUserRepository();
        var authService = new AuthService(repo);
        var controller = new AuthController(authService);
        var request = new RegisterTenantRequest("Nguyễn Văn A", "Cửa hàng A", "0987654321", "owner_a@gmail.com", "Password@123", true);

        // WHEN
        var result = await controller.Register(request, CancellationToken.None);

        // THEN
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(200, okResult.StatusCode);

        var apiResponse = Assert.IsType<ApiResponse<UserInfo>>(okResult.Value);
        Assert.True(apiResponse.Success);
        Assert.NotNull(apiResponse.Data);
        Assert.Equal("Nguyễn Văn A", apiResponse.Data.FullName);
        Assert.Equal("STORE_OWNER", apiResponse.Data.Role);
    }

    [Fact]
    public async Task AuthController_Refresh_Should_Return_Ok_200_When_Valid_RefreshToken()
    {
        // GIVEN
        var repo = new FakeUserRepository();
        var user = new User
        {
            Id = "usr_test_refresh",
            Username = "refresh_user",
            Password = "Password@123",
            RefreshToken = "valid_refresh_token_123",
            RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(5)
        };
        await repo.AddAsync(user);
        var authService = new AuthService(repo);
        var controller = new AuthController(authService);

        // WHEN
        var result = await controller.Refresh(new RefreshTokenRequest("valid_refresh_token_123"), CancellationToken.None);

        // THEN (HTTP 200 OK)
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(200, okResult.StatusCode);

        var apiResponse = Assert.IsType<ApiResponse<UserInfo>>(okResult.Value);
        Assert.True(apiResponse.Success);
        Assert.Equal("usr_test_refresh", apiResponse.Data.Id);

        // Verify Token Rotation
        Assert.NotEqual("valid_refresh_token_123", user.RefreshToken);
        Assert.StartsWith("mt_refresh_", user.RefreshToken);
    }

    [Fact]
    public async Task AuthController_Refresh_Should_Return_Unauthorized_401_When_Token_Expired()
    {
        // GIVEN
        var repo = new FakeUserRepository();
        var user = new User
        {
            Id = "usr_expired",
            Username = "expired_user",
            Password = "Password@123",
            RefreshToken = "expired_refresh_token",
            RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(-2) // Đã quá hạn
        };
        await repo.AddAsync(user);
        var authService = new AuthService(repo);
        var controller = new AuthController(authService);

        // WHEN
        var result = await controller.Refresh(new RefreshTokenRequest("expired_refresh_token"), CancellationToken.None);

        // THEN (HTTP 401 Unauthorized)
        var unauthorizedResult = Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal(401, unauthorizedResult.StatusCode);
    }

    [Fact]
    public async Task AuthController_Refresh_Should_Return_Unauthorized_401_When_Token_Missing()
    {
        // GIVEN
        var repo = new FakeUserRepository();
        var authService = new AuthService(repo);
        var controller = new AuthController(authService);

        // WHEN
        var result = await controller.Refresh(null, CancellationToken.None);

        // THEN (HTTP 401 Unauthorized)
        var unauthorizedResult = Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal(401, unauthorizedResult.StatusCode);
    }
}
