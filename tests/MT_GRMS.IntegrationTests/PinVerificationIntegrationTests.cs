using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Tax.Dtos;
using MT_GRMS.Application.Tax.Security;
using MT_GRMS.Domain.Entities.Identity;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Domain.Exceptions;
using MT_GRMS.Infrastructure.Persistence;
using MT_GRMS.Infrastructure.Services;
using Xunit;

namespace MT_GRMS.IntegrationTests;

public class PinVerificationIntegrationTests
{
    private AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"MT_GRMS_PinIntTestDb_{Guid.NewGuid()}")
            .Options;

        return new AppDbContext(options);
    }

    [Fact]
    public async Task SetPin_And_VerifyPin_CorrectPin_ShouldSucceed()
    {
        // Arrange
        using var context = CreateDbContext();
        var user = new User { Id = "usr-owner-01", Username = "owner01" };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var service = new PinVerificationService(context);

        // Act: Đặt PIN là 123456
        await service.SetStoreOwnerPinAsync("usr-owner-01", "123456");

        // Verify với đúng PIN
        var result = await service.VerifyStoreOwnerPinAsync("usr-owner-01", "123456");

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(0, user.FailedPinAttempts);
        Assert.Null(user.PinLockedUntilUtc);
    }

    [Fact]
    public async Task VerifyPin_WrongPin_ShouldIncrementFailedAttempts()
    {
        // Arrange
        using var context = CreateDbContext();
        var user = new User { Id = "usr-owner-02", Username = "owner02" };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var service = new PinVerificationService(context);
        await service.SetStoreOwnerPinAsync("usr-owner-02", "888888");

        // Act: Nhập sai PIN
        var result = await service.VerifyStoreOwnerPinAsync("usr-owner-02", "111111");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("INVALID_PIN", result.ErrorCode);
        Assert.Equal(4, result.RemainingAttempts); // Còn 4 lần thử
        Assert.Equal(1, user.FailedPinAttempts);
    }

    [Fact]
    public async Task VerifyPin_FiveConsecutiveFails_ShouldLockAccountFor15Minutes()
    {
        // Arrange
        using var context = CreateDbContext();
        var user = new User { Id = "usr-owner-03", Username = "owner03" };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var service = new PinVerificationService(context);
        await service.SetStoreOwnerPinAsync("usr-owner-03", "999999");

        // Act: Nhập sai 5 lần liên tiếp
        for (int i = 0; i < 4; i++)
        {
            var res = await service.VerifyStoreOwnerPinAsync("usr-owner-03", "000000");
            Assert.False(res.IsSuccess);
            Assert.Equal("INVALID_PIN", res.ErrorCode);
        }

        // Lần thứ 5
        var fifthResult = await service.VerifyStoreOwnerPinAsync("usr-owner-03", "000000");

        // Assert: Bị khóa tạm thời
        Assert.False(fifthResult.IsSuccess);
        Assert.Equal("PIN_LOCKED", fifthResult.ErrorCode);
        Assert.Equal(5, user.FailedPinAttempts);
        Assert.NotNull(user.PinLockedUntilUtc);
        Assert.True(user.PinLockedUntilUtc > DateTime.UtcNow.AddMinutes(14));

        // Thử lần thứ 6 dù nhập đúng PIN vẫn bị từ chối
        var sixthResult = await service.VerifyStoreOwnerPinAsync("usr-owner-03", "999999");
        Assert.False(sixthResult.IsSuccess);
        Assert.Equal("PIN_LOCKED", sixthResult.ErrorCode);
    }

    [Fact]
    public async Task VerifyPin_ResetFailedAttempts_WhenEnteredCorrectly()
    {
        // Arrange
        using var context = CreateDbContext();
        var user = new User { Id = "usr-owner-04", Username = "owner04" };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var service = new PinVerificationService(context);
        await service.SetStoreOwnerPinAsync("usr-owner-04", "654321");

        // Nhập sai 2 lần
        await service.VerifyStoreOwnerPinAsync("usr-owner-04", "000000");
        await service.VerifyStoreOwnerPinAsync("usr-owner-04", "000000");
        Assert.Equal(2, user.FailedPinAttempts);

        // Nhập đúng ở lần thứ 3 -> counter reset về 0
        var correctResult = await service.VerifyStoreOwnerPinAsync("usr-owner-04", "654321");
        Assert.True(correctResult.IsSuccess);
        Assert.Equal(0, user.FailedPinAttempts);
    }
}
