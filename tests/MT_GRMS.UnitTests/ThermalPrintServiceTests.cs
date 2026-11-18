using System;
using System.Collections.Generic;
using MT_GRMS.Domain.Entities.Sales;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Infrastructure.Services.Printer;
using Xunit;

namespace MT_GRMS.UnitTests;

public class ThermalPrintServiceTests
{
    [Fact]
    public void BuildEscPosReceipt_WalkInCustomer_Cash_ShouldGenerateBytes()
    {
        var service = new ThermalPrintService();
        var order = new Order
        {
            OrderCode = "HD12345",
            OrderDate = DateTime.UtcNow,
            PaymentMethod = "CASH",
            TotalAmount = 50000,
            FinalAmount = 50000,
            DiscountAmount = 0,
            Branch = new Branch
            {
                Name = "Siêu thị mini số 1",
                Address = "123 Đường Cầu Giấy, Hà Nội",
                Phone = "0987654321"
            },
            Details = new List<OrderDetail>
            {
                new()
                {
                    ProductName = "Sữa chua Vinamilk có đường 100g vỉ 4 hộp",
                    UnitPrice = 25000,
                    Qty = 2,
                    DiscountAmount = 0
                }
            }
        };

        var bytes = service.BuildEscPosReceipt(order, cashReceived: 100000, changeAmount: 50000, cashierName: "Nguyễn Văn Thu Ngân");

        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);
    }

    [Fact]
    public void BuildEscPosReceipt_StoreOwner_GiftItem_Discounts_AndVoucher_ShouldGenerateBytes()
    {
        var service = new ThermalPrintService();
        var customer = new Customer
        {
            CustomerCode = "KH001",
            Name = "Trần Thị Mai Lan",
            Address = "Số 10 Phố Huế, Hoàn Kiếm, Hà Nội",
            Phone = "0901234567",
            RewardPoints = 120
        };

        var order = new Order
        {
            OrderCode = "HD99999",
            OrderDate = new DateTime(2026, 11, 15, 14, 30, 0),
            PaymentMethod = "VIETQR",
            TotalAmount = 150000,
            DiscountAmount = 30000,
            PromotionDiscountAmount = 10000,
            AppliedPromotionSummary = "Giảm giá hóa đơn cuối tuần",
            AppliedVoucherCode = "VOUCHER10K",
            VoucherDiscountAmount = 10000,
            PointsRedeemed = 10,
            PointsDiscountAmount = 10000,
            FinalAmount = 120000,
            Customer = customer,
            Details = new List<OrderDetail>
            {
                new()
                {
                    ProductName = "Bánh gạo One One",
                    UnitPrice = 30000,
                    Qty = 3,
                    DiscountAmount = 5000,
                    IsGift = false
                },
                new()
                {
                    ProductName = "Nước ngọt Coca Cola 330ml lon",
                    UnitPrice = 10000,
                    Qty = 1,
                    IsGift = true
                },
                new()
                {
                    Product = new Product { Name = "Sản phẩm không có name chi tiết" },
                    UnitPrice = 15000,
                    Qty = 2,
                    IsGift = false
                }
            }
        };

        var bytes = service.BuildEscPosReceipt(order, cashierName: "Chu cua hang");

        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);
    }

    [Fact]
    public void BuildEscPosReceipt_CreditDebt_AndOtherPaymentMethods_ShouldWork()
    {
        var service = new ThermalPrintService();
        var orderDebt = new Order
        {
            OrderCode = "HD_DEBT",
            PaymentMethod = "DEBT",
            TotalAmount = 100000,
            FinalAmount = 100000,
            DiscountAmount = 10000
        };
        var bytesDebt = service.BuildEscPosReceipt(orderDebt);
        Assert.NotNull(bytesDebt);

        var orderOther = new Order
        {
            OrderCode = "HD_MOMO",
            PaymentMethod = "MOMO",
            TotalAmount = 50000,
            FinalAmount = 50000
        };
        var bytesOther = service.BuildEscPosReceipt(orderOther);
        Assert.NotNull(bytesOther);
    }

    [Fact]
    public void RawPrinterHelper_And_PrintOrderInvoice_ShouldHandleGracefully()
    {
        var service = new ThermalPrintService();
        var order = new Order
        {
            OrderCode = "HD_RAW",
            PaymentMethod = "CASH",
            TotalAmount = 10000,
            FinalAmount = 10000
        };

        // Non-existent printer on Windows prints false gracefully
        var printed = service.PrintOrderInvoice(order, "NON_EXISTENT_PRINTER_12345");
        Assert.False(printed);

        // Directly test RawPrinterHelper with empty and non-empty byte arrays
        var res1 = RawPrinterHelper.SendBytesToPrinter("INVALID_PRINTER", new byte[] { 0x1B, 0x40 });
        Assert.False(res1);

        var docInfo = new RawPrinterHelper.DOCINFOA
        {
            pDocName = "Test Doc",
            pOutputFile = null,
            pDataType = "RAW"
        };
        Assert.Equal("Test Doc", docInfo.pDocName);
        Assert.Equal("RAW", docInfo.pDataType);
    }
}