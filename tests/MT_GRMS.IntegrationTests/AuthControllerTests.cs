using Microsoft.AspNetCore.Mvc;
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
    public async Task AuthController_Login_Should_Return_Ok_200_With_ApiResponse_When_Valid()
    {
        // GIVEN
        var repo = new FakeUserRepository();
        await repo.AddAsync(new User
        {
            Id = "usr_super_admin_01",
            Username = "admin",
            Password = "Admin@123"
        });
        var authService = new AuthService(repo);
        var controller = new AuthController(authService);

        // WHEN
        var result = await controller.Login(new LoginRequest("admin", "Admin@123", "TENANT-HCM-01"), CancellationToken.None);

        // THEN (HTTP 200 OK)
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(200, okResult.StatusCode);

        var apiResponse = Assert.IsType<ApiResponse<LoginResponse>>(okResult.Value);
        Assert.True(apiResponse.Success);
        Assert.NotNull(apiResponse.Data);
        Assert.Equal("usr_super_admin_01", apiResponse.Data.User.Id);
        Assert.Equal("admin", apiResponse.Data.User.Username);
        Assert.Equal("ADMIN", apiResponse.Data.User.Role);
        Assert.Equal("TENANT-HCM-01", apiResponse.Data.User.TenantId);
    }

    [Fact]
    public void AuthController_Logout_Should_Return_Ok_200_With_Success_Message()
    {
        // GIVEN
        var repo = new FakeUserRepository();
        var authService = new AuthService(repo);
        var controller = new AuthController(authService);

        // ACT
        var result = controller.Logout();

        // THEN (HTTP 200 OK)
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(200, okResult.StatusCode);

        var apiResponse = Assert.IsType<ApiResponse<string>>(okResult.Value);
        Assert.True(apiResponse.Success);
        Assert.Equal("Đăng xuất thành công.", apiResponse.Message);
    }
}
