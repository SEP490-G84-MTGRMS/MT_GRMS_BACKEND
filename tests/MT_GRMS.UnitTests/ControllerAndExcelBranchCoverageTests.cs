using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using MT_GRMS.Api.Controllers;
using MT_GRMS.Application.Admin.Dtos;
using MT_GRMS.Application.Admin.Services;
using MT_GRMS.Application.Auth;
using MT_GRMS.Application.Auth.Dtos;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Common.Interfaces;
using MT_GRMS.Application.Common.DTOs;
using MT_GRMS.Application.PurchaseOrders;
using MT_GRMS.Application.PurchaseOrders.Dtos;
using MT_GRMS.Domain.Constants;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Entities.Identity;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Entities.Sales;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Domain.Exceptions;
using MT_GRMS.Infrastructure.Persistence;
using MT_GRMS.Infrastructure.Services;
using MT_GRMS.UnitTests.Controllers;

namespace MT_GRMS.UnitTests;

public class ControllerAndExcelBranchCoverageTests
{
    private class FakeTenantContext : ITenantContext
    {
        public string CurrentTenantId { get; set; } = "tenant-ctrl-01";
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
            => Task.FromResult<IReadOnlyList<User>>(Users.ToList());

        public Task<User?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult(Users.FirstOrDefault(u => u.Id == id));

        public Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default)
            => Task.FromResult(Users.FirstOrDefault(u =>
                string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(u.Email, username, StringComparison.OrdinalIgnoreCase)));

        public void Update(User user)
        {
            var idx = Users.FindIndex(u => u.Id == user.Id);
            if (idx >= 0) Users[idx] = user;
            else Users.Add(user);
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> ExistsByPhoneOrEmailAsync(string phone, string? email, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<User> RegisterStoreOwnerAsync(
            User user,
            Tenant tenant,
            Branch branch,
            CancellationToken cancellationToken = default)
        {
            Users.Add(user);
            return Task.FromResult(user);
        }
    }

    // =========================================================================
    // SECTION 1: AuthService OTP & 2FA Branches
    // =========================================================================

    [Fact]
    public async Task AuthService_SendLoginOtp_And_VerifyLoginOtp_Branches()
    {
        var userRepo = new FakeUserRepository();
        var hasher = new PasswordHasher();
        var mockEmail = new Mock<IEmailService>();
        mockEmail.Setup(e => e.SendLoginOtpAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var configValues = new Dictionary<string, string?>
        {
            { "ASPNETCORE_ENVIRONMENT", "Development" },
            { "Jwt:Key", "MT_GRMS_SUPER_SECRET_KEY_FOR_TESTING_PURPOSES_ONLY_2026_V4" },
            { "Jwt:Issuer", "MT_GRMS" },
            { "Jwt:Audience", "MT_GRMS_USERS" }
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(configValues).Build();

        var authService = new AuthService(userRepo, config, hasher, mockEmail.Object);

        // Seed 2FA enabled user
        var user2fa = new User
        {
            Id = Guid.NewGuid().ToString(),
            Username = "user_2fa",
            Email = "user2fa@mtgrms.vn",
            FullName = "Nguyen 2FA",
            IsActive = true,
            IsEmailVerified = true,
            IsTwoFactorEnabled = true
        };
        user2fa.PasswordHash = hasher.HashPassword("StrongPass123!");
        await userRepo.AddAsync(user2fa);

        // Seed normal user (IsTwoFactorEnabled = false)
        var userNormal = new User
        {
            Id = Guid.NewGuid().ToString(),
            Username = "user_normal",
            Email = "normal@mtgrms.vn",
            FullName = "Nguyen Normal",
            IsActive = true,
            IsEmailVerified = true,
            IsTwoFactorEnabled = false
        };
        userNormal.PasswordHash = hasher.HashPassword("StrongPass123!");
        userNormal.TenantUsers.Add(new TenantUser
        {
            UserId = userNormal.Id, TenantId = "auth-test-tenant", Role = SystemRoles.StoreOwner
        });
        await userRepo.AddAsync(userNormal);

        // 1. SendLoginOtpAsync for normal user immediately returns session without OTP
        var normalRes = await authService.SendLoginOtpAsync(new LoginRequest("user_normal", "StrongPass123!", null), CancellationToken.None);
        Assert.False(normalRes.RequiresOtp);
        Assert.NotNull(normalRes.Session);

        // 2. SendLoginOtpAsync for 2FA user triggers OTP generation
        var otpRes = await authService.SendLoginOtpAsync(new LoginRequest("user_2fa", "StrongPass123!", null), CancellationToken.None);
        Assert.True(otpRes.RequiresOtp);
        Assert.NotEmpty(otpRes.TempToken);

        // 3. Verify with non-existent temp token throws ArgumentException
        await Assert.ThrowsAsync<ArgumentException>(() =>
            authService.VerifyLoginOtpAsync(new VerifyLoginOtpRequest("otp_login_nonexistent123", "123456"), CancellationToken.None));

        // 4. Verify with wrong OTP increments failure count and throws
        var exWrong1 = await Assert.ThrowsAsync<ArgumentException>(() =>
            authService.VerifyLoginOtpAsync(new VerifyLoginOtpRequest(otpRes.TempToken, "000000"), CancellationToken.None));
        Assert.Contains("còn 4 lần thử", exWrong1.Message);

        // 5. Exceeding 5 failures removes token and locks session
        await Assert.ThrowsAsync<ArgumentException>(() => authService.VerifyLoginOtpAsync(new VerifyLoginOtpRequest(otpRes.TempToken, "000000"), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => authService.VerifyLoginOtpAsync(new VerifyLoginOtpRequest(otpRes.TempToken, "000000"), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => authService.VerifyLoginOtpAsync(new VerifyLoginOtpRequest(otpRes.TempToken, "000000"), CancellationToken.None));
        var exLock = await Assert.ThrowsAsync<ArgumentException>(() =>
            authService.VerifyLoginOtpAsync(new VerifyLoginOtpRequest(otpRes.TempToken, "000000"), CancellationToken.None));
        Assert.Contains("Bạn đã nhập sai mã OTP quá 5 lần", exLock.Message);
    }

    [Fact]
    public async Task AuthService_TwoFactorSettings_AllBranches()
    {
        var userRepo = new FakeUserRepository();
        var hasher = new PasswordHasher();
        var mockEmail = new Mock<IEmailService>();
        var sentOtps = new List<string>();
        mockEmail.Setup(e => e.SendTwoFactorSettingsOtpAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, CancellationToken>((_, _, otp, _) => sentOtps.Add(otp))
            .ReturnsAsync(true);

        var configValues = new Dictionary<string, string?>
        {
            { "ASPNETCORE_ENVIRONMENT", "Development" }
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(configValues).Build();
        var authService = new AuthService(userRepo, config, hasher, mockEmail.Object);

        // 1. GetTwoFactorStatusAsync unknown user throws UnauthorizedAccessException
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            authService.GetTwoFactorStatusAsync(Guid.NewGuid().ToString(), CancellationToken.None));

        // Seed user with unverified email
        var userUnverified = new User
        {
            Id = Guid.NewGuid().ToString(),
            Username = "unverified_user",
            Email = "unverified@mtgrms.vn",
            IsActive = true,
            IsEmailVerified = false,
            IsTwoFactorEnabled = false
        };
        userUnverified.PasswordHash = hasher.HashPassword("StrongPass123!");
        await userRepo.AddAsync(userUnverified);

        // 2. Create challenge with unverified email throws ArgumentException
        await Assert.ThrowsAsync<ArgumentException>(() =>
            authService.CreateTwoFactorChallengeAsync(userUnverified.Id, new TwoFactorChallengeRequest(true, "StrongPass123!"), CancellationToken.None));

        // Seed user with verified email
        var userVerified = new User
        {
            Id = Guid.NewGuid().ToString(),
            Username = "verified_user",
            Email = "verified@mtgrms.vn",
            FullName = "User Verified",
            IsActive = true,
            IsEmailVerified = true,
            IsTwoFactorEnabled = false
        };
        userVerified.PasswordHash = hasher.HashPassword("StrongPass123!");
        await userRepo.AddAsync(userVerified);

        // 3. Create challenge valid in Development sends email and creates token
        var challenge = await authService.CreateTwoFactorChallengeAsync(userVerified.Id, new TwoFactorChallengeRequest(true, "StrongPass123!"), CancellationToken.None);
        Assert.NotNull(challenge);
        Assert.NotEmpty(challenge.ChallengeToken);
        Assert.NotEmpty(sentOtps);
        var validOtp = sentOtps.Last();

        // 4. Confirm challenge with invalid token throws UnauthorizedAccessException
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            authService.ConfirmTwoFactorChallengeAsync(userVerified.Id, new TwoFactorConfirmRequest("invalid_token", "123456"), CancellationToken.None));

        // 5. Confirm challenge with wrong OTP throws ArgumentException
        await Assert.ThrowsAsync<ArgumentException>(() =>
            authService.ConfirmTwoFactorChallengeAsync(userVerified.Id, new TwoFactorConfirmRequest(challenge.ChallengeToken, "000000"), CancellationToken.None));

        // Create fresh challenge for success test (previous token was consumed by TryRemove)
        var challengeSuccess = await authService.CreateTwoFactorChallengeAsync(userVerified.Id, new TwoFactorChallengeRequest(true, "StrongPass123!"), CancellationToken.None);

        // 6. Confirm challenge with correct OTP succeeds and enables 2FA
        var confirmRes = await authService.ConfirmTwoFactorChallengeAsync(userVerified.Id, new TwoFactorConfirmRequest(challengeSuccess.ChallengeToken, sentOtps.Last()), CancellationToken.None);
        Assert.True(confirmRes.Enabled);
        Assert.True(userVerified.IsTwoFactorEnabled);
    }

    // =========================================================================
    // SECTION 2: TenantStoreController Branches
    // =========================================================================

    [Fact]
    public async Task TenantStoreController_GetPricingPlans_Fallback_Branches()
    {
        var tenantContext = new FakeTenantContext();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"TenantStorePlans_{Guid.NewGuid()}")
            .Options;
        using var dbContext = new AppDbContext(options, tenantContext);

        var mockTenantService = new Mock<ITenantService>();
        var mockPlanService = new Mock<ISubscriptionPlanService>();

        // When DB has NO official plans, GetPricingPlans generates default TRIAL, STANDARD, PRO
        mockPlanService.Setup(p => p.GetPlansAsync(It.IsAny<PlanFilterRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlanSummaryResponse>());

        var controller = new TenantStoreController(mockTenantService.Object, mockPlanService.Object, dbContext, new TenantSubscriptionService(dbContext));

        var result = await controller.GetPricingPlans(CancellationToken.None);
        var okResult = Assert.IsType<OkObjectResult>(result);
        var apiRes = Assert.IsType<ApiResponse<IReadOnlyList<PlanSummaryResponse>>>(okResult.Value);

        var plans = Assert.IsAssignableFrom<IReadOnlyList<PlanSummaryResponse>>(apiRes.Data);
        Assert.Equal(3, plans.Count);
        Assert.Contains(plans, p => p.Code == "TRIAL");
        Assert.Contains(plans, p => p.Code == "STANDARD");
        Assert.Contains(plans, p => p.Code == "PRO");
    }

    [Fact]
    public async Task TenantStoreController_StoreInfo_And_UpdateStoreInfo_Branches()
    {
        var tenantId = "tenant-store-branch-01";
        var userId = Guid.NewGuid().ToString();
        var tenantContext = new FakeTenantContext { CurrentTenantId = tenantId };

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"TenantStoreInfo_{Guid.NewGuid()}")
            .Options;
        using var dbContext = new AppDbContext(options, tenantContext);

        var mockTenantService = new Mock<ITenantService>();
        var mockPlanService = new Mock<ISubscriptionPlanService>();
        var controller = new TenantStoreController(mockTenantService.Object, mockPlanService.Object, dbContext, new TenantSubscriptionService(dbContext));

        // Helper to set ControllerContext with claims
        void SetUser(string? tId, string? uId)
        {
            var claims = new List<Claim>();
            if (tId != null) claims.Add(new Claim("tenant_id", tId));
            if (uId != null) claims.Add(new Claim(ClaimTypes.NameIdentifier, uId));
            var identity = new ClaimsIdentity(claims, "TestAuth");
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
            };
        }

        // 1. Missing tenant_id claim -> NotFound
        SetUser(null, userId);
        var res1 = await controller.GetStoreInfo(CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(res1);

        // 2. tenant_id == "DEFAULT" -> NotFound
        SetUser("DEFAULT", userId);
        var res2 = await controller.GetStoreInfo(CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(res2);

        // 3. User not in TenantUsers -> NotFound
        SetUser(tenantId, userId);
        var res3 = await controller.GetStoreInfo(CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(res3);

        // 4. User is active in TenantUsers -> Ok
        var tenant = new Tenant { Id = tenantId, Name = "Store ABC", Status = "ACTIVE" };
        var tenantUser = new TenantUser { TenantId = tenantId, UserId = userId, IsActive = true };
        dbContext.Tenants.Add(tenant);
        dbContext.TenantUsers.Add(tenantUser);
        await dbContext.SaveChangesAsync();

        var detailResponse = new TenantDetailResponse(
            Id: tenantId,
            Subdomain: "sub",
            Name: "Store ABC",
            Code: "T001",
            TaxCode: "0123456789",
            Hotline: "0900000000",
            Address: "123 Street",
            City: "HCM",
            Status: "ACTIVE",
            CurrentPlan: "TRIAL",
            PlanName: "Gói Dùng Thử",
            SubscriptionExpiresAt: DateTime.UtcNow.AddDays(3),
            GracePeriodExpiresAt: null,
            LockType: null,
            LockReason: null,
            LockedAt: null,
            LockedBy: null,
            BranchCount: 1,
            MaxBranches: 1,
            StaffCount: 1,
            MaxStaff: 5,
            Branches: new List<TenantDetailBranchSummaryDto>(),
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: null,
            WalletBalance: 0
        );

        mockTenantService.Setup(s => s.GetByIdAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detailResponse);

        var res4 = await controller.GetStoreInfo(CancellationToken.None);
        Assert.IsType<OkObjectResult>(res4);

        // 5. UpdateStoreInfo without valid tenant -> NotFound
        SetUser("DEFAULT", userId);
        var res5 = await controller.UpdateStoreInfo(new UpdateTenantRequest("Store New", null, null, null, null), CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(res5);

        // 6. UpdateStoreInfo with valid tenant -> Ok
        SetUser(tenantId, userId);
        mockTenantService.Setup(s => s.UpdateTenantAsync(tenantId, It.IsAny<UpdateTenantRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(detailResponse);

        var res6 = await controller.UpdateStoreInfo(new UpdateTenantRequest("Store New", null, null, null, null), CancellationToken.None);
        Assert.IsType<OkObjectResult>(res6);
    }

    // =========================================================================
    // SECTION 3: PaymentsController Branches
    // =========================================================================

    [Fact]
    public async Task PaymentsController_SavePaymentConfig_EncryptionAndThresholds_Branches()
    {
        var tenantId = "tenant-pay-branch-01";
        var tenantContext = new FakeTenantContext { CurrentTenantId = tenantId };

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"PaymentsConfigDb_{Guid.NewGuid()}")
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        using var dbContext = new AppDbContext(options, tenantContext);

        var mockFactory = new Mock<IHttpClientFactory>();
        var mockConfig = new Mock<IConfiguration>();
        var mockLogger = new Mock<ILogger<PaymentsController>>();

        var controller = PaymentTestSupport.CreateController(dbContext, mockFactory.Object, mockConfig.Object, mockLogger.Object, tenantContext);
        var claims = new List<Claim>
        {
            new(ClaimTypes.Role, SystemRoles.StoreOwner),
            new("tenant_id", tenantId)
        };
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth")) }
        };

        PaymentTestSupport.AddTenant(dbContext, tenantId, enabled: true, apiKey: "old-test-sepay-key");
        var oldBank = PaymentTestSupport.AddBank(dbContext, tenantId, "1234567890");
        await dbContext.SaveChangesAsync();

        var request = new SavePaymentConfigRequest
        {
            BankCode = "VCB",
            BankName = "Vietcombank",
            BankAccountNumber = "9876543210",
            BankAccountName = "NGUYEN VAN B",
            BranchIds = [oldBank.BranchId!.Value],
            SepayApiKey = "replacement-test-sepay-key",
            CashOutApprovalThreshold = 500000m,
            CashOutShiftTotalLimit = 2000000m,
            IsActive = true
        };

        var result = await controller.SavePaymentConfig(request, CancellationToken.None);
        Assert.IsType<OkObjectResult>(result);

        // Verify config was encrypted and updated
        var updated = await dbContext.Tenants.SingleAsync(c => c.Id == tenantId);
        Assert.StartsWith("AES256:", updated.SepayApiKeyEncrypted);
        Assert.DoesNotContain(request.SepayApiKey, updated.SepayApiKeyEncrypted);
        Assert.NotEmpty(updated.SepayWebhookSecret);
        Assert.Equal(500000m, updated.CashOutApprovalThreshold);
        Assert.Equal(2000000m, updated.CashOutShiftTotalLimit);

        var paymentAccount = await dbContext.FinanceAccounts.SingleAsync(p => p.TenantId == tenantId && p.BankCode == "VCB");
        Assert.Equal("9876543210", paymentAccount.AccountNumber);
        var branch = await dbContext.Branches.SingleAsync(b => b.Id == oldBank.BranchId);
        Assert.Equal(paymentAccount.Id, branch.CurrentBankFinanceAccountId);
        Assert.Equal("1234567890", oldBank.AccountNumber);
    }

    // =========================================================================
    // SECTION 4: MatrixExcelImportService Branches
    // =========================================================================

    [Fact]
    public async Task MatrixExcelImportService_Preview_ValidationBranches()
    {
        var tenant = new FakeTenantContext();
        var mockPoService = new Mock<IPurchaseOrderService>();
        var service = new MatrixExcelImportService(mockPoService.Object, tenant);

        var branchId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
        var prodId = Guid.NewGuid();

        var matrix = new OrderMatrixResponse
        {
            BranchId = branchId,
            BranchName = "Kho Tổng",
            SupplierConfigs =
            [
                new OrderMatrixSupplierDeliveryConfigDto
                {
                    SupplierId = supplierId,
                    SupplierCode = "SUP01",
                    SupplierName = "NCC A",
                    IsActive = true
                }
            ],
            Products =
            [
                new OrderMatrixProductDto
                {
                    Id = prodId,
                    SupplierId = supplierId,
                    Sku = "SKU-01",
                    Name = "Sản phẩm A",
                    IsProductActive = true,
                    IsSupplierActive = true,
                    IsSupplierProductActive = true,
                    IsSupplierBranchActive = true,
                    IsBranchProductActive = true
                }
            ]
        };

        mockPoService.Setup(p => p.GetOrderMatrixAsync(It.IsAny<OrderMatrixQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(matrix);

        var query = new MatrixExcelPreviewQuery
        {
            BranchId = branchId,
            SupplierId = supplierId,
            ViewFrom = new DateTime(2026, 11, 20),
            DurationDays = 7,
            FileName = "matrix.xlsx",
            FileSize = 1024
        };

        // 1. Corrupt stream -> FORMAT_INVALID
        using var corruptStream = new MemoryStream(new byte[] { 0x01, 0x02, 0x03 });
        var res1 = await service.PreviewAsync(corruptStream, query, CancellationToken.None);
        Assert.False(res1.Valid);
        Assert.Contains(res1.Errors, e => e.Code == "FORMAT_INVALID");

        // 2. Workbook without PO_Matrix sheet -> SHEET_NOT_FOUND
        using var emptyWb = new XLWorkbook();
        emptyWb.Worksheets.Add("OtherSheet");
        using var emptyMs = new MemoryStream();
        emptyWb.SaveAs(emptyMs);
        emptyMs.Position = 0;

        var res2 = await service.PreviewAsync(emptyMs, query, CancellationToken.None);
        Assert.False(res2.Valid);
        Assert.Contains(res2.Errors, e => e.Code == "SHEET_NOT_FOUND");

        // 3. Sheet PO_Matrix with NO "Mã sản phẩm" column -> ParseWorkbook returns empty Rows -> SHEET_NOT_FOUND
        using var noSkuWb = new XLWorkbook();
        var wsNoSku = noSkuWb.Worksheets.Add("PO_Matrix");
        wsNoSku.Cell(1, 1).Value = "Chi nhánh: Kho Tổng";
        wsNoSku.Cell(2, 1).Value = "Nhà cung cấp: ALL";
        wsNoSku.Cell(3, 1).Value = "Thời gian: 20/11/2026 - 26/11/2026";
        wsNoSku.Cell(5, 1).Value = "STT";
        wsNoSku.Cell(5, 2).Value = "Tên sản phẩm";
        wsNoSku.Cell(5, 3).Value = "[2026-11-21]";
        wsNoSku.Cell(6, 1).Value = 1;
        wsNoSku.Cell(6, 2).Value = "Sản phẩm A";
        wsNoSku.Cell(6, 3).Value = 10;
        using var noSkuMs = new MemoryStream();
        noSkuWb.SaveAs(noSkuMs);
        noSkuMs.Position = 0;

        var res3 = await service.PreviewAsync(noSkuMs, query, CancellationToken.None);
        Assert.False(res3.Valid);
        Assert.Contains(res3.Errors, e => e.Code == "SHEET_NOT_FOUND");

        // 4. Sheet with SKU but NO delivery date column -> NO_DELIVERY_DATE
        using var noDateWb = new XLWorkbook();
        var wsNoDate = noDateWb.Worksheets.Add("PO_Matrix");
        wsNoDate.Cell(1, 1).Value = "Header";
        wsNoDate.Cell(2, 1).Value = $"Chi nhánh: Kho Tổng | Mã CN: {branchId}";
        wsNoDate.Cell(3, 1).Value = $"Nhà cung cấp: NCC A | Mã NCC: {supplierId}";
        wsNoDate.Cell(4, 1).Value = "Thời gian: 20/11/2026 - 26/11/2026";
        wsNoDate.Cell(5, 1).Value = "STT";
        wsNoDate.Cell(5, 2).Value = "Mã sản phẩm";
        wsNoDate.Cell(5, 3).Value = "Tên sản phẩm";
        wsNoDate.Cell(6, 1).Value = 1;
        wsNoDate.Cell(6, 2).Value = "SKU-01";
        wsNoDate.Cell(6, 3).Value = "Sản phẩm A";
        using var noDateMs = new MemoryStream();
        noDateWb.SaveAs(noDateMs);
        noDateMs.Position = 0;

        var res4 = await service.PreviewAsync(noDateMs, query, CancellationToken.None);
        Assert.False(res4.Valid);
        Assert.Contains(res4.Errors, e => e.Code == "NO_DELIVERY_DATE");

        // 5. Sheet with delivery date outside query period -> INVALID_PERIOD
        using var outDateWb = new XLWorkbook();
        var wsOutDate = outDateWb.Worksheets.Add("PO_Matrix");
        wsOutDate.Cell(1, 1).Value = "Header";
        wsOutDate.Cell(2, 1).Value = $"Chi nhánh: Kho Tổng | Mã CN: {branchId}";
        wsOutDate.Cell(3, 1).Value = $"Nhà cung cấp: NCC A | Mã NCC: {supplierId}";
        wsOutDate.Cell(4, 1).Value = "Thời gian: 20/11/2026 - 26/11/2026";
        wsOutDate.Cell(5, 1).Value = "STT";
        wsOutDate.Cell(5, 2).Value = "Mã sản phẩm";
        wsOutDate.Cell(5, 3).Value = "Tên sản phẩm";
        wsOutDate.Cell(5, 4).Value = "[2024-01-01]"; // outside Nov 2026
        wsOutDate.Cell(6, 1).Value = 1;
        wsOutDate.Cell(6, 2).Value = "SKU-01";
        wsOutDate.Cell(6, 3).Value = "Sản phẩm A";
        wsOutDate.Cell(6, 4).Value = 5;
        using var outDateMs = new MemoryStream();
        outDateWb.SaveAs(outDateMs);
        outDateMs.Position = 0;

        var res5 = await service.PreviewAsync(outDateMs, query, CancellationToken.None);
        Assert.False(res5.Valid);
        Assert.Contains(res5.Errors, e => e.Code == "INVALID_PERIOD");
    }
}
