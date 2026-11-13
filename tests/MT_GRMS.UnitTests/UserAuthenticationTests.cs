using System.Linq.Expressions;
using Microsoft.Extensions.Configuration;
using MT_GRMS.Application.Auth;
using MT_GRMS.Application.Auth.Dtos;
using MT_GRMS.Application.Common.Interfaces;
using MT_GRMS.Domain.Entities.Identity;
using Xunit;

namespace MT_GRMS.UnitTests;

public class UserAuthenticationTests
{
    private class FakeUserRepository : IUserRepository
    {
        private readonly List<User> _users = new();
        public MT_GRMS.Domain.Entities.SaaS.Tenant? LastRegisteredTenant { get; private set; }

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
            return Task.FromResult(_users.FirstOrDefault(u =>
                u.Username.Equals(username, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrEmpty(u.Email) && u.Email.Equals(username, StringComparison.OrdinalIgnoreCase))));
        }

        public void Update(User user) { }
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> ExistsByPhoneOrEmailAsync(string phone, string? email, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_users.Any(u => u.Phone == phone || (!string.IsNullOrEmpty(email) && u.Email == email)));
        }

        public Task<User> RegisterStoreOwnerAsync(User user, MT_GRMS.Domain.Entities.SaaS.Tenant tenant, MT_GRMS.Domain.Entities.SaaS.Branch branch, CancellationToken cancellationToken = default)
        {
            LastRegisteredTenant = tenant;
            _users.Add(user);
            return Task.FromResult(user);
        }
    }

    [Fact]
    public void UserEntity_Should_Have_StringUid_Username_And_Password()
    {
        // GIVEN
        var user = new User
        {
            Id = "usr_admin_01",
            Username = "admin",
            Password = "Admin@123",
            IsAdmin = true
        };

        // THEN
        Assert.Equal("usr_admin_01", user.Id);
        Assert.Equal("admin", user.Username);
        Assert.Equal("Admin@123", user.Password);
        Assert.True(user.IsAdmin);
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
            Password = "SecurePassword@123",
            IsAdmin = true
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
            Password = "Password@123",
            IsTwoFactorEnabled = false,
            IsAdmin = true
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
        Assert.True(response.AccessToken.StartsWith("mt_jwt_") || response.AccessToken.StartsWith("eyJ"));
        Assert.StartsWith("mt_refresh_", response.RefreshToken);
        Assert.Equal("usr_test_01", response.User.Id);
        Assert.Equal("testuser", response.User.Username);
        Assert.Equal("ADMIN", response.User.Role);
        Assert.Equal("ADMIN", response.User.RoleName);
        Assert.True(response.User.IsAdmin);
        Assert.Equal("TENANT-01", response.User.TenantId);
    }

    [Fact]
    public async Task AuthService_LoginAsync_NonAdminUser_Should_Have_StoreOwner_Role()
    {
        // GIVEN
        var repo = new FakeUserRepository();
        var seedUser = new User
        {
            Id = "usr_owner_01",
            Username = "shopowner",
            Password = "Password@123",
            IsTwoFactorEnabled = false,
            IsAdmin = false
        };
        await repo.AddAsync(seedUser);
        var authService = new AuthService(repo);

        // WHEN
        var response = await authService.LoginAsync(new LoginRequest("shopowner", "Password@123", "TENANT-01"));

        // THEN
        Assert.Equal("STORE_OWNER", response.User.Role);
        Assert.False(response.User.IsAdmin);
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
            Password = "Password@123",
            IsTwoFactorEnabled = false
        });

        var authService = new AuthService(repo);

        // WHEN
        var response = await authService.LoginAsync(new LoginRequest("admin", "Password@123", TenantCode: null));

        // THEN
        Assert.Equal("tenant_mt_mart_01", response.User.TenantId);
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
        await repo.AddAsync(new User { Id = "usr_01", Username = "user01", Password = "123", IsAdmin = true, IsTwoFactorEnabled = false });
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
        await repo.AddAsync(new User
        {
            Id = "usr_01",
            Username = "user01",
            Password = "123",
            IsAdmin = true,
            RefreshToken = "mt_refresh_token_sample",
            RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(7)
        });
        var authService = new AuthService(repo);

        // WHEN
        var response = await authService.RefreshTokenAsync(new RefreshTokenRequest("mt_refresh_token_sample"));

        // THEN
        Assert.NotNull(response);
        Assert.True(response.AccessToken.StartsWith("mt_jwt_") || response.AccessToken.StartsWith("eyJ"));
        Assert.StartsWith("mt_refresh_", response.RefreshToken);
        Assert.Equal("user01", response.User.Username);
        Assert.Equal("ADMIN", response.User.Role);
        Assert.True(response.User.IsAdmin);
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
            TenantId: "TENANT-HCM-01",
            IsAdmin: false
        );

        // THEN
        Assert.Equal("MANAGER", userInfo.RoleName);
        Assert.Equal("MANAGER", userInfo.Role);
        Assert.False(userInfo.IsAdmin);
    }

    [Fact]
    public void PasswordHasher_Should_Hash_And_Verify_Successfully()
    {
        var rawPassword = "SecurePassword@123";
        var hash = PasswordHasher.Hash(rawPassword);

        Assert.NotNull(hash);
        Assert.NotEqual(rawPassword, hash);
        Assert.True(PasswordHasher.Verify(hash, rawPassword, out var rehashNeeded));
        Assert.False(rehashNeeded);
        Assert.False(PasswordHasher.Verify(hash, "WrongPassword", out _));
    }

    [Fact]
    public void PasswordHasher_Should_Support_Legacy_Plaintext_And_Flag_Rehash()
    {
        var legacyPassword = "Admin@123";
        Assert.True(PasswordHasher.Verify(legacyPassword, legacyPassword, out var rehashNeeded));
        Assert.True(rehashNeeded);
        Assert.False(PasswordHasher.Verify(legacyPassword, "WrongPassword", out _));
    }

    [Fact]
    public async Task AuthService_LoginAsync_With_Hashed_Password_Should_Succeed()
    {
        var repo = new FakeUserRepository();
        var hashedPass = PasswordHasher.Hash("Secret@999");
        await repo.AddAsync(new User { Id = "usr_hash", Username = "user_hash", PasswordHash = hashedPass, IsTwoFactorEnabled = false });

        var authService = new AuthService(repo);
        var res = await authService.LoginAsync(new LoginRequest("user_hash", "Secret@999", null));

        Assert.NotNull(res);
        Assert.Equal("user_hash", res.User.Username);
    }

    private class FakeEmailService : IEmailService
    {
        public string? LastToEmail { get; private set; }
        public string? LastOtp { get; private set; }

        public Task SendPasswordResetOtpAsync(string toEmail, string fullName, string otpCode, CancellationToken cancellationToken = default)
        {
            LastToEmail = toEmail;
            LastOtp = otpCode;
            return Task.CompletedTask;
        }

        public Task<bool> SendLoginOtpAsync(string toEmail, string fullName, string otpCode, CancellationToken cancellationToken = default)
        {
            LastToEmail = toEmail;
            LastOtp = otpCode;
            return Task.FromResult(false);
        }

        public Task<bool> SendRegistrationOtpAsync(string toEmail, string fullName, string storeName, string otpCode, CancellationToken cancellationToken = default)
        {
            LastToEmail = toEmail;
            LastOtp = otpCode;
            return Task.FromResult(false);
        }
    }

    [Fact]
    public async Task AuthService_ForgotPasswordAsync_Should_Generate_Otp_And_Call_EmailService()
    {
        var repo = new FakeUserRepository();
        var emailService = new FakeEmailService();
        var user = new User { Id = "usr_forgot", Username = "user_forgot", Email = "owner@mtgrms.vn", FullName = "Nguyễn Chủ Tiệm" };
        await repo.AddAsync(user);

        var developmentConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Development"
        }).Build();
        var authService = new AuthService(repo, developmentConfig, emailService: emailService);
        await authService.ForgotPasswordAsync(new ForgotPasswordRequest("owner@mtgrms.vn"));

        Assert.NotNull(user.PasswordResetOtp);
        Assert.Equal(6, user.PasswordResetOtp.Length);
        Assert.NotNull(user.OtpExpiry);
        Assert.True(user.OtpExpiry > DateTime.UtcNow);
        Assert.Equal("owner@mtgrms.vn", emailService.LastToEmail);
        Assert.Equal(user.PasswordResetOtp, emailService.LastOtp);
    }

    [Fact]
    public async Task AuthService_ForgotPasswordAsync_With_Nonexistent_Email_Should_Throw_KeyNotFoundException()
    {
        var repo = new FakeUserRepository();
        var authService = new AuthService(repo);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            authService.ForgotPasswordAsync(new ForgotPasswordRequest("nonexistent@domain.com")));
    }

    [Fact]
    public async Task AuthService_VerifyOtpAsync_With_Correct_Otp_Should_Return_True()
    {
        var repo = new FakeUserRepository();
        var user = new User
        {
            Id = "usr_verify",
            Username = "user_verify",
            Email = "verify@mtgrms.vn",
            PasswordResetOtp = "849201",
            OtpExpiry = DateTime.UtcNow.AddMinutes(5)
        };
        await repo.AddAsync(user);

        var authService = new AuthService(repo);
        var result = await authService.VerifyOtpAsync(new VerifyOtpRequest("verify@mtgrms.vn", "849201"));

        Assert.True(result);

        // Sai mã OTP
        await Assert.ThrowsAsync<ArgumentException>(() =>
            authService.VerifyOtpAsync(new VerifyOtpRequest("verify@mtgrms.vn", "000000")));

        // OTP hết hạn
        user.OtpExpiry = DateTime.UtcNow.AddMinutes(-1);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            authService.VerifyOtpAsync(new VerifyOtpRequest("verify@mtgrms.vn", "849201")));
    }

    [Fact]
    public async Task AuthService_ResetPasswordAsync_Should_Update_PasswordHash_And_Clear_Otp()
    {
        var repo = new FakeUserRepository();
        var user = new User
        {
            Id = "usr_reset",
            Username = "user_reset",
            Email = "reset@mtgrms.vn",
            PasswordHash = "OldHash",
            PasswordResetOtp = "123456",
            OtpExpiry = DateTime.UtcNow.AddMinutes(5)
        };
        await repo.AddAsync(user);

        var authService = new AuthService(repo);
        await authService.ResetPasswordAsync(new ResetPasswordRequest("reset@mtgrms.vn", "123456", "NewSuperPassword@2026"));

        Assert.Null(user.PasswordResetOtp);
        Assert.Null(user.OtpExpiry);
        Assert.NotEqual("OldHash", user.PasswordHash);
        Assert.True(PasswordHasher.Verify(user.PasswordHash, "NewSuperPassword@2026", out _));
    }

    [Fact]
    public async Task AuthService_LoginAsync_InactiveUser_ShouldThrowUnauthorizedAccessException()
    {
        var repo = new FakeUserRepository();
        var user = new User
        {
            Id = "usr_locked",
            Username = "locked_staff",
            Email = "locked@mtgrms.vn",
            PasswordHash = PasswordHasher.Hash("Staff@123456"),
            IsActive = false
        };
        await repo.AddAsync(user);

        var authService = new AuthService(repo);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            authService.LoginAsync(new LoginRequest("locked_staff", "Staff@123456", null)));
    }

    [Fact]
    public async Task AuthService_RefreshTokenAsync_InactiveUser_ShouldThrowUnauthorizedAccessException()
    {
        var repo = new FakeUserRepository();
        var user = new User
        {
            Id = "usr_locked_refresh",
            Username = "locked_refresh",
            Email = "locked_refresh@mtgrms.vn",
            PasswordHash = "Hash",
            IsActive = false,
            RefreshToken = "mt_refresh_locked",
            RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(5)
        };
        await repo.AddAsync(user);

        var authService = new AuthService(repo);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            authService.RefreshTokenAsync(new RefreshTokenRequest("mt_refresh_locked")));
    }
}
