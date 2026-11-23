using MT_GRMS.Application.Inventory;
using Xunit;

namespace MT_GRMS.UnitTests;

public class InventoryOnOrderTests
{
    [Theory]
    [InlineData("APPROVED", true)]
    [InlineData("CONFIRMED", true)]
    [InlineData("DISPATCHED", true)]
    [InlineData("SENT", true)]
    [InlineData("DRAFT", false)]
    [InlineData("PENDING_APPROVAL", false)]
    [InlineData("CANCELLED", false)]
    [InlineData("REJECTED", false)]
    public void IsOpenPoStatus_MatchesPlan(string status, bool expected)
    {
        Assert.Equal(expected, InventoryOnOrder.IsOpenPoStatus(status));
    }

    [Fact]
    public void LineRemaining_DispatchedOpen_ReturnsOrderedMinusReceived()
    {
        var remaining = InventoryOnOrder.LineRemaining(
            orderedQty: 100m,
            finalQty: 90m,
            receivedQty: 40m,
            isClosed: false);

        Assert.Equal(60m, remaining);
    }

    [Fact]
    public void LineRemaining_UsesFinalQtyWhenOrderedNull()
    {
        var remaining = InventoryOnOrder.LineRemaining(
            orderedQty: null,
            finalQty: 50m,
            receivedQty: 10m,
            isClosed: false);

        Assert.Equal(40m, remaining);
    }

    [Fact]
    public void LineRemaining_FullyReceived_ReturnsZero()
    {
        var remaining = InventoryOnOrder.LineRemaining(80m, null, 80m, isClosed: false);
        Assert.Equal(0m, remaining);
    }

    [Fact]
    public void LineRemaining_OverReceived_ReturnsZero()
    {
        var remaining = InventoryOnOrder.LineRemaining(50m, null, 60m, isClosed: false);
        Assert.Equal(0m, remaining);
    }

    [Fact]
    public void LineRemaining_ClosedLine_ReturnsZero()
    {
        var remaining = InventoryOnOrder.LineRemaining(100m, null, 0m, isClosed: true);
        Assert.Equal(0m, remaining);
    }

    [Fact]
    public void TotalOnOrder_SumsLineRemainings()
    {
        var lines = new[]
        {
            InventoryOnOrder.LineRemaining(100m, null, 40m, false), // 60
            InventoryOnOrder.LineRemaining(null, 20m, 20m, false),  // 0 fully received
            InventoryOnOrder.LineRemaining(30m, null, 0m, true),    // 0 closed
            InventoryOnOrder.LineRemaining(10m, null, 0m, false),   // 10
        };

        Assert.Equal(70m, lines[0] + lines[1] + lines[2] + lines[3]);
    }
}
