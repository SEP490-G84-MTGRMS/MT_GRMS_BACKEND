using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Auth;
using MT_GRMS.Application.Tax.Dtos;
using MT_GRMS.Domain.Entities.Identity;
using MT_GRMS.Domain.Exceptions;
using MT_GRMS.Infrastructure.Persistence;
using MT_GRMS.Infrastructure.Services;
using Xunit;

namespace MT_GRMS.UnitTests;

public class HouseholdTaxPinManagementTests
{
    private readonly IPasswordHasher _passwordHasher = new PasswordHasher();

    private AppDbContext CreateDbContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        return new AppDbContext(options);
    }

    private User CreateStoreOwnerUser(string userId = "owner-01", string username = "store_owner", string rawPassword = "password123")
    {
        return new User
        {
            Id = userId,
            UserName = username,
            FullName = "Chủ Hộ Kinh Doanh",
            Email = "owner@test.com",
            PasswordHash = _passwordHasher.HashPassword(rawPassword),
            IsActive = true
        };
    }

    [Fact]
    public async Task ChangeSecurityPin_InitialSet_WithoutPassword_ThrowsPasswordRequired()
    {
        using var context = CreateDbContext(Guid.NewGuid().ToString());
        var user = CreateStoreOwnerUser();
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var pinService = new PinVerificationService(context, _passwordHasher);

        var request = new ChangeSecurityPinRequest
        {
            CurrentPassword = "",
            NewPin = "123456"
        };

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            pinService.ChangeSecurityPinAsync(user.Id, request));

        Assert.Equal("PASSWORD_REQUIRED", ex.ErrorCode);
    }

    [Fact]
    public async Task ChangeSecurityPin_InitialSet_WithWrongPassword_ThrowsInvalidPassword()
    {
        using var context = CreateDbContext(Guid.NewGuid().ToString());
        var user = CreateStoreOwnerUser(rawPassword: "CorrectPassword123!");
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var pinService = new PinVerificationService(context, _passwordHasher);

        var request = new ChangeSecurityPinRequest
        {
            CurrentPassword = "WrongPassword999!",
            NewPin = "123456"
        };

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            pinService.ChangeSecurityPinAsync(user.Id, request));

        Assert.Equal("INVALID_PASSWORD", ex.ErrorCode);
    }

    [Fact]
    public async Task ChangeSecurityPin_InitialSet_WithValidPassword_SetsPinSuccessfully()
    {
        using var context = CreateDbContext(Guid.NewGuid().ToString());
        var user = CreateStoreOwnerUser(rawPassword: "MyPassword2026!");
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var pinService = new PinVerificationService(context, _passwordHasher);

        var request = new ChangeSecurityPinRequest
        {
            CurrentPassword = "MyPassword2026!",
            NewPin = "654321"
        };

        await pinService.ChangeSecurityPinAsync(user.Id, request);

        // Assert: User đã có PIN hash & salt
        var updated = await context.Users.FirstAsync(u => u.Id == user.Id);
        Assert.NotNull(updated.SecurityPinHash);
        Assert.NotNull(updated.SecurityPinSalt);
        Assert.Equal(0, updated.FailedPinAttempts);
        Assert.Null(updated.PinLockedUntilUtc);

        // Xác thực PIN mới thành công
        var verifyResult = await pinService.VerifyStoreOwnerPinAsync(user.Id, "654321");
        Assert.True(verifyResult.IsSuccess);
    }

    [Theory]
    [InlineData("")]
    [InlineData("123")] // Quá ngắn (< 4)
    [InlineData("1234567")] // Quá dài (> 6)
    [InlineData("12ab")] // Chứa chữ cái
    [InlineData("abcd")]
    public async Task ChangeSecurityPin_WithInvalidPinFormat_ThrowsInvalidPinFormat(string invalidPin)
    {
        using var context = CreateDbContext(Guid.NewGuid().ToString());
        var user = CreateStoreOwnerUser();
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var pinService = new PinVerificationService(context, _passwordHasher);

        var request = new ChangeSecurityPinRequest
        {
            CurrentPassword = "password123",
            NewPin = invalidPin
        };

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            pinService.ChangeSecurityPinAsync(user.Id, request));

        Assert.Equal("INVALID_PIN_FORMAT", ex.ErrorCode);
    }

    [Fact]
    public async Task ChangeSecurityPin_ExistingPin_WithWrongCurrentPin_ThrowsInvalidCurrentPin()
    {
        using var context = CreateDbContext(Guid.NewGuid().ToString());
        var user = CreateStoreOwnerUser();
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var pinService = new PinVerificationService(context, _passwordHasher);
        await pinService.SetStoreOwnerPinAsync(user.Id, "111111");

        var request = new ChangeSecurityPinRequest
        {
            CurrentPin = "999999", // Sai PIN
            NewPin = "222222"
        };

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            pinService.ChangeSecurityPinAsync(user.Id, request));

        Assert.Equal("INVALID_PIN", ex.ErrorCode);
    }

    [Fact]
    public async Task ChangeSecurityPin_ExistingPin_WithCorrectCurrentPin_ChangesPinSuccessfully()
    {
        using var context = CreateDbContext(Guid.NewGuid().ToString());
        var user = CreateStoreOwnerUser();
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var pinService = new PinVerificationService(context, _passwordHasher);
        await pinService.SetStoreOwnerPinAsync(user.Id, "111111");

        var request = new ChangeSecurityPinRequest
        {
            CurrentPin = "111111", // Đúng PIN cũ
            NewPin = "888888"
        };

        await pinService.ChangeSecurityPinAsync(user.Id, request);

        // PIN cũ không còn hợp lệ
        var oldVerify = await pinService.VerifyStoreOwnerPinAsync(user.Id, "111111");
        Assert.False(oldVerify.IsSuccess);

        // PIN mới hợp lệ
        var newVerify = await pinService.VerifyStoreOwnerPinAsync(user.Id, "888888");
        Assert.True(newVerify.IsSuccess);
    }

    [Fact]
    public async Task ChangeSecurityPin_ExistingPin_WithPassword_ChangesPinSuccessfully()
    {
        using var context = CreateDbContext(Guid.NewGuid().ToString());
        var user = CreateStoreOwnerUser(rawPassword: "Password@2026");
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var pinService = new PinVerificationService(context, _passwordHasher);
        await pinService.SetStoreOwnerPinAsync(user.Id, "111111");

        // Đổi PIN bằng mật khẩu thay vì PIN cũ
        var request = new ChangeSecurityPinRequest
        {
            CurrentPassword = "Password@2026",
            NewPin = "999999"
        };

        await pinService.ChangeSecurityPinAsync(user.Id, request);

        var verify = await pinService.VerifyStoreOwnerPinAsync(user.Id, "999999");
        Assert.True(verify.IsSuccess);
    }

    [Fact]
    public async Task ChangeSecurityPin_ExistingPin_WithoutCredentials_ThrowsCredentialRequired()
    {
        using var context = CreateDbContext(Guid.NewGuid().ToString());
        var user = CreateStoreOwnerUser();
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var pinService = new PinVerificationService(context, _passwordHasher);
        await pinService.SetStoreOwnerPinAsync(user.Id, "111111");

        var request = new ChangeSecurityPinRequest
        {
            NewPin = "777777" // Thiếu cả CurrentPin và CurrentPassword
        };

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            pinService.ChangeSecurityPinAsync(user.Id, request));

        Assert.Equal("CREDENTIAL_REQUIRED", ex.ErrorCode);
    }

    [Fact]
    public async Task ResetSecurityPin_WithoutPassword_ThrowsPasswordRequired()
    {
        using var context = CreateDbContext(Guid.NewGuid().ToString());
        var user = CreateStoreOwnerUser();
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var pinService = new PinVerificationService(context, _passwordHasher);

        var request = new ResetSecurityPinRequest
        {
            Password = "",
            NewPin = "123456"
        };

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            pinService.ResetSecurityPinAsync(user.Id, request));

        Assert.Equal("PASSWORD_REQUIRED", ex.ErrorCode);
    }

    [Fact]
    public async Task ResetSecurityPin_WithWrongPassword_ThrowsInvalidPassword()
    {
        using var context = CreateDbContext(Guid.NewGuid().ToString());
        var user = CreateStoreOwnerUser(rawPassword: "OwnerSecretPassword!");
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var pinService = new PinVerificationService(context, _passwordHasher);

        var request = new ResetSecurityPinRequest
        {
            Password = "WrongPassword!",
            NewPin = "123456"
        };

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            pinService.ResetSecurityPinAsync(user.Id, request));

        Assert.Equal("INVALID_PASSWORD", ex.ErrorCode);
    }

    [Fact]
    public async Task ResetSecurityPin_WhenLocked_ResetsPinAndClearsLockout()
    {
        using var context = CreateDbContext(Guid.NewGuid().ToString());
        var user = CreateStoreOwnerUser(rawPassword: "OwnerSecretPassword!");
        user.FailedPinAttempts = 5;
        user.PinLockedUntilUtc = DateTime.UtcNow.AddMinutes(15);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var pinService = new PinVerificationService(context, _passwordHasher);

        var request = new ResetSecurityPinRequest
        {
            Password = "OwnerSecretPassword!",
            NewPin = "555555"
        };

        await pinService.ResetSecurityPinAsync(user.Id, request);

        // Assert: Trạng thái khóa và số lần nhập sai đã được xóa sạch
        var updated = await context.Users.FirstAsync(u => u.Id == user.Id);
        Assert.Equal(0, updated.FailedPinAttempts);
        Assert.Null(updated.PinLockedUntilUtc);

        // Có thể đăng nhập và xác thực ngay lập tức bằng PIN mới
        var verify = await pinService.VerifyStoreOwnerPinAsync(user.Id, "555555");
        Assert.True(verify.IsSuccess);
    }

    [Fact]
    public async Task VerifyStoreOwnerPin_WhenPinNotSet_ReturnsPinNotSet()
    {
        using var context = CreateDbContext(Guid.NewGuid().ToString());
        var user = CreateStoreOwnerUser();
        // Không set PIN
        user.SecurityPinHash = null;
        user.SecurityPinSalt = null;
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var pinService = new PinVerificationService(context, _passwordHasher);

        var result = await pinService.VerifyStoreOwnerPinAsync(user.Id, "123456");

        Assert.False(result.IsSuccess);
        Assert.Equal("PIN_NOT_SET", result.ErrorCode);
    }

    [Fact]
    public async Task VerifyStoreOwnerPin_WhenUserInactive_ReturnsUserInactive()
    {
        using var context = CreateDbContext(Guid.NewGuid().ToString());
        var user = CreateStoreOwnerUser();
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var pinService = new PinVerificationService(context, _passwordHasher);
        await pinService.SetStoreOwnerPinAsync(user.Id, "123456");

        // Vô hiệu hóa tài khoản sau khi đã có PIN
        user.IsActive = false;
        await context.SaveChangesAsync();

        var result = await pinService.VerifyStoreOwnerPinAsync(user.Id, "123456");
        Assert.False(result.IsSuccess);
        Assert.Equal("USER_INACTIVE", result.ErrorCode);
    }

    [Fact]
    public async Task ChangeSecurityPin_WhenUserInactive_ThrowsUserInactive()
    {
        using var context = CreateDbContext(Guid.NewGuid().ToString());
        var user = CreateStoreOwnerUser();
        user.IsActive = false;
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var pinService = new PinVerificationService(context, _passwordHasher);

        var request = new ChangeSecurityPinRequest
        {
            CurrentPassword = "password123",
            NewPin = "654321"
        };

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            pinService.ChangeSecurityPinAsync(user.Id, request));

        Assert.Equal("USER_INACTIVE", ex.ErrorCode);
    }

    [Fact]
    public async Task ResetSecurityPin_WhenUserInactive_ThrowsUserInactive()
    {
        using var context = CreateDbContext(Guid.NewGuid().ToString());
        var user = CreateStoreOwnerUser();
        user.IsActive = false;
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var pinService = new PinVerificationService(context, _passwordHasher);

        var request = new ResetSecurityPinRequest
        {
            Password = "password123",
            NewPin = "654321"
        };

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            pinService.ResetSecurityPinAsync(user.Id, request));

        Assert.Equal("USER_INACTIVE", ex.ErrorCode);
    }

    [Fact]
    public async Task ResetSecurityPin_NonAdminTargetsAnotherUser_ThrowsForbidden()
    {
        using var context = CreateDbContext(Guid.NewGuid().ToString());
        var caller = CreateStoreOwnerUser(userId: "owner-01", username: "store_owner_01");
        caller.IsAdmin = false;

        var targetUser = CreateStoreOwnerUser(userId: "owner-02", username: "store_owner_02", rawPassword: "TargetSecret123!");
        context.Users.AddRange(caller, targetUser);
        await context.SaveChangesAsync();

        var pinService = new PinVerificationService(context, _passwordHasher);

        // Caller cố tình truyền UserId của targetUser
        var request = new ResetSecurityPinRequest
        {
            UserId = targetUser.Id,
            Password = "TargetSecret123!",
            NewPin = "999999"
        };

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            pinService.ResetSecurityPinAsync(caller.Id, request));

        Assert.Equal("FORBIDDEN", ex.ErrorCode);
    }

    [Fact]
    public async Task ResetSecurityPin_AdminTargetsAnotherUser_ThrowsForbidden()
    {
        using var context = CreateDbContext(Guid.NewGuid().ToString());
        var admin = CreateStoreOwnerUser(userId: "admin-01", username: "system_admin");
        admin.IsAdmin = true;

        var targetUser = CreateStoreOwnerUser(userId: "owner-03", username: "store_owner_03", rawPassword: "TargetSecret123!");
        context.Users.AddRange(admin, targetUser);
        await context.SaveChangesAsync();

        var pinService = new PinVerificationService(context, _passwordHasher);

        var request = new ResetSecurityPinRequest
        {
            UserId = targetUser.Id,
            Password = "TargetSecret123!",
            NewPin = "888888"
        };

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            pinService.ResetSecurityPinAsync(admin.Id, request));

        Assert.Equal("FORBIDDEN", ex.ErrorCode);
    }
}
