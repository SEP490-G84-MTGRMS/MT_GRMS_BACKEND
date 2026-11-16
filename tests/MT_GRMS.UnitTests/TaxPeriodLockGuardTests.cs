using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using MT_GRMS.Application.Cashbook;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Expenses;
using MT_GRMS.Application.Expenses.Dtos;
using MT_GRMS.Application.PosOrders;
using MT_GRMS.Application.PosOrders.Dtos;
using MT_GRMS.Application.Tax;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Domain.Exceptions;
using MT_GRMS.Infrastructure.Services.Tax;
using Xunit;

namespace MT_GRMS.UnitTests;

public class TaxPeriodLockGuardTests
{
    private readonly Mock<IHouseholdTaxRepository> _taxRepoMock;
    private readonly TaxPeriodLockGuard _guard;
    private readonly List<HouseholdTaxSnapshot> _snapshots;

    public TaxPeriodLockGuardTests()
    {
        _snapshots = new List<HouseholdTaxSnapshot>();
        _taxRepoMock = new Mock<IHouseholdTaxRepository>();

        _taxRepoMock.Setup(r => r.GetSnapshotsAsync(It.IsAny<int?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int? year, Guid? branchId, CancellationToken ct) =>
            {
                var query = _snapshots.AsQueryable();
                if (year.HasValue)
                {
                    query = query.Where(s => s.Year == year.Value);
                }
                if (branchId.HasValue)
                {
                    query = query.Where(s => s.BranchId == branchId.Value);
                }
                return query.ToList();
            });

        _guard = new TaxPeriodLockGuard(_taxRepoMock.Object);
    }

    [Fact]
    public async Task IsPeriodFinalizedAsync_WhenMonthFinalizedTenantWide_ReturnsTrueForMatchingDate()
    {
        // Arrange: Tháng 3/2026 đã chốt ở cấp hộ (BranchId == null)
        _snapshots.Add(new HouseholdTaxSnapshot
        {
            Year = 2026,
            PeriodType = TaxPeriodType.Month,
            PeriodNumber = 3,
            BranchId = null
        });

        var dateInMarch = new DateTime(2026, 3, 15, 10, 0, 0, DateTimeKind.Utc);

        // Act
        var isLocked = await _guard.IsPeriodFinalizedAsync(dateInMarch);

        // Assert
        Assert.True(isLocked);
    }

    [Fact]
    public async Task IsPeriodFinalizedAsync_WhenMonthFinalizedTenantWide_ReturnsFalseForDifferentMonth()
    {
        // Arrange: Tháng 3/2026 chốt, nhưng kiểm tra ngày trong Tháng 4/2026
        _snapshots.Add(new HouseholdTaxSnapshot
        {
            Year = 2026,
            PeriodType = TaxPeriodType.Month,
            PeriodNumber = 3,
            BranchId = null
        });

        var dateInApril = new DateTime(2026, 4, 15, 10, 0, 0, DateTimeKind.Utc);

        // Act
        var isLocked = await _guard.IsPeriodFinalizedAsync(dateInApril);

        // Assert
        Assert.False(isLocked);
    }

    [Fact]
    public async Task IsPeriodFinalizedAsync_WhenQuarterFinalizedTenantWide_LocksAllDatesInQuarter()
    {
        // Arrange: Quý 2/2026 chốt (Tháng 4, 5, 6)
        _snapshots.Add(new HouseholdTaxSnapshot
        {
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 2,
            BranchId = null
        });

        var dateApril = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
        var dateMay = new DateTime(2026, 5, 20, 12, 0, 0, DateTimeKind.Utc);
        var dateJune = new DateTime(2026, 6, 30, 12, 0, 0, DateTimeKind.Utc);
        var dateJuly = new DateTime(2026, 7, 1, 10, 0, 0, DateTimeKind.Utc);

        // Act & Assert
        Assert.True(await _guard.IsPeriodFinalizedAsync(dateApril));
        Assert.True(await _guard.IsPeriodFinalizedAsync(dateMay));
        Assert.True(await _guard.IsPeriodFinalizedAsync(dateJune));
        Assert.False(await _guard.IsPeriodFinalizedAsync(dateJuly));
    }

    [Fact]
    public async Task IsPeriodFinalizedAsync_WhenYearFinalizedTenantWide_LocksAnyDateInYear()
    {
        // Arrange: Năm 2026 đã chốt toàn bộ
        _snapshots.Add(new HouseholdTaxSnapshot
        {
            Year = 2026,
            PeriodType = TaxPeriodType.Year,
            PeriodNumber = 1,
            BranchId = null
        });

        var dateIn2026 = new DateTime(2026, 8, 15, 10, 0, 0, DateTimeKind.Utc);
        var dateIn2027 = new DateTime(2027, 1, 5, 10, 0, 0, DateTimeKind.Utc);

        // Act & Assert
        Assert.True(await _guard.IsPeriodFinalizedAsync(dateIn2026));
        Assert.False(await _guard.IsPeriodFinalizedAsync(dateIn2027));
    }

    [Fact]
    public async Task ValidateTaxPeriodUnlockedAsync_WhenLocked_ThrowsBusinessExceptionWithVietnameseMessage()
    {
        // Arrange
        _snapshots.Add(new HouseholdTaxSnapshot
        {
            Year = 2026,
            PeriodType = TaxPeriodType.Month,
            PeriodNumber = 3,
            BranchId = null
        });

        var dateInMarch = new DateTime(2026, 3, 20, 10, 0, 0, DateTimeKind.Utc);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            _guard.ValidateTaxPeriodUnlockedAsync(dateInMarch, "hủy hóa đơn bán lẻ"));

        Assert.Equal("TAX_PERIOD_FINALIZED", ex.ErrorCode);
        Assert.Contains("Tháng 3/2026", ex.Message);
        Assert.Contains("hủy hóa đơn bán lẻ", ex.Message);
        Assert.Contains("điều chỉnh ở kỳ tiếp theo", ex.Message);
    }

    [Fact]
    public async Task ValidateTaxPeriodUnlockedAsync_WhenUnlocked_DoesNotThrow()
    {
        // Arrange: Không có snapshot nào
        var date = new DateTime(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc);

        // Act & Assert: Không ném lỗi
        await _guard.ValidateTaxPeriodUnlockedAsync(date, "hủy hóa đơn bán lẻ");
    }

    [Fact]
    public async Task BranchScope_TenantWideSnapshot_LocksAllBranches()
    {
        // Arrange: Kỳ Tháng 1/2026 chốt Tenant-wide (BranchId == null)
        _snapshots.Add(new HouseholdTaxSnapshot
        {
            Year = 2026,
            PeriodType = TaxPeriodType.Month,
            PeriodNumber = 1,
            BranchId = null
        });

        var branchA = Guid.NewGuid();
        var branchB = Guid.NewGuid();
        var date = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);

        // Act & Assert: Cả 2 chi nhánh đều bị khóa
        Assert.True(await _guard.IsPeriodFinalizedAsync(date, branchA));
        Assert.True(await _guard.IsPeriodFinalizedAsync(date, branchB));
        Assert.True(await _guard.IsPeriodFinalizedAsync(date, null));
    }

    [Fact]
    public async Task BranchScope_WhenPeriodFinalized_LocksAllBranchesRegardlessOfBranchId()
    {
        // Arrange: Kỳ Tháng 1/2026 đã chốt. Theo quy định toàn hộ kinh doanh, tất cả chi nhánh đều bị khóa.
        var branchA = Guid.NewGuid();
        var branchB = Guid.NewGuid();

        _snapshots.Add(new HouseholdTaxSnapshot
        {
            Year = 2026,
            PeriodType = TaxPeriodType.Month,
            PeriodNumber = 1,
            BranchId = branchA
        });

        var date = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);

        // Act & Assert: Chốt sổ kỳ thuế là toàn bộ hộ kinh doanh (Tenant-wide) - mọi chi nhánh (A, B, null) đều bị khóa
        Assert.True(await _guard.IsPeriodFinalizedAsync(date, branchA));
        Assert.True(await _guard.IsPeriodFinalizedAsync(date, branchB));
        Assert.True(await _guard.IsPeriodFinalizedAsync(date, null));
    }

    [Fact]
    public async Task TimezoneBoundary_AccuratelyConvertsUtcToVietnamTime()
    {
        // Arrange: Tháng 3/2026 chốt
        _snapshots.Add(new HouseholdTaxSnapshot
        {
            Year = 2026,
            PeriodType = TaxPeriodType.Month,
            PeriodNumber = 3,
            BranchId = null
        });

        // 31/03/2026 16:59:59 UTC = 31/03/2026 23:59:59 VN (vẫn trong Tháng 3) -> KHÓA
        var endOfMarchUtc = new DateTime(2026, 3, 31, 16, 59, 59, DateTimeKind.Utc);
        // 31/03/2026 17:00:00 UTC = 01/04/2026 00:00:00 VN (sang Tháng 4) -> MỞ
        var startOfAprilUtc = new DateTime(2026, 3, 31, 17, 0, 0, DateTimeKind.Utc);

        // Act & Assert
        Assert.True(await _guard.IsPeriodFinalizedAsync(endOfMarchUtc));
        Assert.False(await _guard.IsPeriodFinalizedAsync(startOfAprilUtc));
    }

    [Fact]
    public async Task TimezoneBoundary_QuarterEnd_AccuratelyConvertsUtcToVietnamTime()
    {
        // Arrange: Quý 2/2026 chốt (Tháng 4, 5, 6)
        _snapshots.Add(new HouseholdTaxSnapshot
        {
            Year = 2026,
            PeriodType = TaxPeriodType.Quarter,
            PeriodNumber = 2,
            BranchId = null
        });

        // 30/06/2026 16:59:59 UTC = 30/06/2026 23:59:59 VN (vẫn trong Quý 2) -> KHÓA
        var endOfQ2Utc = new DateTime(2026, 6, 30, 16, 59, 59, DateTimeKind.Utc);
        // 30/06/2026 17:00:00 UTC = 01/07/2026 00:00:00 VN (sang Quý 3) -> MỞ
        var startOfQ3Utc = new DateTime(2026, 6, 30, 17, 0, 0, DateTimeKind.Utc);

        // Act & Assert
        Assert.True(await _guard.IsPeriodFinalizedAsync(endOfQ2Utc));
        Assert.False(await _guard.IsPeriodFinalizedAsync(startOfQ3Utc));
    }

    [Fact]
    public async Task TimezoneBoundary_YearEnd_AccuratelyConvertsUtcToVietnamTime()
    {
        // Arrange: Năm 2026 chốt
        _snapshots.Add(new HouseholdTaxSnapshot
        {
            Year = 2026,
            PeriodType = TaxPeriodType.Year,
            PeriodNumber = 1,
            BranchId = null
        });

        // 31/12/2026 16:59:59 UTC = 31/12/2026 23:59:59 VN (vẫn trong Năm 2026) -> KHÓA
        var endOfYearUtc = new DateTime(2026, 12, 31, 16, 59, 59, DateTimeKind.Utc);
        // 31/12/2026 17:00:00 UTC = 01/01/2027 00:00:00 VN (sang Năm 2027) -> MỞ
        var startOfNextYearUtc = new DateTime(2026, 12, 31, 17, 0, 0, DateTimeKind.Utc);

        // Act & Assert
        Assert.True(await _guard.IsPeriodFinalizedAsync(endOfYearUtc));
        Assert.False(await _guard.IsPeriodFinalizedAsync(startOfNextYearUtc));
    }

    [Fact]
    public async Task TimezoneBoundary_LeapYear_AccuratelyConvertsUtcToVietnamTime()
    {
        // Arrange: Tháng 2/2028 (năm nhuận) chốt
        _snapshots.Add(new HouseholdTaxSnapshot
        {
            Year = 2028,
            PeriodType = TaxPeriodType.Month,
            PeriodNumber = 2,
            BranchId = null
        });

        // 29/02/2028 16:59:59 UTC = 29/02/2028 23:59:59 VN (ngày nhuận Tháng 2) -> KHÓA
        var leapDayUtc = new DateTime(2028, 2, 29, 16, 59, 59, DateTimeKind.Utc);
        // 29/02/2028 17:00:00 UTC = 01/03/2028 00:00:00 VN (sang Tháng 3) -> MỞ
        var startOfMarchUtc = new DateTime(2028, 2, 29, 17, 0, 0, DateTimeKind.Utc);

        // Act & Assert
        Assert.True(await _guard.IsPeriodFinalizedAsync(leapDayUtc));
        Assert.False(await _guard.IsPeriodFinalizedAsync(startOfMarchUtc));
    }
}
