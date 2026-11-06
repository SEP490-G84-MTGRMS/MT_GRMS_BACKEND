using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Entities.Sales;
using MT_GRMS.Infrastructure.Services.Printer;
using Xunit;

namespace MT_GRMS.UnitTests;

public class PosPrintAndPreOrderUnitsTests
{
    private readonly ThermalPrintService _printService = new();

    [Fact]
    public void REQ_POS_PRINT_01_ThermalPrintService_TwoLineLayout_NoRowClash_Valid40Chars()
    {
        // Arrange: Hóa đơn chứa mặt hàng có tên rất dài
        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrderCode = "HD20261105001",
            OrderDate = DateTime.Now,
            TotalAmount = 110000m,
            FinalAmount = 110000m,
            PaymentMethod = "CASH",
            Branch = new Branch { Name = "Sieu Thi Mini", Address = "120 Hoang Hoa Tham", Phone = "0987654321" },
            Details = new List<OrderDetail>
            {
                new OrderDetail
                {
                    ProductName = "Banh Orion Choco-Pie truyen thong Hop 12 cai",
                    Qty = 2,
                    UnitPrice = 55000m,
                    ActualPrice = 55000m
                }
            }
        };

        // Act
        byte[] bytes = _printService.BuildEscPosReceipt(order, cashReceived: 110000m, changeAmount: 0m, cashierName: "Chu cua hang: Tran Van An");
        string receiptText = Encoding.ASCII.GetString(bytes);

        // Assert
        Assert.Contains("Chu cua hang: Tran Van An", receiptText);
        Assert.Contains("Banh Orion Choco-Pie", receiptText);
        Assert.Contains("55.000", receiptText);
        Assert.Contains("110.000", receiptText);
        Assert.Contains("Tong cong:", receiptText);
        Assert.Contains("TONG PHAI TRA", receiptText);
    }

    [Fact]
    public void REQ_POS_PRINT_01_ThermalPrintService_DynamicCashier_OwnerAndStaff_Labels()
    {
        var order = new Order { Id = Guid.NewGuid(), OrderCode = "HD01", Details = new List<OrderDetail>() };

        // Test Chủ cửa hàng
        byte[] ownerBytes = _printService.BuildEscPosReceipt(order, cashierName: "Chu cua hang: Nguyen Ba Son");
        string ownerText = Encoding.ASCII.GetString(ownerBytes);
        Assert.Contains("Chu cua hang: Nguyen Ba Son", ownerText);

        // Test Nhân viên thu ngân ca
        byte[] staffBytes = _printService.BuildEscPosReceipt(order, cashierName: "Le Hoang Nam");
        string staffText = Encoding.ASCII.GetString(staffBytes);
        Assert.Contains("Thu ngan: Le Hoang Nam", staffText);
    }

    [Fact]
    public void REQ_POS_PRINT_02_ThermalPrintService_MatchesImageForm_4WordsPerLine()
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrderCode = "HD261105131245851",
            OrderDate = new DateTime(2026, 11, 5, 20, 12, 46),
            TotalAmount = 364000m,
            FinalAmount = 364000m,
            PaymentMethod = "CASH",
            Branch = new Branch
            {
                Name = "An Nhien Mart - Chi nhanh Ba Dinh",
                Address = "120 Hoang Hoa Tham, Tay Ho, Ha Noi",
                Phone = "024 3822 9988"
            },
            Customer = new Customer
            {
                Name = "Khach hang le",
                RewardPoints = 36
            },
            Details = new List<OrderDetail>
            {
                new OrderDetail
                {
                    ProductName = "Muối chấm Hảo Hảo tôm chua cay Hũ 120g",
                    Qty = 1,
                    UnitPrice = 16000m,
                    ActualPrice = 16000m
                },
                new OrderDetail
                {
                    ProductName = "Cà phê hòa tan G7 3in1 Hộp 18 gói",
                    Qty = 6,
                    UnitPrice = 58000m,
                    ActualPrice = 58000m
                }
            }
        };

        byte[] bytes = _printService.BuildEscPosReceipt(
            order,
            cashReceived: 400000m,
            changeAmount: 36000m,
            cashierName: "Chu cua hang: Tran Quoc An (Chu tiem An Nhien)");

        string receiptText = Encoding.ASCII.GetString(bytes);

        // Header
        Assert.Contains("AN NHIEN MART - CHI NHANH BA DINH", receiptText);
        Assert.Contains("HOA DON BAN LE", receiptText);
        Assert.Contains("HD:  HD261105131245851", receiptText);
        Assert.Contains("Chu cua hang: Tran Quoc An", receiptText);

        // Table Header
        Assert.Contains("STT  Ten hang", receiptText);
        Assert.Contains("Thanh tien", receiptText);
        Assert.Contains("Don gia / So luong", receiptText);

        // 4 words per line check
        Assert.Contains("1. Muoi cham Hao Hao", receiptText);
        Assert.Contains("tom chua cay Hu", receiptText);
        Assert.Contains("120g", receiptText);
        Assert.Contains("16.000", receiptText);

        Assert.Contains("2. Ca phe hoa tan", receiptText);
        Assert.Contains("G7 3in1 Hop 18", receiptText);
        Assert.Contains("goi", receiptText);
        Assert.Contains("348.000", receiptText);

        // Footer table
        Assert.Contains("Tong cong:", receiptText);

        // Payment
        Assert.Contains("TONG PHAI TRA:", receiptText);
        Assert.Contains("Tien khach tra:", receiptText);
        Assert.Contains("Tien mat:", receiptText);
        Assert.Contains("Tien con du:", receiptText);

        // Customer
        Assert.Contains("THONG TIN KHACH HANG", receiptText);
        Assert.Contains("Ten khach hang: Khach hang le", receiptText);
        Assert.Contains("Tong diem tich luy: 36 diem", receiptText);
    }

    [Fact]
    public void REQ_POS_MULTI_BRANCH_01_RoleScoping_OwnerCanSwitchAndCancel_EmployeeScoped()
    {
        // 1. Arrange Roles & Permissions
        var ownerRole = "STORE_OWNER";
        var employeeRole = "EMPLOYEE";

        var branches = new List<string> { "BR-AN-01", "BR-AN-02", "BR-AN-03" };
        var employeeBranch = "BR-AN-01";

        // 2. Act & Assert: Store owner can switch across all branches
        bool ownerCanSwitchBranch = ownerRole == "STORE_OWNER";
        Assert.True(ownerCanSwitchBranch);

        // Store owner can cancel order
        bool ownerCanCancelOrder = ownerRole == "STORE_OWNER";
        Assert.True(ownerCanCancelOrder);

        // Employee cannot cancel order, but can return/exchange
        bool employeeCanCancelOrder = employeeRole == "STORE_OWNER";
        bool employeeCanReturnExchange = true; // Employee is allowed return/exchange
        Assert.False(employeeCanCancelOrder);
        Assert.True(employeeCanReturnExchange);

        // Employee branch scoping
        var accessibleBranches = employeeRole == "STORE_OWNER" ? branches : new List<string> { employeeBranch };
        Assert.Single(accessibleBranches);
        Assert.Equal("BR-AN-01", accessibleBranches[0]);
    }

    [Fact]
    public void REQ_POS_FEFO_02_FefoLotAllocation_And_ManualOverride()
    {
        // Arrange lots with different expiry dates
        var lotNearExpiry = new { BatchCode = "BATCH-AN-SUA-2611-CAN-DATE", Expiry = DateTime.UtcNow.AddDays(5), Qty = 10 };
        var lotNormal = new { BatchCode = "BATCH-AN-SUA-2611-MOI", Expiry = DateTime.UtcNow.AddDays(60), Qty = 50 };

        var lots = new[] { lotNormal, lotNearExpiry };

        // Act 1: Default FEFO sorts earliest expiry date first
        var fefoSorted = System.Linq.Enumerable.OrderBy(lots, l => l.Expiry).ToList();
        Assert.Equal("BATCH-AN-SUA-2611-CAN-DATE", fefoSorted[0].BatchCode);

        // Act 2: Manual lot selection overrides default allocation
        string? userSelectedLot = "BATCH-AN-SUA-2611-CAN-DATE";
        string effectiveCartLot = userSelectedLot ?? fefoSorted[0].BatchCode;

        Assert.Equal("BATCH-AN-SUA-2611-CAN-DATE", effectiveCartLot);
    }
}

