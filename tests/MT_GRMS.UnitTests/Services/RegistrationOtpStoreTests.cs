using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Auth;
using MT_GRMS.Application.Common;
using MT_GRMS.Infrastructure.Persistence;
using MT_GRMS.Infrastructure.Services;
using Xunit;

namespace MT_GRMS.UnitTests.Services;

public class RegistrationOtpStoreTests
{
    private class FakeTenantContext : ITenantContext
    {
        public string CurrentTenantId { get; set; } = "tenant-01";
        public Guid CurrentUserId { get; set; } = Guid.NewGuid();
        public Guid? CurrentBranchId { get; set; } = null;
        public string CurrentRole { get; set; } = "ADMIN";
        public void SetContext(string tenantId, Guid userId, Guid? branchId) { }
        public void SetRole(string? role) { }
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"RegOtpDb_{Guid.NewGuid()}")
            .Options;
        return new AppDbContext(options, new FakeTenantContext());
    }

    [Fact]
    public async Task RegistrationOtpStore_Lifecycle_InMemory_ShouldWork()
    {
        using var db = CreateDbContext();
        var store = new RegistrationOtpStore(db);
        var cancellationToken = CancellationToken.None;

        var email = "register@testmart.vn";
        var challengeId1 = "chal-01";
        var now = DateTime.UtcNow;

        var state1 = new RegistrationOtpState(
            ChallengeId: challengeId1,
            NormalizedEmail: email,
            OtpHash: "hash123",
            Intent: "REGISTER",
            RequestFingerprint: "fp123",
            State: RegistrationOtpStates.Pending,
            ReservedByIdempotencyKey: null,
            ExpiresAtUtc: now.AddMinutes(5),
            FailedAttempts: 0
        );

        // 1. Create first challenge
        await store.CreateAsync(state1, cancellationToken);

        // 2. GetAsync by challengeId
        var retrieved1 = await store.GetAsync(challengeId1, email, cancellationToken);
        Assert.NotNull(retrieved1);
        Assert.Equal(challengeId1, retrieved1!.ChallengeId);
        Assert.Equal(RegistrationOtpStates.Pending, retrieved1.State);

        // 3. Create second challenge for same email (cleans up previous pending)
        var challengeId2 = "chal-02";
        var state2 = new RegistrationOtpState(
            ChallengeId: challengeId2,
            NormalizedEmail: email,
            OtpHash: "hash456",
            Intent: "REGISTER",
            RequestFingerprint: "fp456",
            State: RegistrationOtpStates.Pending,
            ReservedByIdempotencyKey: null,
            ExpiresAtUtc: now.AddMinutes(5),
            FailedAttempts: 0
        );
        await store.CreateAsync(state2, cancellationToken);

        // Old challenge was removed
        var oldRetrieved = await store.GetAsync(challengeId1, email, cancellationToken);
        Assert.Null(oldRetrieved);

        // GetAsync by email without challengeId finds latest
        var byEmail = await store.GetAsync(null, email, cancellationToken);
        Assert.NotNull(byEmail);
        Assert.Equal(challengeId2, byEmail!.ChallengeId);

        // 4. TryReserveAsync:
        // Non-existent key -> false
        Assert.False(await store.TryReserveAsync("non-existent", "idem-1", now, cancellationToken));

        // Expired key -> false
        var expiredState = new RegistrationOtpState("chal-expired", "exp@test.com", "h", "I", "fp", RegistrationOtpStates.Pending, null, now.AddMinutes(-1), 0);
        await store.CreateAsync(expiredState, cancellationToken);
        Assert.False(await store.TryReserveAsync("chal-expired", "idem-1", now, cancellationToken));

        // Reserve valid key -> true
        var reserved = await store.TryReserveAsync(challengeId2, "idem-1", now, cancellationToken);
        Assert.True(reserved);

        // Reserve again with same idempotency key -> true
        Assert.True(await store.TryReserveAsync(challengeId2, "idem-1", now, cancellationToken));

        // Reserve again with different idempotency key -> false
        Assert.False(await store.TryReserveAsync(challengeId2, "idem-2", now, cancellationToken));

        // 5. ReleaseReservationAsync:
        // Release with wrong key -> still reserved
        await store.ReleaseReservationAsync(challengeId2, "wrong-idem", now, cancellationToken);
        Assert.False(await store.TryReserveAsync(challengeId2, "idem-2", now, cancellationToken));

        // Release with correct key -> becomes pending again
        await store.ReleaseReservationAsync(challengeId2, "idem-1", now, cancellationToken);
        var reReserved = await store.TryReserveAsync(challengeId2, "idem-2", now, cancellationToken);
        Assert.True(reReserved);

        // 6. ConsumeAsync:
        // Consume with correct key -> marks Consumed
        await store.ConsumeAsync(challengeId2, "idem-2", cancellationToken);
        var consumed = await store.GetAsync(challengeId2, email, cancellationToken);
        Assert.NotNull(consumed);
        Assert.Equal(RegistrationOtpStates.Consumed, consumed!.State);

        // GetAsync by email ignores Consumed
        var noneActive = await store.GetAsync(null, email, cancellationToken);
        Assert.Null(noneActive);

        // 7. IncrementFailureOrRemoveAsync:
        var challengeId3 = "chal-03";
        var state3 = new RegistrationOtpState(challengeId3, "fail@test.com", "h", "I", "fp", RegistrationOtpStates.Pending, null, now.AddMinutes(5), 0);
        await store.CreateAsync(state3, cancellationToken);

        Assert.Equal(3, await store.IncrementFailureOrRemoveAsync("missing-chal", 3, cancellationToken));

        var attempts1 = await store.IncrementFailureOrRemoveAsync(challengeId3, 3, cancellationToken);
        Assert.Equal(1, attempts1);
        var attempts2 = await store.IncrementFailureOrRemoveAsync(challengeId3, 3, cancellationToken);
        Assert.Equal(2, attempts2);
        var attempts3 = await store.IncrementFailureOrRemoveAsync(challengeId3, 3, cancellationToken);
        Assert.Equal(3, attempts3);

        // Exceeded maxAttempts, should be removed
        Assert.Null(await store.GetAsync(challengeId3, "fail@test.com", cancellationToken));

        // 8. RemoveAsync:
        var challengeId4 = "chal-04";
        var state4 = new RegistrationOtpState(challengeId4, "rem@test.com", "h", "I", "fp", RegistrationOtpStates.Pending, null, now.AddMinutes(5), 0);
        await store.CreateAsync(state4, cancellationToken);
        Assert.NotNull(await store.GetAsync(challengeId4, "rem@test.com", cancellationToken));

        await store.RemoveAsync(challengeId4, cancellationToken);
        Assert.Null(await store.GetAsync(challengeId4, "rem@test.com", cancellationToken));
    }
}
