using System;
using MT_GRMS.Application.Batches;
using MT_GRMS.Domain.Entities.Inventory;
using Xunit;

namespace MT_GRMS.UnitTests.Batches;

public class FefoDiscountWindowTests
{
    private static BranchStock Stock(DateTime startUtc, DateTime endUtc, decimal percent = 20m, bool active = true)
        => new()
        {
            IsFefoDiscountActive = active,
            FefoDiscountPercent = percent,
            FefoDiscountStartAtUtc = startUtc,
            FefoDiscountEndAtUtc = endUtc
        };

    [Fact]
    public void IsEffective_BeforeStart_ReturnsFalse()
    {
        var start = new DateTime(2026, 11, 24, 7, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 11, 29, 0, 0, 0, DateTimeKind.Utc);
        var stock = Stock(start, end);

        Assert.False(FefoDiscountWindow.IsEffective(stock, start.AddMinutes(-1)));
        Assert.Equal("Scheduled", FefoDiscountWindow.ResolveStatus(stock, start.AddMinutes(-1)));
    }

    [Fact]
    public void IsEffective_AtStart_ReturnsTrue()
    {
        var start = new DateTime(2026, 11, 24, 7, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 11, 29, 0, 0, 0, DateTimeKind.Utc);
        var stock = Stock(start, end);

        Assert.True(FefoDiscountWindow.IsEffective(stock, start));
        Assert.Equal("Active", FefoDiscountWindow.ResolveStatus(stock, start));
    }

    [Fact]
    public void IsEffective_AtEnd_ReturnsFalse()
    {
        var start = new DateTime(2026, 11, 24, 7, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 11, 29, 0, 0, 0, DateTimeKind.Utc);
        var stock = Stock(start, end);

        Assert.False(FefoDiscountWindow.IsEffective(stock, end));
        Assert.Equal("Completed", FefoDiscountWindow.ResolveStatus(stock, end));
    }

    [Fact]
    public void IsEffective_AfterCancel_ReturnsFalse()
    {
        var start = new DateTime(2026, 11, 24, 7, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 11, 29, 0, 0, 0, DateTimeKind.Utc);
        var stock = Stock(start, end, active: false, percent: 0m);

        Assert.False(FefoDiscountWindow.IsEffective(stock, start.AddHours(1)));
        Assert.Equal("None", FefoDiscountWindow.ResolveStatus(stock, start.AddHours(1)));
    }

    [Fact]
    public void IsEffective_ActiveWithNullWindow_ReturnsTrueAndActiveStatus()
    {
        var stock = new BranchStock
        {
            IsFefoDiscountActive = true,
            FefoDiscountPercent = 50m,
            FefoDiscountStartAtUtc = null,
            FefoDiscountEndAtUtc = null
        };

        var now = new DateTime(2026, 11, 26, 7, 0, 0, DateTimeKind.Utc);
        Assert.True(FefoDiscountWindow.IsEffective(stock, now));
        Assert.Equal("Active", FefoDiscountWindow.ResolveStatus(stock, now));
    }
}
