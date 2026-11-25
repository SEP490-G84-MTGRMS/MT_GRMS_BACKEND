using System.Linq.Expressions;
using Microsoft.Extensions.Configuration;
using Moq;
using MT_GRMS.Application.Auth;
using MT_GRMS.Application.Auth.Dtos;
using MT_GRMS.Application.Common.Interfaces;
using MT_GRMS.Application.HR.Employees;
using MT_GRMS.Domain.Entities.HR;
using MT_GRMS.Domain.Entities.Identity;
using MT_GRMS.Domain.Enums;
using Xunit;

namespace MT_GRMS.UnitTests;

public class UserAuthenticationTests
{
    private const string TestJwtKey = "MT_GRMS_UNIT_TEST_JWT_SIGNING_SECRET_2026_0123456789";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("UNKNOWN")]
    public void UnknownRole_IsRejected(string? role) =>
        Assert.Throws<ArgumentException>(() => MT_GRMS.Domain.Enums.UserRoleNames.FromString(role));

    [Theory]
    [InlineData("BRANCH_MANAGER")]
    [InlineData("MANAGER")]
    [InlineData("CASHIER")]
    [InlineData("EMPLOYEE")]
    public void LegacyStaffRoles_AreNeverPromotedToOwner(string role) =>
        Assert.Equal(MT_GRMS.Domain.Enums.UserRole.Staff, LegacyRoleReader.ReadStoredRole(role));

    [Fact]
    public async Task DemoPassword_DoesNotBypassStoredPassword()
    {
        var repo = new FakeUserRepository();
        await repo.AddAsync(new User { Id = "admin-id", Username = "admin", Password = "DifferentPassword", IsAdmin = true });
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            CreateAuthService(repo).LoginAsync(new LoginRequest("admin", "password123", null)));
    }

    [Fact]
    public async Task LegacyBranchManager_LoginAndRefreshIssueOnlyStaffRole()
    {
        var repo = new FakeUserRepository();
        var user = new User { Id = "staff-id", Username = "staff", Password = "StrongPassword", IsTwoFactorEnabled = false };
        user.TenantUsers.Add(new TenantUser { TenantId = "tenant-a", UserId = user.Id, Role = "BRANCH_MANAGER" });
        await repo.AddAsync(user);
        var service = CreateAuthService(repo);
        var login = await service.LoginAsync(new LoginRequest("staff", "StrongPassword", "tenant-a"));
        var refresh = await service.RefreshTokenAsync(new RefreshTokenRequest(login.RefreshToken));
        foreach (var session in new[] { login, refresh })
        {
            Assert.Equal("STAFF", session.User.Role);
            var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(session.AccessToken);
            Assert.All(jwt.Claims.Where(c => c.Type == "role" || c.Type == System.Security.Claims.ClaimTypes.Role), c => Assert.Equal("STAFF", c.Value));
            Assert.Contains(jwt.Claims, c => c.Type == "tenant_id" && c.Value == "tenant-a");
            Assert.Contains(jwt.Claims, c => c.Type == "security_stamp" && c.Value == user.SecurityStamp);
        }
    }

    [Fact]
    public async Task Login_WithMismatchedTenant_IsRejected()
    {
        var repo = new FakeUserRepository();
        var user = new User { Id = "owner-id", Username = "owner", Password = "StrongPassword", IsTwoFactorEnabled = false };
        user.TenantUsers.Add(new TenantUser { TenantId = "tenant-a", UserId = user.Id, Role = "STORE_OWNER" });
        await repo.AddAsync(user);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            CreateAuthService(repo).LoginAsync(new LoginRequest("owner", "StrongPassword", "tenant-b")));
    }

    [Fact]
    public async Task Refresh_RotatesHashedTokenAndRejectsReplays_PreservingSecurityStamp()
    {
        var repo = new FakeUserRepository();
        var user = new User { Id = "refresh-owner", Username = "refresh-owner", Password = "StrongPassword123!", IsTwoFactorEnabled = false };
        user.TenantUsers.Add(new TenantUser { TenantId = "tenant-a", UserId = user.Id, Role = "STORE_OWNER" });
        await repo.AddAsync(user);
        var service = CreateAuthService(repo);
        var login = await service.LoginAsync(new LoginRequest(user.Username, "StrongPassword123!", "tenant-a"));
        var stamp = user.SecurityStamp;
        var storedLoginToken = user.RefreshToken;
        Assert.StartsWith("sha256:", storedLoginToken);
        Assert.NotEqual(login.RefreshToken, storedLoginToken);
        Assert.False(string.IsNullOrWhiteSpace(stamp));

        var refreshed = await service.RefreshTokenAsync(new RefreshTokenRequest(login.RefreshToken));
        Assert.NotEqual(login.RefreshToken, refreshed.RefreshToken);
        Assert.NotEqual(storedLoginToken, user.RefreshToken);
        Assert.Equal("sha256:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(refreshed.RefreshToken))), user.RefreshToken);
        Assert.Equal(stamp, user.SecurityStamp);
        var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(refreshed.AccessToken);
        Assert.Contains(jwt.Claims, claim => claim.Type == "security_stamp" && claim.Value == stamp);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.RefreshTokenAsync(new RefreshTokenRequest(login.RefreshToken)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.RefreshTokenAsync(new RefreshTokenRequest(user.RefreshToken!)));
    }

    private static AuthService CreateAuthService(FakeUserRepository repository) =>
        new(repository, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = TestJwtKey,
            ["Jwt:Issuer"] = "MTGRMS_API",
            ["Jwt:Audience"] = "MTGRMS_CLIENT"
        }).Build());

    private class FakeUserRepository : IUserRepository
    {
        public Guid? AssignedBranch { get; set; } = Guid.NewGuid();
        public Task<Guid?> GetEmployeeBranchIdAsync(string userId, string tenantId, CancellationToken cancellationToken = default)
            => Task.FromResult(AssignedBranch);
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

        var authService = CreateAuthService(repo);

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
        Assert.Equal(string.Empty, response.User.TenantId);
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
        seedUser.TenantUsers.Add(new TenantUser { TenantId = "TENANT-01", UserId = seedUser.Id, Role = "STORE_OWNER" });
        await repo.AddAsync(seedUser);
        var authService = CreateAuthService(repo);

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

        var authService = CreateAuthService(repo);

        // WHEN & THEN
        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            authService.LoginAsync(new LoginRequest("admin", "WrongPassword@999", null))
        );

        Assert.Equal("Tài khoản hoặc mật khẩu không chính xác.", ex.Message);
    }

    [Fact]
    public async Task AuthService_LoginAsync_UsernameAdminWithoutAdminFlag_IsNotAdmin()
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

        var authService = CreateAuthService(repo);

        // WHEN
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => authService.LoginAsync(new LoginRequest("admin", "Password@123", TenantCode: null)));
    }

    [Fact]
    public async Task AuthService_LoginAsync_Should_Throw_Unauthorized_When_User_Not_Found()
    {
        // GIVEN
        var repo = new FakeUserRepository();
        var authService = CreateAuthService(repo);

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
        var authService = CreateAuthService(repo);

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
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:ExpiresMinutes"] = "120",
            ["Jwt:Key"] = TestJwtKey
        }).Build();
        var authService = new AuthService(repo, config, passwordHasher: new PasswordHasher());

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
        var authService = CreateAuthService(repo);

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
        var user = new User { Id = "usr_hash", Username = "user_hash", PasswordHash = hashedPass, IsTwoFactorEnabled = false };
        user.TenantUsers.Add(new TenantUser { TenantId = "tenant-a", UserId = user.Id, Role = "STAFF" });
        await repo.AddAsync(user);

        var authService = CreateAuthService(repo);
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

    [Fact]
    public async Task AuthService_SendLoginOtpAsync_TwoFactorDisabled_ReturnsSessionDirectly()
    {
        var repo = new FakeUserRepository();
        var user = new User
        {
            Id = "usr_otp_disabled",
            Username = "user_otp_disabled",
            Email = "otp_disabled@mtgrms.vn",
            PasswordHash = PasswordHasher.Hash("Pass@123"),
            IsActive = true,
            IsTwoFactorEnabled = false
        };
        user.TenantUsers.Add(new TenantUser { TenantId = "tenant-01", UserId = user.Id, Role = UserRoleNames.StoreOwner });
        await repo.AddAsync(user);

        var authService = CreateAuthService(repo);
        var resp = await authService.SendLoginOtpAsync(new LoginRequest("user_otp_disabled", "Pass@123", "tenant-01"));

        Assert.False(resp.RequiresOtp);
        Assert.NotNull(resp.Session);
        Assert.Equal("user_otp_disabled", resp.User?.Username);
        Assert.Equal("tenant-01", resp.User?.TenantId);
    }

    [Fact]
    public async Task AuthService_SendLoginOtpAsync_TwoFactorEnabled_WithEmailService_SendsOtpSuccessfully()
    {
        var repo = new FakeUserRepository();
        var user = new User
        {
            Id = "usr_otp_enabled",
            Username = "user_otp_enabled",
            Email = "otp_enabled@mtgrms.vn",
            PasswordHash = PasswordHasher.Hash("Pass@123"),
            IsActive = true,
            IsTwoFactorEnabled = true
        };
        await repo.AddAsync(user);

        var mockEmail = new Mock<IEmailService>();
        mockEmail.Setup(e => e.SendLoginOtpAsync("otp_enabled@mtgrms.vn", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var authService = new AuthService(repo, emailService: mockEmail.Object);
        var resp = await authService.SendLoginOtpAsync(new LoginRequest("user_otp_enabled", "Pass@123", "tenant-01"));

        Assert.True(resp.RequiresOtp);
        Assert.False(string.IsNullOrEmpty(resp.TempToken));
        Assert.True(resp.IsRealEmailSent);
    }

    [Fact]
    public async Task AuthService_SendLoginOtpAsync_TwoFactorEnabled_ProdEnvironment_NoEmailSent_ThrowsInvalidOperationException()
    {
        var repo = new FakeUserRepository();
        var user = new User
        {
            Id = "usr_otp_prod",
            Username = "user_otp_prod",
            Email = "otp_prod@mtgrms.vn",
            PasswordHash = PasswordHasher.Hash("Pass@123"),
            IsActive = true,
            IsTwoFactorEnabled = true
        };
        await repo.AddAsync(user);

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Production"
        }).Build();

        var mockEmail = new Mock<IEmailService>();
        mockEmail.Setup(e => e.SendLoginOtpAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var authService = new AuthService(repo, configuration: config, emailService: mockEmail.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            authService.SendLoginOtpAsync(new LoginRequest("user_otp_prod", "Pass@123", "tenant-01")));
    }

    [Fact]
    public async Task AuthService_VerifyLoginOtpAsync_Flow_ShouldWork()
    {
        var repo = new FakeUserRepository();
        var user = new User
        {
            Id = "usr_verify_flow",
            Username = "user_verify_flow",
            Email = "verify_flow@mtgrms.vn",
            PasswordHash = PasswordHasher.Hash("Pass@123"),
            IsActive = true,
            IsTwoFactorEnabled = true
        };
        user.TenantUsers.Add(new TenantUser { TenantId = "tenant-01", UserId = user.Id, Role = UserRoleNames.StoreOwner });
        await repo.AddAsync(user);

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["Jwt:Key"] = TestJwtKey
        }).Build();

        var authService = new AuthService(repo, configuration: config);
        var sendResp = await authService.SendLoginOtpAsync(new LoginRequest("user_verify_flow", "Pass@123", "tenant-01"));

        Assert.NotNull(sendResp.DevOtpCode);
        Assert.False(string.IsNullOrEmpty(sendResp.TempToken));

        // Invalid token
        await Assert.ThrowsAsync<ArgumentException>(() =>
            authService.VerifyLoginOtpAsync(new VerifyLoginOtpRequest("invalid_token", "123456")));

        // Wrong code
        await Assert.ThrowsAsync<ArgumentException>(() =>
            authService.VerifyLoginOtpAsync(new VerifyLoginOtpRequest(sendResp.TempToken, "000000")));

        // Correct code
        var loginResp = await authService.VerifyLoginOtpAsync(new VerifyLoginOtpRequest(sendResp.TempToken, sendResp.DevOtpCode));
        Assert.NotNull(loginResp);
        Assert.Equal("user_verify_flow", loginResp.User.Username);
        Assert.Equal("tenant-01", loginResp.User.TenantId);
    }

    [Fact]
    public async Task AuthService_TwoFactorChallenge_And_Confirm_Lifecycle_ShouldWork()
    {
        var repo = new FakeUserRepository();
        var user = new User
        {
            Id = "usr_2fa_chal",
            Username = "user_2fa_chal",
            Email = "2fa_chal@mtgrms.vn",
            PasswordHash = PasswordHasher.Hash("Pass@123"),
            IsActive = true,
            IsEmailVerified = true,
            IsTwoFactorEnabled = false
        };
        await repo.AddAsync(user);

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Development"
        }).Build();

        var authService = new AuthService(repo, configuration: config);

        // Status
        var status = await authService.GetTwoFactorStatusAsync("usr_2fa_chal");
        Assert.False(status.Enabled);

        // Challenge with wrong pass
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            authService.CreateTwoFactorChallengeAsync("usr_2fa_chal", new TwoFactorChallengeRequest(true, "WrongPass")));

        // Challenge success
        var chalResp = await authService.CreateTwoFactorChallengeAsync("usr_2fa_chal", new TwoFactorChallengeRequest(true, "Pass@123"));
        Assert.NotNull(chalResp.DevOtpCode);

        // Confirm wrong OTP removes token and throws ArgumentException
        await Assert.ThrowsAsync<ArgumentException>(() =>
            authService.ConfirmTwoFactorChallengeAsync("usr_2fa_chal", new TwoFactorConfirmRequest(chalResp.ChallengeToken, "999999")));

        // Create new challenge for valid confirm
        var chalResp2 = await authService.CreateTwoFactorChallengeAsync("usr_2fa_chal", new TwoFactorChallengeRequest(true, "Pass@123"));
        Assert.NotNull(chalResp2.DevOtpCode);

        // Confirm correct OTP
        var confirmResp = await authService.ConfirmTwoFactorChallengeAsync("usr_2fa_chal", new TwoFactorConfirmRequest(chalResp2.ChallengeToken, chalResp2.DevOtpCode));
        Assert.True(confirmResp.Enabled);
        Assert.True(user.IsTwoFactorEnabled);
    }

    [Fact]
    public async Task AuthService_RegistrationFlow_SendOtp_VerifyOtp_RegisterTenant_ShouldSucceed()
    {
        var repo = new FakeUserRepository();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["Jwt:Key"] = TestJwtKey
        }).Build();

        var authService = new AuthService(repo, configuration: config);

        var sendReq = new SendRegistrationOtpRequest("0908765432", "Green Mart", "Nguyen Van A", "nguyenvana@gmail.com", true);
        var sendResp = await authService.SendRegistrationOtpAsync(sendReq);

        Assert.True(sendResp.Success);
        Assert.NotNull(sendResp.ChallengeId);
        Assert.NotNull(sendResp.DevOtpCode);

        // Verify registration OTP
        var verifyRes = await authService.VerifyRegistrationOtpAsync(new VerifyRegistrationOtpRequest("nguyenvana@gmail.com", sendResp.DevOtpCode));
        Assert.True(verifyRes);

        // Register Tenant
        var regReq = new RegisterTenantRequest(
            FullName: "Nguyen Van A",
            StoreName: "Green Mart",
            Phone: "0908765432",
            Email: "nguyenvana@gmail.com",
            Password: "SecurePass@123",
            AgreeTerms: true,
            OtpCode: sendResp.DevOtpCode,
            StartTrial: true,
            ChallengeId: sendResp.ChallengeId
        );

        var regResp = await authService.RegisterTenantAsync(regReq);
        Assert.NotNull(regResp);
        Assert.Equal("Nguyen Van A", regResp.User.FullName);
        Assert.NotNull(repo.LastRegisteredTenant);
        Assert.Equal("Green Mart", repo.LastRegisteredTenant.Name);
    }

    [Fact]
    public async Task AuthService_GetCurrentUserAsync_And_RevokeRefreshToken()
    {
        var repo = new FakeUserRepository();
        var user = new User
        {
            Id = "usr_current_check",
            Username = "user_current_check",
            Email = "current_check@mtgrms.vn",
            PasswordHash = "Hash",
            IsActive = true,
            RefreshToken = "mt_refresh_to_revoke",
            RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(7)
        };
        user.TenantUsers.Add(new TenantUser { TenantId = "tenant-01", UserId = user.Id, Role = UserRoleNames.StoreOwner });
        await repo.AddAsync(user);

        var authService = new AuthService(repo);

        // Get current user null when not found
        Assert.Null(await authService.GetCurrentUserAsync(""));
        Assert.Null(await authService.GetCurrentUserAsync("non_existent"));

        // Get current user found
        var current = await authService.GetCurrentUserAsync("usr_current_check");
        Assert.NotNull(current);
        Assert.Equal("user_current_check", current.Username);
        Assert.Equal("tenant-01", current.TenantId);

        // Revoke refresh token
        await authService.RevokeRefreshTokenAsync("usr_current_check");
        Assert.Null(user.RefreshToken);
        Assert.Null(user.RefreshTokenExpiryTime);
    }

    [Fact]
    public async Task AuthService_EnsureAccountActive_LinkedEmployeeInactive_ThrowsUnauthorizedAccessException()
    {
        var repo = new FakeUserRepository();
        var user = new User
        {
            Id = "usr_emp_inactive",
            Username = "emp_inactive",
            Email = "emp_inactive@mtgrms.vn",
            PasswordHash = PasswordHasher.Hash("Pass@123"),
            IsActive = true
        };
        user.TenantUsers.Add(new TenantUser { TenantId = "tenant-01", UserId = user.Id, Role = UserRoleNames.Staff });
        await repo.AddAsync(user);

        var mockEmpRepo = new Mock<IEmployeeRepository>();
        mockEmpRepo.Setup(e => e.GetByUserIdAsync("usr_emp_inactive", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee
            {
                Id = Guid.NewGuid(),
                FullName = "Inactive Emp",
                Status = EmployeeStatus.Inactive
            });

        var authService = new AuthService(repo, employeeRepository: mockEmpRepo.Object);

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            authService.LoginAsync(new LoginRequest("emp_inactive", "Pass@123", "tenant-01")));

        Assert.Contains("nhân viên đã ngừng làm việc", ex.Message);
    }

    [Fact]
    public async Task ForgotPassword_VerifyOtp_ResetPassword_FullLifecycle()
    {
        var repo = new FakeUserRepository();
        var mockEmail = new Mock<IEmailService>();
        mockEmail.Setup(e => e.SendPasswordResetOtpAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var user = new User
        {
            Id = "usr_forgot_pwd",
            Username = "forgot_user",
            Email = "forgot@mtgrms.vn",
            FullName = "Forgot User",
            PasswordHash = PasswordHasher.Hash("OldPass@123"),
            IsActive = true
        };
        await repo.AddAsync(user);

        var authService = new AuthService(repo, emailService: mockEmail.Object);

        // 1. ForgotPassword empty email throws ArgumentException
        await Assert.ThrowsAsync<ArgumentException>(() =>
            authService.ForgotPasswordAsync(new ForgotPasswordRequest("")));

        // 2. ForgotPassword user not found throws KeyNotFoundException
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            authService.ForgotPasswordAsync(new ForgotPasswordRequest("unknown@mtgrms.vn")));

        // 3. Valid ForgotPassword sets OTP
        await authService.ForgotPasswordAsync(new ForgotPasswordRequest("forgot@mtgrms.vn"));
        Assert.NotNull(user.PasswordResetOtp);
        Assert.NotNull(user.OtpExpiry);
        var validOtp = user.PasswordResetOtp!;

        // 4. VerifyOtp wrong OTP throws ArgumentException
        await Assert.ThrowsAsync<ArgumentException>(() =>
            authService.VerifyOtpAsync(new VerifyOtpRequest("forgot@mtgrms.vn", "000000")));

        // 5. VerifyOtp valid OTP succeeds
        var verified = await authService.VerifyOtpAsync(new VerifyOtpRequest("forgot@mtgrms.vn", validOtp));
        Assert.True(verified);

        // 6. ResetPassword short password throws ArgumentException
        await Assert.ThrowsAsync<ArgumentException>(() =>
            authService.ResetPasswordAsync(new ResetPasswordRequest("forgot@mtgrms.vn", validOtp, "123")));

        // 7. ResetPassword valid succeeds and clears OTP
        await authService.ResetPasswordAsync(new ResetPasswordRequest("forgot@mtgrms.vn", validOtp, "BrandNewPass@123"));
        Assert.Null(user.PasswordResetOtp);
        Assert.Null(user.OtpExpiry);
        Assert.True(PasswordHasher.Verify(user.PasswordHash, "BrandNewPass@123", out _));

        // 8. Exceeding 5 wrong OTP attempts invalidates OTP, and weak 8-char password is rejected
        await authService.ForgotPasswordAsync(new ForgotPasswordRequest("forgot@mtgrms.vn"));
        var secondOtp = user.PasswordResetOtp!;
        await Assert.ThrowsAsync<ArgumentException>(() =>
            authService.ResetPasswordAsync(new ResetPasswordRequest("forgot@mtgrms.vn", secondOtp, "12345678")));

        for (var i = 0; i < 4; i++)
        {
            await Assert.ThrowsAsync<ArgumentException>(() =>
                authService.VerifyOtpAsync(new VerifyOtpRequest("forgot@mtgrms.vn", "999999")));
        }
        var exceeded = await Assert.ThrowsAsync<ArgumentException>(() =>
            authService.VerifyOtpAsync(new VerifyOtpRequest("forgot@mtgrms.vn", "999999")));
        Assert.Contains("OTP_ATTEMPTS_EXCEEDED", exceeded.Message);
        Assert.Null(user.PasswordResetOtp);
    }

    [Fact]
    public async Task GetTwoFactorStatus_And_CreateTwoFactorChallenge_And_Confirm()
    {
        var repo = new FakeUserRepository();
        var mockEmail = new Mock<IEmailService>();
        string? capturedOtp = null;
        mockEmail.Setup(e => e.SendTwoFactorSettingsOtpAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, CancellationToken>((to, name, otp, ct) => capturedOtp = otp)
            .ReturnsAsync(true);

        var user = new User
        {
            Id = "usr_2fa_test",
            Username = "user_2fa",
            Email = "user2fa@mtgrms.vn",
            FullName = "User 2FA",
            PasswordHash = PasswordHasher.Hash("Pass@123"),
            IsActive = true,
            IsEmailVerified = true,
            IsTwoFactorEnabled = false
        };
        await repo.AddAsync(user);

        var authService = new AuthService(repo, emailService: mockEmail.Object);

        // 1. Get status
        var status = await authService.GetTwoFactorStatusAsync(user.Id);
        Assert.False(status.Enabled);
        Assert.True(status.IsEmailVerified);

        // 2. Create challenge
        var challenge = await authService.CreateTwoFactorChallengeAsync(user.Id, new TwoFactorChallengeRequest(true, "Pass@123"));
        Assert.NotNull(challenge.ChallengeToken);
        Assert.NotNull(capturedOtp);

        // 3. Confirm with wrong OTP throws ArgumentException
        await Assert.ThrowsAsync<ArgumentException>(() =>
            authService.ConfirmTwoFactorChallengeAsync(user.Id, new TwoFactorConfirmRequest(challenge.ChallengeToken, "999999")));

        // 4. Create fresh challenge and confirm with valid OTP enables 2FA
        var challenge2 = await authService.CreateTwoFactorChallengeAsync(user.Id, new TwoFactorChallengeRequest(true, "Pass@123"));
        var confirmedStatus = await authService.ConfirmTwoFactorChallengeAsync(user.Id, new TwoFactorConfirmRequest(challenge2.ChallengeToken, capturedOtp!));
        Assert.True(confirmedStatus.Enabled);
        Assert.True(user.IsTwoFactorEnabled);
    }

    [Fact]
    public async Task AuthService_LoginAsync_WrongPassword_ThrowsUnauthorized()
    {
        var repo = new FakeUserRepository();
        await repo.AddAsync(new User
        {
            Id = "usr_wrong_pw",
            Username = "user_wrong_pw",
            PasswordHash = PasswordHasher.Hash("Correct@123"),
            IsActive = true
        });

        var authService = new AuthService(repo);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            authService.LoginAsync(new LoginRequest("user_wrong_pw", "WrongPassword!", null)));
    }

    [Fact]
    public async Task AuthService_RefreshTokenAsync_EmptyToken_ThrowsArgumentException()
    {
        var repo = new FakeUserRepository();
        var authService = new AuthService(repo);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            authService.RefreshTokenAsync(new RefreshTokenRequest("")));
    }

    [Fact]
    public async Task AuthService_ConfirmTwoFactorChallengeAsync_InvalidToken_ThrowsUnauthorized()
    {
        var repo = new FakeUserRepository();
        var authService = new AuthService(repo);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            authService.ConfirmTwoFactorChallengeAsync("usr_id", new TwoFactorConfirmRequest("invalid-token", "123456")));
    }

    [Fact]
    public async Task AuthService_VerifyLoginOtpAsync_InvalidToken_ThrowsArgumentException()
    {
        var repo = new FakeUserRepository();
        var authService = new AuthService(repo);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            authService.VerifyLoginOtpAsync(new VerifyLoginOtpRequest("invalid-temp-token", "123456")));
    }

    [Fact]
    public async Task AuthService_RegisterTenantAsync_InvalidRegistrationOtp_ThrowsArgumentException()
    {
        var repo = new FakeUserRepository();
        var authService = new AuthService(repo);

        var req = new RegisterTenantRequest(
            FullName: "Owner",
            StoreName: "MT Mart",
            Phone: "0987654321",
            Email: "owner@test.com",
            Password: "Password@123",
            ChallengeId: Guid.NewGuid().ToString(),
            OtpCode: "123456"
        );

        await Assert.ThrowsAsync<ArgumentException>(() =>
            authService.RegisterTenantAsync(req));
    }

    [Fact]
    public async Task AuthService_VerifyOtpAsync_ValidationErrors_ThrowsAppropriateExceptions()
    {
        var repo = new FakeUserRepository();
        var authService = new AuthService(repo);

        // 1. Empty Email / OtpCode
        await Assert.ThrowsAsync<ArgumentException>(() =>
            authService.VerifyOtpAsync(new VerifyOtpRequest("", "123456")));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            authService.VerifyOtpAsync(new VerifyOtpRequest("test@test.com", "")));

        // 2. User not found
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            authService.VerifyOtpAsync(new VerifyOtpRequest("unknown@test.com", "123456")));

        // 3. User without OTP requested
        var userNoOtp = new User { Id = "u1", Username = "user1", Email = "user1@test.com", PasswordResetOtp = null };
        await repo.AddAsync(userNoOtp);
        var exNoOtp = await Assert.ThrowsAsync<ArgumentException>(() =>
            authService.VerifyOtpAsync(new VerifyOtpRequest("user1@test.com", "123456")));
        Assert.Contains("chưa được yêu cầu", exNoOtp.Message);

        // 4. Expired OTP
        var userExpired = new User { Id = "u2", Username = "user2", Email = "user2@test.com", PasswordResetOtp = "123456", OtpExpiry = DateTime.UtcNow.AddMinutes(-5) };
        await repo.AddAsync(userExpired);
        var exExpired = await Assert.ThrowsAsync<ArgumentException>(() =>
            authService.VerifyOtpAsync(new VerifyOtpRequest("user2@test.com", "123456")));
        Assert.Contains("hết hạn", exExpired.Message);

        // 5. Wrong OTP
        var userValid = new User { Id = "u3", Username = "user3", Email = "user3@test.com", PasswordResetOtp = "123456", OtpExpiry = DateTime.UtcNow.AddMinutes(5) };
        await repo.AddAsync(userValid);
        var exWrong = await Assert.ThrowsAsync<ArgumentException>(() =>
            authService.VerifyOtpAsync(new VerifyOtpRequest("user3@test.com", "654321")));
        Assert.Contains("không chính xác", exWrong.Message);
    }

    [Fact]
    public async Task AuthService_ResetPasswordAsync_ValidationErrors_ThrowsAppropriateExceptions()
    {
        var repo = new FakeUserRepository();
        var authService = new AuthService(repo);

        // 1. Empty fields
        await Assert.ThrowsAsync<ArgumentException>(() =>
            authService.ResetPasswordAsync(new ResetPasswordRequest("", "123456", "NewPassword@123")));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            authService.ResetPasswordAsync(new ResetPasswordRequest("test@test.com", "", "NewPassword@123")));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            authService.ResetPasswordAsync(new ResetPasswordRequest("test@test.com", "123456", "")));

        // 2. Short password (< 8 chars)
        var exShort = await Assert.ThrowsAsync<ArgumentException>(() =>
            authService.ResetPasswordAsync(new ResetPasswordRequest("test@test.com", "123456", "12345")));
        Assert.Contains("tối thiểu 8 ký tự", exShort.Message);
    }

    [Fact]
    public async Task AuthService_SendRegistrationOtpAsync_ValidationErrors_ThrowsAppropriateExceptions()
    {
        var repo = new FakeUserRepository();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Development"
        }).Build();
        var authService = new AuthService(repo, configuration: config);

        // 1. Invalid email format
        var exEmail = await Assert.ThrowsAsync<ArgumentException>(() =>
            authService.SendRegistrationOtpAsync(new SendRegistrationOtpRequest(
                Phone: "0901234567",
                StoreName: "Test Store",
                FullName: "Owner",
                Email: "invalid-email-address",
                StartTrial: true)));
        Assert.Contains("email", exEmail.Message, StringComparison.OrdinalIgnoreCase);

        // 2. Duplicate phone or email
        await repo.AddAsync(new User { Id = "u_dup", Username = "dup_user", Phone = "0901234567", Email = "dup@test.com" });
        var exDup = await Assert.ThrowsAsync<ArgumentException>(() =>
            authService.SendRegistrationOtpAsync(new SendRegistrationOtpRequest(
                Phone: "0901234567",
                StoreName: "New Store",
                FullName: "New Owner",
                Email: "other@test.com",
                StartTrial: true)));
        Assert.Contains("đã được đăng ký", exDup.Message);
    }

    [Fact]
    public async Task AuthService_RegisterTenantAsync_MismatchErrors_ThrowsAppropriateExceptions()
    {
        var repo = new FakeUserRepository();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Development"
        }).Build();
        var authService = new AuthService(repo, configuration: config);

        // Send valid registration OTP
        var sendReq = new SendRegistrationOtpRequest("0908888888", "Orig Store", "Owner Name", "orig@test.com", true);
        var sendResp = await authService.SendRegistrationOtpAsync(sendReq);

        // 1. Email mismatch
        var reqEmailMismatch = new RegisterTenantRequest(
            FullName: "Owner Name",
            StoreName: "Orig Store",
            Phone: "0908888888",
            Email: "different@test.com",
            Password: "Password@123",
            AgreeTerms: true,
            ChallengeId: sendResp.ChallengeId,
            OtpCode: sendResp.DevOtpCode,
            StartTrial: true
        );
        var exEmail = await Assert.ThrowsAsync<ArgumentException>(() => authService.RegisterTenantAsync(reqEmailMismatch));
        Assert.Contains("OTP_EMAIL_MISMATCH", exEmail.Message);

        // 2. Intent mismatch
        var reqIntentMismatch = new RegisterTenantRequest(
            FullName: "Owner Name",
            StoreName: "Orig Store",
            Phone: "0908888888",
            Email: "orig@test.com",
            Password: "Password@123",
            AgreeTerms: true,
            ChallengeId: sendResp.ChallengeId,
            OtpCode: sendResp.DevOtpCode,
            StartTrial: false
        );
        var exIntent = await Assert.ThrowsAsync<ArgumentException>(() => authService.RegisterTenantAsync(reqIntentMismatch));
        Assert.Contains("OTP_INTENT_MISMATCH", exIntent.Message);

        // 3. Fingerprint mismatch (e.g. StoreName changed)
        var reqFingerprintMismatch = new RegisterTenantRequest(
            FullName: "Owner Name",
            StoreName: "Changed Store Name",
            Phone: "0908888888",
            Email: "orig@test.com",
            Password: "Password@123",
            AgreeTerms: true,
            ChallengeId: sendResp.ChallengeId,
            OtpCode: sendResp.DevOtpCode,
            StartTrial: true
        );
        var exFingerprint = await Assert.ThrowsAsync<ArgumentException>(() => authService.RegisterTenantAsync(reqFingerprintMismatch));
        Assert.Contains("OTP_FINGERPRINT_MISMATCH", exFingerprint.Message);
    }

    [Fact]
    public async Task AuthService_VerifyRegistrationOtpAsync_WrongOtpAndAttemptsExceeded()
    {
        var repo = new FakeUserRepository();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Development"
        }).Build();
        var authService = new AuthService(repo, configuration: config);

        var sendReq = new SendRegistrationOtpRequest("0907777777", "Test Store", "Owner", "attempts@test.com", true);
        var sendResp = await authService.SendRegistrationOtpAsync(sendReq);

        // Wrong OTP code 1 to 4 -> OTP_INVALID
        for (int i = 0; i < 4; i++)
        {
            var exInvalid = await Assert.ThrowsAsync<ArgumentException>(() =>
                authService.VerifyRegistrationOtpAsync(new VerifyRegistrationOtpRequest("attempts@test.com", "000000")));
            Assert.Contains("OTP_INVALID", exInvalid.Message);
        }

        // 5th wrong attempt -> OTP_ATTEMPTS_EXCEEDED
        var exExceeded = await Assert.ThrowsAsync<ArgumentException>(() =>
            authService.VerifyRegistrationOtpAsync(new VerifyRegistrationOtpRequest("attempts@test.com", "000000")));
        Assert.Contains("OTP_ATTEMPTS_EXCEEDED", exExceeded.Message);
    }

    [Fact]
    public async Task ChangePasswordAsync_ValidRequest_UpdatesHashRotatesStampAndRevokesOldSession()
    {
        var repo = new FakeUserRepository();
        var user = new User
        {
            Id = "owner-change-pwd",
            Username = "0912345678",
            Email = "owner@mtgrms.vn",
            FullName = "Chủ Tiệm",
            Password = "OldPassword@123",
            IsTwoFactorEnabled = false
        };
        user.TenantUsers.Add(new TenantUser { TenantId = "tenant-01", UserId = user.Id, Role = "STORE_OWNER", IsActive = true });
        await repo.AddAsync(user);

        var service = CreateAuthService(repo);
        var initialSession = await service.LoginAsync(new LoginRequest(user.Username, "OldPassword@123", "tenant-01"));
        var initialStamp = user.SecurityStamp;

        var newSession = await service.ChangePasswordAsync(
            user.Id,
            new ChangePasswordRequest("OldPassword@123", "NewPassword@456"));

        Assert.NotEqual(initialStamp, user.SecurityStamp);
        Assert.NotEqual(initialSession.RefreshToken, newSession.RefreshToken);

        var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(newSession.AccessToken);
        Assert.Contains(jwt.Claims, c => c.Type == "security_stamp" && c.Value == user.SecurityStamp);

        // Old refresh token from another device is rejected
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.RefreshTokenAsync(new RefreshTokenRequest(initialSession.RefreshToken)));

        // Old password rejected, new password succeeds
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.LoginAsync(new LoginRequest(user.Username, "OldPassword@123", "tenant-01")));
        var relogin = await service.LoginAsync(new LoginRequest(user.Username, "NewPassword@456", "tenant-01"));
        Assert.Equal(user.Id, relogin.User.Id);
    }

    [Fact]
    public async Task ChangePasswordAsync_InvalidCurrentOrWeakOrSamePassword_ThrowsArgumentExceptionAndKeepsStamp()
    {
        var repo = new FakeUserRepository();
        var user = new User
        {
            Id = "owner-change-invalid",
            Username = "0987654321",
            Password = "OldPassword@123",
            IsTwoFactorEnabled = false
        };
        user.TenantUsers.Add(new TenantUser { TenantId = "tenant-01", UserId = user.Id, Role = "STORE_OWNER", IsActive = true });
        await repo.AddAsync(user);

        var service = CreateAuthService(repo);
        await service.LoginAsync(new LoginRequest(user.Username, "OldPassword@123", "tenant-01"));
        var originalStamp = user.SecurityStamp;

        var wrongCurrent = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.ChangePasswordAsync(user.Id, new ChangePasswordRequest("WrongPass@123", "NewPassword@456")));
        Assert.Contains("INVALID_CURRENT_PASSWORD", wrongCurrent.Message);
        Assert.Equal(originalStamp, user.SecurityStamp);

        var samePass = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.ChangePasswordAsync(user.Id, new ChangePasswordRequest("OldPassword@123", "OldPassword@123")));
        Assert.Contains("PASSWORD_SAME_AS_OLD", samePass.Message);
        Assert.Equal(originalStamp, user.SecurityStamp);

        var weakPass = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.ChangePasswordAsync(user.Id, new ChangePasswordRequest("OldPassword@123", "weakpass")));
        Assert.Contains("WEAK_PASSWORD", weakPass.Message);
        Assert.Equal(originalStamp, user.SecurityStamp);
    }

    [Fact]
    public async Task UpdateProfileAsync_UpdatesFullNameAndPhone_AndRejectsDuplicatePhone()
    {
        var repo = new FakeUserRepository();
        var user1 = new User
        {
            Id = "owner-profile-1",
            Username = "0912345678",
            FullName = "Chu Cua Hang Cu",
            Email = "owner1@mtgrms.vn",
            Phone = "0912345678",
            NormalizedPhone = "0912345678",
            Password = "OldPassword@123",
            IsActive = true
        };
        user1.TenantUsers.Add(new TenantUser { TenantId = "tenant-01", UserId = user1.Id, Role = "STORE_OWNER", IsActive = true });

        var user2 = new User
        {
            Id = "owner-profile-2",
            Username = "0988776655",
            FullName = "Chu Cua Hang 2",
            Email = "owner2@mtgrms.vn",
            Phone = "0988776655",
            NormalizedPhone = "0988776655",
            Password = "OldPassword@123",
            IsActive = true
        };
        user2.TenantUsers.Add(new TenantUser { TenantId = "tenant-02", UserId = user2.Id, Role = "STORE_OWNER", IsActive = true });

        await repo.AddAsync(user1);
        await repo.AddAsync(user2);

        var service = CreateAuthService(repo);
        var updated = await service.UpdateProfileAsync(user1.Id, new UpdateProfileRequest("  Nguyễn Văn Mới  ", "+84933445566"));

        Assert.Equal("Nguyễn Văn Mới", updated.FullName);
        Assert.Equal("0933445566", updated.Phone);
        Assert.Equal("0933445566", user1.NormalizedPhone);

        var dupEx = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UpdateProfileAsync(user1.Id, new UpdateProfileRequest("Nguyễn Văn Mới", "0988776655")));
        Assert.Contains("PHONE_EXISTS", dupEx.Message);
    }
}
