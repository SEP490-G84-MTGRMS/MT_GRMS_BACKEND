using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using MT_GRMS.Api.Controllers;
using MT_GRMS.Application.Admin.Dtos;
using MT_GRMS.Application.Admin.Services;
using MT_GRMS.Application.Auth;
using MT_GRMS.Application.Auth.Dtos;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Common.Interfaces;
using MT_GRMS.Application.HR.Employees;
using MT_GRMS.Application.Transfers;
using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Constants;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Entities.HR;
using MT_GRMS.Domain.Entities.Identity;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Entities.Sales;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Domain.Exceptions;
using MT_GRMS.Infrastructure.Persistence;
using Xunit;

namespace MT_GRMS.UnitTests;

public class DeepBranchCoverageSuite3Tests
{
    #region Fakes & Helpers

    private class FakeTenantContext : ITenantContext
    {
        public string CurrentTenantId { get; set; } = "tenant-suite3";
        public Guid CurrentUserId { get; set; } = Guid.NewGuid();
        public string CurrentRole { get; set; } = "STORE_OWNER";
        public Guid? CurrentBranchId { get; set; } = Guid.NewGuid();
        public string? CustomRawUserId { get; set; } = null;
        public string CurrentRawUserId => CustomRawUserId ?? (CurrentUserId != Guid.Empty ? CurrentUserId.ToString() : string.Empty);

        public void SetContext(string tenantId, Guid userId, Guid? branchId)
        {
            CurrentTenantId = tenantId;
            CurrentUserId = userId;
            CurrentBranchId = branchId;
        }

        public void SetRole(string? role) => CurrentRole = role ?? string.Empty;
    }

    private class FakeUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
        public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private class FakeUserRepository : IUserRepository
    {
        public List<User> Users { get; } = new();

        public Task AddAsync(User user, CancellationToken cancellationToken = default)
        {
            Users.Add(user);
            return Task.CompletedTask;
        }

        public void Delete(User user) => Users.Remove(user);

        public Task<IReadOnlyList<User>> FindAsync(Expression<Func<User, bool>> predicate, CancellationToken cancellationToken = default)
        {
            var compiled = predicate.Compile();
            IReadOnlyList<User> list = Users.Where(compiled).ToList();
            return Task.FromResult(list);
        }

        public Task<IReadOnlyList<User>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            IReadOnlyList<User> list = Users.ToList();
            return Task.FromResult(list);
        }

        public Task<User?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Users.FirstOrDefault(u => u.Id == id));
        }

        public Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Users.FirstOrDefault(u =>
                u.Username.Equals(username, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrEmpty(u.Email) && u.Email.Equals(username, StringComparison.OrdinalIgnoreCase))));
        }

        public void Update(User user)
        {
            var idx = Users.FindIndex(u => u.Id == user.Id);
            if (idx >= 0) Users[idx] = user;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> ExistsByPhoneOrEmailAsync(string phone, string? email, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Users.Any(u => u.Phone == phone || (!string.IsNullOrEmpty(email) && u.Email == email)));
        }

        public Task<User> RegisterStoreOwnerAsync(User user, Tenant tenant, Branch branch, CancellationToken cancellationToken = default)
        {
            Users.Add(user);
            return Task.FromResult(user);
        }

        public Task<Guid?> GetEmployeeBranchIdAsync(string userId, string tenantId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<Guid?>(Guid.NewGuid());
        }
    }

    private class FakeRepository<T> : IRepository<T> where T : BaseTenantEntity
    {
        public List<T> Items { get; } = new();

        public Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(Items.FirstOrDefault(x => x.Id == id && !x.IsDeleted));

        public Task<IReadOnlyList<T>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<T>>(Items.Where(x => !x.IsDeleted).ToList());

        public Task<IReadOnlyList<T>> FindAsync(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<T>>(Items.AsQueryable().Where(predicate).Where(x => !x.IsDeleted).ToList());

        public Task AddAsync(T entity, CancellationToken cancellationToken = default)
        {
            if (entity.Id == Guid.Empty) entity.Id = Guid.NewGuid();
            if (!Items.Any(i => i.Id == entity.Id)) Items.Add(entity);
            return Task.CompletedTask;
        }

        public void Update(T entity)
        {
            var index = Items.FindIndex(x => x.Id == entity.Id);
            if (index >= 0) Items[index] = entity;
            else Items.Add(entity);
        }

        public void Delete(T entity)
        {
            entity.IsDeleted = true;
        }
    }

    #endregion

    // =========================================================================
    // SECTION 1: AuthService - Password Verification, Fallbacks & Demo Accounts
    // =========================================================================

    [Fact]
    public async Task AuthService_PasswordVerification_Fallbacks_And_DemoBranches()
    {
        var repo = new FakeUserRepository();
        var mockHasher = new PasswordHasher();
        var service = new AuthService(repo, passwordHasher: mockHasher);

        // 1. Null / empty PasswordHash -> UnauthorizedAccessException
        var userEmptyHash = new User { Id = "u_empty", Username = "user_empty", PasswordHash = "", IsActive = true };
        repo.Users.Add(userEmptyHash);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.LoginAsync(new LoginRequest("user_empty", "pass123", "tenant1")));

        // 2. Fallback 1: SHA256 hashed password with auto-rehash to PBKDF2
        var rawPasswordSha = "SecretSha123!";
        var shaHex = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawPasswordSha))).ToLowerInvariant();
        var userSha = new User { Id = "u_sha", Username = "user_sha", PasswordHash = shaHex, IsActive = true };
        repo.Users.Add(userSha);

        var loginShaRes = await service.LoginAsync(new LoginRequest("user_sha", rawPasswordSha, "tenant1"));
        Assert.NotNull(loginShaRes);
        Assert.NotEqual(shaHex, userSha.PasswordHash); // rehashed to PBKDF2

        // 3. Fallback 2: Plaintext password match with auto-rehash
        var rawPlain = "PlaintextSecret999";
        var userPlain = new User { Id = "u_plain", Username = "user_plain", PasswordHash = rawPlain, IsActive = true };
        repo.Users.Add(userPlain);

        var loginPlainRes = await service.LoginAsync(new LoginRequest("user_plain", rawPlain, "tenant1"));
        Assert.NotNull(loginPlainRes);
        Assert.NotEqual(rawPlain, userPlain.PasswordHash);

        // 4. Fallback 3: Demo passwords "password123" and "123456" for seeded accounts
        var adminUser = new User { Id = "u_admin", Username = "admin", PasswordHash = "legacy_demo_hash", IsAdmin = true, IsActive = true };
        var managerUser = new User { Id = "u_mgr", Username = "manager01", PasswordHash = "legacy_demo_hash", IsActive = true };
        var cashierUser = new User { Id = "u_csh", Username = "cashier01", PasswordHash = "legacy_demo_hash", IsActive = true };
        repo.Users.AddRange(new[] { adminUser, managerUser, cashierUser });

        var adminRes = await service.LoginAsync(new LoginRequest("admin", "password123", "tenant1"));
        Assert.NotNull(adminRes);

        var mgrRes = await service.LoginAsync(new LoginRequest("manager01", "123456", "tenant1"));
        Assert.NotNull(mgrRes);

        var cshRes = await service.LoginAsync(new LoginRequest("cashier01", "123456", "tenant1"));
        Assert.NotNull(cshRes);

        // 5. Wrong password -> UnauthorizedAccessException
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.LoginAsync(new LoginRequest("admin", "wrong_pass", "tenant1")));
    }

    [Fact]
    public async Task AuthService_AccountActive_And_TenantResolution_Branches()
    {
        var repo = new FakeUserRepository();
        var mockHasher = new PasswordHasher();
        var empRepoMock = new Mock<IEmployeeRepository>();
        var service = new AuthService(repo, passwordHasher: mockHasher, employeeRepository: empRepoMock.Object);

        // 1. Inactive user -> UnauthorizedAccessException
        var inactiveUser = new User
        {
            Id = "u_inact",
            Username = "user_inact",
            PasswordHash = mockHasher.HashPassword("Pass123!"),
            IsActive = false
        };
        repo.Users.Add(inactiveUser);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.LoginAsync(new LoginRequest("user_inact", "Pass123!", "tenant1")));

        // 2. Inactive tenantUser -> UnauthorizedAccessException
        var userWithInactiveTenant = new User
        {
            Id = "u_tenant_inact",
            Username = "user_tenant_inact",
            PasswordHash = mockHasher.HashPassword("Pass123!"),
            IsActive = true,
            TenantUsers = new List<TenantUser>
            {
                new() { TenantId = "t_inact", UserId = "u_tenant_inact", IsActive = false }
            }
        };
        repo.Users.Add(userWithInactiveTenant);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.LoginAsync(new LoginRequest("user_tenant_inact", "Pass123!", "t_inact")));

        // 3. Linked employee inactive -> UnauthorizedAccessException
        var userInactiveEmp = new User
        {
            Id = "u_emp_inact",
            Username = "user_emp_inact",
            PasswordHash = mockHasher.HashPassword("Pass123!"),
            IsActive = true
        };
        repo.Users.Add(userInactiveEmp);
        empRepoMock.Setup(e => e.GetByUserIdAsync("u_emp_inact", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Employee { Id = Guid.NewGuid(), Status = EmployeeStatus.Inactive });

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.LoginAsync(new LoginRequest("user_emp_inact", "Pass123!", "tenant1")));

        // 4. Tenant code aliases normalization ("default-tenant", "TENANT-HCM-01", "TENANT-DEFAULT", "DEFAULT") -> "tenant_mt_mart_01"
        var activeUser = new User
        {
            Id = "u_norm",
            Username = "user_norm",
            PasswordHash = mockHasher.HashPassword("Pass123!"),
            IsActive = true
        };
        repo.Users.Add(activeUser);

        foreach (var alias in new[] { "default-tenant", "TENANT-HCM-01", "TENANT-DEFAULT", "DEFAULT" })
        {
            var res = await service.LoginAsync(new LoginRequest("user_norm", "Pass123!", alias));
            Assert.Equal("tenant_mt_mart_01", res.User.TenantId);
        }
    }

    [Fact]
    public async Task AuthService_RefreshToken_And_GetCurrentUser_Branches()
    {
        var repo = new FakeUserRepository();
        var mockHasher = new PasswordHasher();
        var service = new AuthService(repo, passwordHasher: mockHasher);

        // 1. RefreshToken whitespace -> ArgumentException
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.RefreshTokenAsync(new RefreshTokenRequest("   ")));

        // 2. RefreshToken > 256 chars -> UnauthorizedAccessException
        var hugeToken = new string('A', 257);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.RefreshTokenAsync(new RefreshTokenRequest(hugeToken)));

        // 3. Legacy unhashed token match (!StartsWith("sha256:"))
        var legacyRawToken = "legacy_token_123456";
        var userLegacy = new User
        {
            Id = "u_legacy_tok",
            Username = "user_legacy",
            RefreshToken = legacyRawToken,
            RefreshTokenExpiryTime = DateTime.UtcNow.AddHours(2),
            IsActive = true
        };
        repo.Users.Add(userLegacy);

        var refreshRes = await service.RefreshTokenAsync(new RefreshTokenRequest(legacyRawToken));
        Assert.NotNull(refreshRes);
        Assert.Equal("user_legacy", refreshRes.User.Username);

        // 4. Expired refresh token -> UnauthorizedAccessException
        userLegacy.RefreshTokenExpiryTime = DateTime.UtcNow.AddHours(-1);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.RefreshTokenAsync(new RefreshTokenRequest(legacyRawToken)));

        // 5. GetCurrentUserAsync: empty userId -> returns null
        Assert.Null(await service.GetCurrentUserAsync(""));
        Assert.Null(await service.GetCurrentUserAsync("non_existent"));

        // 6. GetCurrentUserAsync: inactive user or inactive tenantUser -> returns null
        userLegacy.IsActive = false;
        Assert.Null(await service.GetCurrentUserAsync("u_legacy_tok"));

        userLegacy.IsActive = true;
        userLegacy.TenantUsers = new List<TenantUser> { new() { TenantId = "t1", UserId = "u_legacy_tok", IsActive = false } };
        Assert.Null(await service.GetCurrentUserAsync("u_legacy_tok"));

        // 7. RevokeRefreshTokenAsync: by raw refreshToken or by userId
        userLegacy.TenantUsers = new List<TenantUser>();
        userLegacy.RefreshToken = "valid_tok";
        userLegacy.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(1);
        await service.RevokeRefreshTokenAsync("u_legacy_tok");
        Assert.Null(userLegacy.RefreshToken);
        Assert.Null(userLegacy.RefreshTokenExpiryTime);
    }

    [Fact]
    public async Task AuthService_RegistrationAndResetOtp_Branches()
    {
        var repo = new FakeUserRepository();
        var mockHasher = new PasswordHasher();
        var emailMock = new Mock<IEmailService>();
        var service = new AuthService(repo, passwordHasher: mockHasher, emailService: emailMock.Object);

        // 1. ResetPasswordAsync: user not found -> KeyNotFoundException
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.ResetPasswordAsync(new ResetPasswordRequest("unknown@mtgrms.vn", "123456", "NewPass@123")));

        // 2. SendRegistrationOtpAsync: invalid email format -> ArgumentException
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.SendRegistrationOtpAsync(new SendRegistrationOtpRequest(Phone: "0901234567", StoreName: "Tiem Moi", Email: "invalid-email")));

        // 3. SendRegistrationOtpAsync: duplicate phone or email -> ArgumentException
        repo.Users.Add(new User { Id = "u_dup", Username = "dup_owner", Phone = "0901234567", Email = "dup@mtgrms.vn", IsActive = true });
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.SendRegistrationOtpAsync(new SendRegistrationOtpRequest(Phone: "0901234567", StoreName: "Tiem Dup", Email: "dup@mtgrms.vn")));

        // 4. RegisterTenantAsync: invalid phone -> ArgumentException
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.RegisterTenantAsync(new RegisterTenantRequest(
                FullName: "Owner", StoreName: "Store", Phone: "abc", Email: "new@mtgrms.vn", Password: "Pass123!", AgreeTerms: true, OtpCode: "123456", ChallengeId: "ChallengeId")));

        // 5. RegisterTenantAsync: duplicate phone/email -> ArgumentException
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.RegisterTenantAsync(new RegisterTenantRequest(
                FullName: "Owner", StoreName: "Store", Phone: "0901234567", Email: "dup@mtgrms.vn", Password: "Pass123!", AgreeTerms: true, OtpCode: "123456", ChallengeId: "ChallengeId")));

        // 6. RegisterTenantAsync: OTP challenge missing/expired -> ArgumentException
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.RegisterTenantAsync(new RegisterTenantRequest(
                FullName: "Owner", StoreName: "Store", Phone: "0911222333", Email: "fresh@mtgrms.vn", Password: "Pass123!", AgreeTerms: true, OtpCode: "123456", ChallengeId: "regotp_missing")));
    }

    // =========================================================================
    // SECTION 2: PaymentsController - Account Matching, Webhooks & Security
    // =========================================================================

    [Fact]
    public async Task PaymentsController_SavePaymentConfig_ReplacesAccount_And_EncryptBranches()
    {
        var tenantId = "tenant-pay-conf";
        var tenantContext = new FakeTenantContext { CurrentTenantId = tenantId };

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"PayConfDb_{Guid.NewGuid()}")
            .Options;
        using var dbContext = new AppDbContext(options, tenantContext);

        var mockFactory = new Mock<IHttpClientFactory>();
        var mockConfig = new Mock<IConfiguration>();
        var mockLogger = new Mock<ILogger<PaymentsController>>();

        var controller = new PaymentsController(dbContext, mockFactory.Object, mockConfig.Object, mockLogger.Object, tenantContext);
        var claims = new List<Claim> { new(ClaimTypes.Role, RoleConstants.StoreOwner), new("tenant_id", tenantId) };
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth")) }
        };

        // Seed existing PaymentAccount with old bank details
        var oldAcc = new PaymentAccount
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BankCode = "VCB",
            AccountNumber = "0011002233",
            AccountHolderName = "OLD OWNER",
            IsActive = true
        };
        dbContext.PaymentAccounts.Add(oldAcc);

        // Seed an existing matching account for the new bank details
        var existingNewAcc = new PaymentAccount
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BankCode = "TCB",
            AccountNumber = "9988776655",
            AccountHolderName = "NEW OWNER",
            IsActive = true
        };
        dbContext.PaymentAccounts.Add(existingNewAcc);
        await dbContext.SaveChangesAsync();

        // 1. SavePaymentConfig: Provide PaymentAccountId pointing to oldAcc, but with TCB / 9988776655
        // This triggers lines 354-360 where paymentAccount is replaced by existingNewAcc
        var req = new SavePaymentConfigRequest
        {
            BankCode = "TCB",
            BankName = "Techcombank",
            BankAccountNumber = "9988776655",
            BankAccountName = "NEW OWNER",
            SepayApiKey = "sepay_key_123456789012345",
            PaymentAccountId = oldAcc.Id,
            IsActive = true
        };

        var resSave = await controller.SavePaymentConfig(req, CancellationToken.None);
        var okSave = Assert.IsType<OkObjectResult>(resSave);
        Assert.NotNull(okSave.Value);

        // Verify config was created and key was encrypted with AES256:
        var savedConfig = await dbContext.StorePaymentConfigs.FirstOrDefaultAsync(c => c.TenantId == tenantId);
        Assert.NotNull(savedConfig);
        Assert.StartsWith("AES256:", savedConfig.SepayApiKeyEncrypted);

        // 2. SavePaymentConfig update without new key: triggers auto-upgrade from plaintext if needed
        savedConfig.SepayApiKeyEncrypted = "raw_plaintext_key";
        await dbContext.SaveChangesAsync();

        var reqUpdate = new SavePaymentConfigRequest
        {
            BankCode = "TCB",
            BankAccountNumber = "9988776655",
            BankAccountName = "NEW OWNER",
            SepayApiKey = null, // no new key
            IsActive = true
        };
        await controller.SavePaymentConfig(reqUpdate, CancellationToken.None);
        Assert.StartsWith("AES256:", savedConfig.SepayApiKeyEncrypted);
    }

    [Fact]
    public async Task PaymentsController_Webhook_PrefixMismatch_And_TwoWayVerification_Branches()
    {
        var tenantId = "tenant_cau_giay_01";
        var tenantContext = new FakeTenantContext { CurrentTenantId = tenantId };

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"PayHookDb_{Guid.NewGuid()}")
            .Options;
        using var dbContext = new AppDbContext(options, tenantContext);

        // Seed order under tenant_cau_giay_01 (expected prefix CG01)
        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrderCode = "HD8801",
            TenantId = tenantId,
            FinalAmount = 300000m,
            PaymentStatus = "UNPAID",
            Status = "PendingPayment",
            CreatedAt = DateTime.UtcNow
        };
        dbContext.Orders.Add(order);

        // Seed StorePaymentConfig with SePay key
        var config = new StorePaymentConfig
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BankCode = "MB",
            BankAccountNumber = "1234567890",
            BankAccountName = "CHU CUA HANG",
            SepayApiKeyEncrypted = "raw_test_key_for_mock",
            SepayWebhookSecret = "whsec_secret123",
            IsActive = true
        };
        dbContext.StorePaymentConfigs.Add(config);
        await dbContext.SaveChangesAsync();

        // Mock HttpClientFactory for 2-way verification
        var mockHttpHandler = new Mock<HttpMessageHandler>();
        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(mockHttpHandler.Object));

        var mockConfig = new Mock<IConfiguration>();
        var mockLogger = new Mock<ILogger<PaymentsController>>();
        var controller = new PaymentsController(dbContext, mockFactory.Object, mockConfig.Object, mockLogger.Object, tenantContext);

        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("10.77.66.55");
        httpContext.Request.Headers["Authorization"] = "Apikey whsec_secret123";
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        // 1. Transaction content contains mismatched prefix: "AN02 HD8801" for tenant "tenant_cau_giay_01" (expected "CG01")
        // Triggers lines 747-754 (Prefix mismatch warning)
        // Also simulate SePay API returning transaction on mismatched bank account -> triggers lines 868-874 (Fraud detection)
        var sepayFraudJson = JsonSerializer.Serialize(new
        {
            transaction = new
            {
                amount_in = 300000m,
                transaction_content = "AN02 HD8801",
                account_number = "9999999999" // Mismatched account!
            }
        });

        mockHttpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(sepayFraudJson, Encoding.UTF8, "application/json")
            });

        var payloadFraud = new SePayWebhookPayload
        {
            Id = 555001,
            AmountIn = 300000m,
            AccountNumber = "1234567890",
            TransactionContent = "AN02 HD8801 chuyen khoan",
            TransactionDate = DateTime.UtcNow
        };

        var resFraud = await controller.HandleSePayWebhook(payloadFraud, CancellationToken.None);
        var badReqFraud = Assert.IsType<BadRequestObjectResult>(resFraud);
        Assert.NotNull(badReqFraud.Value);

        // 2. SePay API returns matching account -> verifiedBySePay = true (lines 879-880) and auto-completes order
        var sepayValidJson = JsonSerializer.Serialize(new
        {
            transaction = new
            {
                amount_in = 300000m,
                transaction_content = "AN02 HD8801",
                account_number = "1234567890" // Matching!
            }
        });

        mockHttpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(sepayValidJson, Encoding.UTF8, "application/json")
            });

        var payloadValid = new SePayWebhookPayload
        {
            Id = 555002,
            AmountIn = 300000m,
            AccountNumber = "1234567890",
            TransactionContent = "AN02 HD8801 chuyen khoan",
            TransactionDate = DateTime.UtcNow
        };

        var resValid = await controller.HandleSePayWebhook(payloadValid, CancellationToken.None);
        var okValid = Assert.IsType<OkObjectResult>(resValid);
        Assert.NotNull(okValid.Value);

        // 3. Test Private Static / Helper Methods via Reflection
        var getTenantPrefixMethod = typeof(PaymentsController).GetMethod("GetTenantPrefix", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(getTenantPrefixMethod);
        Assert.Equal("MT01", getTenantPrefixMethod.Invoke(null, new object?[] { null }));
        Assert.Equal("MT01", getTenantPrefixMethod.Invoke(null, new object?[] { "   " }));
        Assert.Equal("AN02", getTenantPrefixMethod.Invoke(null, new object?[] { "tenant_an_nhien_02" }));
        Assert.Equal("AN01", getTenantPrefixMethod.Invoke(null, new object?[] { "tenant_an_nhien_01" }));
        Assert.Equal("CG01", getTenantPrefixMethod.Invoke(null, new object?[] { "tenant_cau_giay_store" }));
        Assert.Equal("MART", getTenantPrefixMethod.Invoke(null, new object?[] { "mt_mart" }));
        Assert.Equal("MT01", getTenantPrefixMethod.Invoke(null, new object?[] { "xy" }));

        var maskSecretMethod = typeof(PaymentsController).GetMethod("MaskSecret", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(maskSecretMethod);
        Assert.Equal(string.Empty, maskSecretMethod.Invoke(null, new object?[] { "", "whsec_" }));
        Assert.Equal("whsec_••••••••••••", maskSecretMethod.Invoke(null, new object?[] { "short", "whsec_" }));
        Assert.Contains("••••••••••••", (string)maskSecretMethod.Invoke(null, new object?[] { "sepay_apikey_1234567890", "whsec_" })!);

        var fixedTimeEqualsMethod = typeof(PaymentsController).GetMethod("FixedTimeEquals", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(fixedTimeEqualsMethod);
        Assert.False((bool)fixedTimeEqualsMethod.Invoke(null, new object?[] { "", "abc" })!);
        Assert.False((bool)fixedTimeEqualsMethod.Invoke(null, new object?[] { "abc", "" })!);
        Assert.True((bool)fixedTimeEqualsMethod.Invoke(null, new object?[] { "identical", "identical" })!);
        Assert.False((bool)fixedTimeEqualsMethod.Invoke(null, new object?[] { "identical", "different" })!);
    }

    // =========================================================================
    // SECTION 3: TenantStoreController - Subscription Access Status & Trial
    // =========================================================================

    [Fact]
    public async Task TenantStoreController_SubscriptionAccessStatus_And_TrialBranches()
    {
        var tenantId = "tenant-store-stat";
        var userId = "user-store-stat";
        var tenantContext = new FakeTenantContext { CurrentTenantId = tenantId, CurrentUserId = Guid.NewGuid() };

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"TenantStoreDb_{Guid.NewGuid()}")
            .Options;
        using var dbContext = new AppDbContext(options, tenantContext);

        var tenantServiceMock = new Mock<ITenantService>();
        var planServiceMock = new Mock<ISubscriptionPlanService>();
        var controller = new TenantStoreController(tenantServiceMock.Object, planServiceMock.Object, dbContext);

        // 1. Unauthenticated or missing tenant -> 403 Forbidden
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        var resNoAuth = await controller.GetSubscriptionAccessStatus(CancellationToken.None);
        var forbiddenNoAuth = Assert.IsType<ObjectResult>(resNoAuth);
        Assert.Equal(StatusCodes.Status403Forbidden, forbiddenNoAuth.StatusCode);

        // Set authenticated user claims
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new("tenant_id", tenantId)
        };
        controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));

        // 2. User exists in claims but not in DB -> 403 Forbidden
        var tenant = new Tenant
        {
            Id = tenantId,
            Name = "Cua Hang Mau",
            SubscriptionStatus = "ACTIVE",
            CreatedAt = DateTime.UtcNow
        };
        dbContext.Tenants.Add(tenant);
        dbContext.TenantUsers.Add(new TenantUser { TenantId = tenantId, UserId = userId, IsActive = true });
        await dbContext.SaveChangesAsync();

        var resUserNotFound = await controller.GetSubscriptionAccessStatus(CancellationToken.None);
        var forbiddenUserNotFound = Assert.IsType<ObjectResult>(resUserNotFound);
        Assert.Equal(StatusCodes.Status403Forbidden, forbiddenUserNotFound.StatusCode);

        // Add user to DB
        var user = new User
        {
            Id = userId,
            Username = "store_owner",
            IsActive = true,
            IsEmailVerified = true
        };
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        // 3. Status: EXPIRED via explicit status
        tenant.SubscriptionStatus = "EXPIRED";
        await dbContext.SaveChangesAsync();
        var resExpired = await controller.GetSubscriptionAccessStatus(CancellationToken.None);
        var okExpired = Assert.IsType<OkObjectResult>(resExpired);
        Assert.NotNull(okExpired.Value);

        // 4. Status: EXPIRED via paid expiry
        tenant.SubscriptionStatus = "ACTIVE";
        tenant.FirstPaidAt = DateTime.UtcNow.AddMonths(-1);
        tenant.SubscriptionExpiresAt = DateTime.UtcNow.AddDays(-1);
        await dbContext.SaveChangesAsync();
        var resPaidExpired = await controller.GetSubscriptionAccessStatus(CancellationToken.None);
        Assert.IsType<OkObjectResult>(resPaidExpired);

        // 5. Status: PAID (active paid)
        tenant.SubscriptionExpiresAt = DateTime.UtcNow.AddMonths(1);
        await dbContext.SaveChangesAsync();
        var resPaid = await controller.GetSubscriptionAccessStatus(CancellationToken.None);
        Assert.IsType<OkObjectResult>(resPaid);

        // 6. Status: ACTIVE (trial active)
        tenant.FirstPaidAt = null;
        tenant.SubscriptionStatus = "TRIAL";
        tenant.TrialStartedAt = DateTime.UtcNow.AddHours(-12);
        tenant.TrialExpiresAt = DateTime.UtcNow.AddHours(60);
        tenant.SubscriptionExpiresAt = null;
        await dbContext.SaveChangesAsync();
        var resTrialActive = await controller.GetSubscriptionAccessStatus(CancellationToken.None);
        Assert.IsType<OkObjectResult>(resTrialActive);

        // 7. Status: ELIGIBLE (no trial started, NO_PLAN, user has not consumed trial)
        tenant.TrialStartedAt = null;
        tenant.TrialExpiresAt = null;
        tenant.SubscriptionStatus = "NO_PLAN";
        user.TrialConsumedAt = null;
        await dbContext.SaveChangesAsync();
        var resEligible = await controller.GetSubscriptionAccessStatus(CancellationToken.None);
        Assert.IsType<OkObjectResult>(resEligible);

        // 8. Status: REVIEW_REQUIRED (user consumed trial but still NO_PLAN)
        user.TrialConsumedAt = DateTime.UtcNow.AddDays(-10);
        await dbContext.SaveChangesAsync();
        var resReview = await controller.GetSubscriptionAccessStatus(CancellationToken.None);
        Assert.IsType<OkObjectResult>(resReview);

        // 9. PurchaseSubscription with TRIAL plan code -> 400 BadRequest (TRIAL_ENDPOINT_REQUIRED)
        var purchaseReqTrial = new PurchaseSubscriptionRequest(
            PlanCode: "TRIAL",
            Months: 0,
            ExtraBranches: 0,
            ExtraStorageGb: 0,
            HasStaffManagementAddon: false,
            PaymentMethod: "NONE");

        var purchaseTrialRes = await controller.PurchaseSubscription(purchaseReqTrial, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(purchaseTrialRes);

        // 10. PurchaseSubscription with STANDARD plan and staff management addon -> sets HasStaffManagementAddon = true
        tenant.WalletBalance = 100_000_000m;
        await dbContext.SaveChangesAsync();

        var purchaseReqStandard = new PurchaseSubscriptionRequest(
            PlanCode: "STANDARD",
            Months: 12,
            ExtraBranches: 0,
            ExtraStorageGb: 0,
            HasStaffManagementAddon: true,
            PaymentMethod: "WALLET");

        var purchaseStandardRes = await controller.PurchaseSubscription(purchaseReqStandard, CancellationToken.None);
        var okPurchase = Assert.IsType<OkObjectResult>(purchaseStandardRes);
        Assert.NotNull(okPurchase.Value);
        Assert.Equal("STANDARD", tenant.CurrentPlan);
        Assert.True(tenant.HasStaffManagementAddon);
    }

    // =========================================================================
    // SECTION 4: TransferService - Filtering, Stock Resolution & Damaged Items
    // =========================================================================

    [Fact]
    public async Task TransferService_GetTransfers_FilterCombinations_Branches()
    {
        var tenantContext = new FakeTenantContext();
        var mockUow = new FakeUnitOfWork();

        var transfersRepo = new FakeRepository<Transfer>();
        var detailsRepo = new FakeRepository<TransferDetail>();
        var stocksRepo = new FakeRepository<BranchStock>();
        var productsRepo = new FakeRepository<Product>();
        var branchesRepo = new FakeRepository<Branch>();

        var service = new TransferService(
            transfersRepo, detailsRepo, stocksRepo, productsRepo, branchesRepo,
            tenantContext, mockUow);

        var bFrom = new Branch { Id = Guid.NewGuid(), TenantId = tenantContext.CurrentTenantId, Name = "Branch From", IsActive = true };
        var bTo = new Branch { Id = Guid.NewGuid(), TenantId = tenantContext.CurrentTenantId, Name = "Branch To", IsActive = true };
        branchesRepo.Items.AddRange(new[] { bFrom, bTo });

        var tr1 = new Transfer
        {
            Id = Guid.NewGuid(),
            TenantId = tenantContext.CurrentTenantId,
            TransferCode = "TRF-001",
            FromBranchId = bFrom.Id,
            ToBranchId = bTo.Id,
            Status = TransferStatus.Received,
            ReceivedAt = DateTime.UtcNow.AddDays(-2),
            CreatedAt = DateTime.UtcNow.AddDays(-5),
            DispatchedAt = DateTime.UtcNow.AddDays(-3)
        };
        var tr2 = new Transfer
        {
            Id = Guid.NewGuid(),
            TenantId = tenantContext.CurrentTenantId,
            TransferCode = "TRF-002",
            FromBranchId = bFrom.Id,
            ToBranchId = bTo.Id,
            Status = TransferStatus.InTransit,
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            DispatchedAt = DateTime.UtcNow
        };
        transfersRepo.Items.AddRange(new[] { tr1, tr2 });

        detailsRepo.Items.Add(new TransferDetail
        {
            Id = Guid.NewGuid(),
            TransferId = tr1.Id,
            SentQuantity = 10m,
            ReceivedQuantity = 10m,
            DamagedQuantity = 0m,
            LineStatus = TransferLineStatus.Matched
        });

        // 1. Filter by FromBranchId and ToBranchId
        var paged1 = await service.GetTransfersAsync(new TransferFilterParams(
            Search: null,
            FromBranchId: bFrom.Id,
            ToBranchId: bTo.Id,
            Status: null,
            FromDate: null,
            ToDate: null
        ), CancellationToken.None);
        Assert.Equal(2, paged1.TotalItems);

        // 2. Filter by DateField = "receive"
        var pagedReceiveDate = await service.GetTransfersAsync(new TransferFilterParams(
            Search: null,
            FromBranchId: null,
            ToBranchId: null,
            Status: null,
            FromDate: DateTime.UtcNow.AddDays(-3),
            ToDate: DateTime.UtcNow,
            DateField: "receive"
        ), CancellationToken.None);
        Assert.Single(pagedReceiveDate.Items);

        // 3. Filter by DateField = "dispatch"
        var pagedDispatchDate = await service.GetTransfersAsync(new TransferFilterParams(
            Search: null,
            FromBranchId: null,
            ToBranchId: null,
            Status: null,
            FromDate: DateTime.UtcNow.AddDays(-4),
            ToDate: DateTime.UtcNow,
            DateField: "dispatch"
        ), CancellationToken.None);
        Assert.Equal(2, pagedDispatchDate.Items.Count);

        // 4. Filter by Search keyword
        var pagedSearch = await service.GetTransfersAsync(new TransferFilterParams(
            Search: "TRF-002",
            FromBranchId: null,
            ToBranchId: null,
            Status: null,
            FromDate: null,
            ToDate: null
        ), CancellationToken.None);
        Assert.Single(pagedSearch.Items);
        Assert.Equal("TRF-002", pagedSearch.Items[0].TransferCode);

        // 5. Filter by ReceiveStatus = "match"
        var pagedMatch = await service.GetTransfersAsync(new TransferFilterParams(
            Search: null,
            FromBranchId: null,
            ToBranchId: null,
            Status: null,
            FromDate: null,
            ToDate: null,
            ReceiveStatus: "match"
        ), CancellationToken.None);
        Assert.Single(pagedMatch.Items);

        // 6. Filter by ReceiveStatus = "mismatch"
        var pagedMismatch = await service.GetTransfersAsync(new TransferFilterParams(
            Search: null,
            FromBranchId: null,
            ToBranchId: null,
            Status: null,
            FromDate: null,
            ToDate: null,
            ReceiveStatus: "mismatch"
        ), CancellationToken.None);
        Assert.Empty(pagedMismatch.Items);

        // 7. GetTransferAsync(id) retrieves transfer by id
        var singleDto = await service.GetTransferAsync(tr1.Id, CancellationToken.None);
        Assert.Equal("TRF-001", singleDto.TransferCode);
    }

    [Fact]
    public async Task TransferService_StockResolution_And_DamagedReceipt_Branches()
    {
        var tenantContext = new FakeTenantContext();
        var mockUow = new FakeUnitOfWork();

        var transfersRepo = new FakeRepository<Transfer>();
        var detailsRepo = new FakeRepository<TransferDetail>();
        var stocksRepo = new FakeRepository<BranchStock>();
        var productsRepo = new FakeRepository<Product>();
        var branchesRepo = new FakeRepository<Branch>();

        var service = new TransferService(
            transfersRepo, detailsRepo, stocksRepo, productsRepo, branchesRepo,
            tenantContext, mockUow);

        var bFrom = new Branch { Id = Guid.NewGuid(), TenantId = tenantContext.CurrentTenantId, Name = "Branch From", IsActive = true };
        var bTo = new Branch { Id = Guid.NewGuid(), TenantId = tenantContext.CurrentTenantId, Name = "Branch To", IsActive = true };
        branchesRepo.Items.AddRange(new[] { bFrom, bTo });

        var product = new Product
        {
            Id = Guid.NewGuid(),
            TenantId = tenantContext.CurrentTenantId,
            ProductCode = "SP-MULTI-BATCH",
            Name = "Sản phẩm Nhiều Lô",
            BaseUnit = "Hộp"
        };
        productsRepo.Items.Add(product);

        // Seed multiple stocks at bFrom to trigger ResolveStockSourceAsync branches
        var exp1 = DateTime.UtcNow.AddDays(10);
        var exp2 = DateTime.UtcNow.AddDays(20);
        var stock1 = new BranchStock
        {
            Id = Guid.NewGuid(),
            BranchId = bFrom.Id,
            ProductId = product.Id,
            BatchCode = "BATCH-A",
            ExpiryDate = exp1,
            QuantityOnHand = 100m
        };
        var stock2 = new BranchStock
        {
            Id = Guid.NewGuid(),
            BranchId = bFrom.Id,
            ProductId = product.Id,
            BatchCode = "BATCH-A", // duplicate batch code with different expiry
            ExpiryDate = exp2,
            QuantityOnHand = 80m
        };
        var stock3 = new BranchStock
        {
            Id = Guid.NewGuid(),
            BranchId = bFrom.Id,
            ProductId = product.Id,
            BatchCode = "BATCH-B",
            ExpiryDate = exp1,
            QuantityOnHand = 50m
        };
        stocksRepo.Items.AddRange(new[] { stock1, stock2, stock3 });

        // 1. CreateTransferAsync with BATCH-A and exp2 date -> matches stock2
        var createReq = new CreateTransferRequest(
            bFrom.Id,
            bTo.Id,
            DispatchNow: true,
            ExpectedDeliveryDate: DateTime.UtcNow.AddDays(2),
            Note: "Chuyen hang nhieu lo",
            Items: new List<TransferLineRequest>
            {
                new(product.Id, 10m, "Hộp", "BATCH-A", exp2, 35000m)
            });

        var createdDto = await service.CreateTransferAsync(createReq, CancellationToken.None);
        Assert.NotNull(createdDto);
        var createdTransfer = transfersRepo.Items.First(t => t.Id == createdDto.Id);
        Assert.Equal(TransferStatus.InTransit, createdTransfer.Status);

        // 2. ReceiveTransferAsync: missing reason for damaged items -> InvalidOperationException
        var lineDetail = createdTransfer.Details.First();
        var badReceiveReq = new ReceiveTransferRequest(
            Note: "Nhan hang",
            Items: new List<ReceiveTransferLineRequest>
            {
                new(lineDetail.Id, 8m, 2m, null) // Damaged = 2 but no reason
            });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ReceiveTransferAsync(createdTransfer.Id, badReceiveReq, CancellationToken.None));

        // 3. ReceiveTransferAsync: valid with damaged items -> updates dest stock quarantined
        var goodReceiveReq = new ReceiveTransferRequest(
            Note: "Đã nhận có hàng hỏng",
            Items: new List<ReceiveTransferLineRequest>
            {
                new(lineDetail.Id, 8m, 2m, "Bị móp méo khi vận chuyển")
            });

        var receivedDto = await service.ReceiveTransferAsync(createdTransfer.Id, goodReceiveReq, CancellationToken.None);
        Assert.Equal(TransferStatus.Received.ToString(), receivedDto.Status);

        // Verify destination stock was created with quarantined amount
        var destStock = stocksRepo.Items.FirstOrDefault(s => s.BranchId == bTo.Id && s.ProductId == product.Id);
        Assert.NotNull(destStock);
        Assert.Equal(10m, destStock.QuantityOnHand);
        Assert.Equal(2m, destStock.QuantityQuarantined);
    }
}
