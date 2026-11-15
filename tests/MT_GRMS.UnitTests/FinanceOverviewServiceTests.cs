using MT_GRMS.Application.FinanceOverview;
using MT_GRMS.Application.FinanceOverview.Dtos;
using MT_GRMS.Domain.Exceptions;

namespace MT_GRMS.UnitTests;

public sealed class FinanceOverviewServiceTests
{
    private sealed class FakeRepository : IFinanceOverviewRepository
    {
        public int CallCount { get; private set; }
        public Guid? BranchId { get; private set; }
        public DateTime FromUtc { get; private set; }
        public DateTime ToUtc { get; private set; }

        public Task<FinanceOverviewResponse> GetOverviewAsync(
            Guid? branchId,
            DateTime fromUtc,
            DateTime toUtc,
            CancellationToken ct = default)
        {
            CallCount++;
            BranchId = branchId;
            FromUtc = fromUtc;
            ToUtc = toUtc;
            return Task.FromResult(new FinanceOverviewResponse(
                new FinanceOverviewScopeDto(branchId, "Test", 1),
                new FinanceOverviewPeriodDto(fromUtc, toUtc, "Asia/Ho_Chi_Minh"),
                toUtc,
                DateTime.UtcNow,
                new FinanceOverviewTotalsDto(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
                new FinanceAttentionDto(0, 0, 0, 0, 0, 0, true),
                Array.Empty<BranchCashHealthDto>()));
        }
    }

    [Fact]
    public async Task GetOverviewAsync_WithValidUtcPeriod_PreservesScopeAndPeriod()
    {
        var repository = new FakeRepository();
        var service = new FinanceOverviewService(repository);
        var branchId = Guid.NewGuid();
        var from = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 11, 15, 23, 59, 59, DateTimeKind.Utc);

        await service.GetOverviewAsync(branchId, from, to);

        Assert.Equal(1, repository.CallCount);
        Assert.Equal(branchId, repository.BranchId);
        Assert.Equal(from, repository.FromUtc);
        Assert.Equal(to, repository.ToUtc);
    }

    [Fact]
    public async Task GetOverviewAsync_WithLocalDates_RejectsAmbiguousTimeZone()
    {
        var repository = new FakeRepository();
        var service = new FinanceOverviewService(repository);

        await Assert.ThrowsAsync<BusinessException>(() => service.GetOverviewAsync(
            null,
            new DateTime(2026, 11, 1),
            new DateTime(2026, 11, 15)));

        Assert.Equal(0, repository.CallCount);
    }

    [Fact]
    public async Task GetOverviewAsync_WhenStartAfterEnd_RejectsPeriod()
    {
        var repository = new FakeRepository();
        var service = new FinanceOverviewService(repository);
        var from = new DateTime(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc);

        await Assert.ThrowsAsync<BusinessException>(() => service.GetOverviewAsync(null, from, to));
        Assert.Equal(0, repository.CallCount);
    }

    [Fact]
    public async Task GetOverviewAsync_WhenRangeExceeds366Days_RejectsExpensiveQuery()
    {
        var repository = new FakeRepository();
        var service = new FinanceOverviewService(repository);
        var from = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);

        await Assert.ThrowsAsync<BusinessException>(() => service.GetOverviewAsync(null, from, to));
        Assert.Equal(0, repository.CallCount);
    }
}
