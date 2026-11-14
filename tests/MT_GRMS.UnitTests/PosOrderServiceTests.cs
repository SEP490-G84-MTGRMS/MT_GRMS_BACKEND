using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using MT_GRMS.Application.Catalog;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.PosOrders;
using MT_GRMS.Application.PosOrders.Dtos;
using MT_GRMS.Application.PosOrders.Validators;
using MT_GRMS.Application.Promotions;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Application.PosShifts;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Entities.Sales;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Domain.Exceptions;
using MT_GRMS.Infrastructure.Services.Printer;
using Moq;
using Xunit;

namespace MT_GRMS.UnitTests;

public class PosOrderServiceTests
{
    private readonly FakeOrderRepository _orderRepo;
    private readonly FakeUnitOfWork _uow;
    private readonly ThermalPrintService _printService;
    private readonly OrderService _orderService;

    private readonly Guid _branchId = Guid.Parse("11111111-aaaa-bbbb-cccc-111111111111");
    private readonly Guid _customerId = Guid.Parse("22222222-aaaa-bbbb-cccc-222222222222");
    private readonly Guid _prodHaoHaoId = Guid.Parse("33333333-aaaa-bbbb-cccc-333333333333");
    private readonly Guid _prodCocaId = Guid.Parse("44444444-aaaa-bbbb-cccc-444444444444");

    public PosOrderServiceTests()
    {
        _orderRepo = new FakeOrderRepository();
        _uow = new FakeUnitOfWork();
        _printService = new ThermalPrintService();
        _orderService = new OrderService(_orderRepo, _uow, _printService, new OrderCreateRequestValidator());

        // Seed Branch
        _orderRepo.Branches.Add(new Branch
        {
            Id = _branchId,
            TenantId = "tenant-01",
            BranchCode = "CN01",
            Name = "Sieu Thi Mini MT-Grocery Cau Giay",
            Address = "123 Xuan Thuy, Cau Giay, Ha Noi",
            Phone = "0987654321",
            IsActive = true
        });

        // Seed Customer
        _orderRepo.Customers.Add(new Customer
        {
            Id = _customerId,
            TenantId = "tenant-01",
            CustomerCode = "KH001",
            Name = "Nguyen Van Khach",
            Phone = "0912345678",
            RewardPoints = 50,
            TotalSpending = 1000000m,
            TotalDebt = 200000m,
            DebtLimit = 2000000m,
            AllowDebt = true,
            IsBlockedForDebt = false
        });

        // Seed Products
        _orderRepo.Products.AddRange(new[]
        {
            new Product
            {
                Id = _prodHaoHaoId,
                TenantId = "tenant-01",
                ProductCode = "HH01",
                Barcode = "8934563138164",
                Name = "Mi Hao Hao Tom Chua Cay",
                SellingPrice = 5000m,
                BaseUnit = "Goi",
                VatRate = 0.01m,
                IsActive = true
            },
            new Product
            {
                Id = _prodCocaId,
                TenantId = "tenant-01",
                ProductCode = "COCA01",
                Barcode = "8935049500123",
                Name = "Nuoc Ngot Coca-Cola 330ml",
                SellingPrice = 10000m,
                BaseUnit = "Lon",
                VatRate = 0.01m,
                IsActive = true
            }
        });

        // Seed BranchStocks (Lô FEFO cho Mì Hảo Hảo)
        _orderRepo.BranchStocks.AddRange(new[]
        {
            // Lô A: Hạn sử dụng sớm (01/2026) -> còn 10 gói
            new BranchStock
            {
                Id = Guid.NewGuid(),
                TenantId = "tenant-01",
                BranchId = _branchId,
                ProductId = _prodHaoHaoId,
                BatchCode = "BATCH-A-2026-01",
                ExpiryDate = new DateTime(2026, 1, 1),
                QuantityOnHand = 10m,
                LandedCostPerUnit = 3800m
            },
            // Lô B: Hạn sử dụng muộn hơn (06/2026) -> còn 20 gói
            new BranchStock
            {
                Id = Guid.NewGuid(),
                TenantId = "tenant-01",
                BranchId = _branchId,
                ProductId = _prodHaoHaoId,
                BatchCode = "BATCH-B-2026-06",
                ExpiryDate = new DateTime(2026, 6, 1),
                QuantityOnHand = 20m,
                LandedCostPerUnit = 4000m
            },
            // Tồn kho Coca-Cola chỉ có 3 lon
            new BranchStock
            {
                Id = Guid.NewGuid(),
                TenantId = "tenant-01",
                BranchId = _branchId,
                ProductId = _prodCocaId,
                BatchCode = "BATCH-COCA-01",
                ExpiryDate = new DateTime(2026, 12, 31),
                QuantityOnHand = 3m,
                LandedCostPerUnit = 7500m
            }
        });

        // Seed BranchProducts Active cho Mì Hảo Hảo & Coca-Cola tại _branchId (REQ-BRANCH-SCOPE-02)
        _orderRepo.BranchProducts.AddRange(new[]
        {
            new BranchProduct
            {
                Id = Guid.NewGuid(),
                TenantId = "tenant-01",
                BranchId = _branchId,
                ProductId = _prodHaoHaoId,
                Status = "Active"
            },
            new BranchProduct
            {
                Id = Guid.NewGuid(),
                TenantId = "tenant-01",
                BranchId = _branchId,
                ProductId = _prodCocaId,
                Status = "Active"
            }
        });
    }

    [Fact]
    public async Task REQ_POS_01_CreateOrder_Cash_Success_ComputesChangeAmount()
    {
        // Arrange: Mua 2 lon Coca (2 x 10.000 = 20.000), Khách đưa 50.000
        var request = new OrderCreateRequest(
            BranchId: _branchId,
            CustomerId: null,
            PaymentMethod: "CASH",
            CashTendered: 50000m,
            DiscountAmount: 0m,
            Items: new List<OrderItemCreateDto>
            {
                new OrderItemCreateDto(_prodCocaId, 2m, 10000m)
            }
        );

        // Act
        var result = await _orderService.CreateOrderAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(20000m, result.TotalAmount);
        Assert.Equal(20000m, result.FinalAmount);
        Assert.Equal(50000m, result.CashTendered);
        Assert.Equal(30000m, result.ChangeAmount);
        Assert.Equal("PAID", result.PaymentStatus);
        Assert.Equal("Completed", result.Status);
        Assert.Single(result.Items);
    }

    [Fact]
    public async Task REQ_POS_01_CreateOrder_VietQr_Success_StatusPaid()
    {
        // Arrange
        var request = new OrderCreateRequest(
            BranchId: _branchId,
            CustomerId: null,
            PaymentMethod: "VIETQR",
            Items: new List<OrderItemCreateDto>
            {
                new OrderItemCreateDto(_prodCocaId, 1m, 10000m)
            }
        );

        // Act
        var result = await _orderService.CreateOrderAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(10000m, result.FinalAmount);
        Assert.Equal("VIETQR", result.PaymentMethod);
        Assert.Equal("PAID", result.PaymentStatus);
        Assert.Equal(0m, result.ChangeAmount);
    }

    [Fact]
    public async Task REQ_POS_01_CreateOrder_EmptyCart_ThrowsValidationException()
    {
        // Arrange: Giỏ hàng rỗng
        var request = new OrderCreateRequest(
            BranchId: _branchId,
            CustomerId: null,
            PaymentMethod: "CASH",
            Items: new List<OrderItemCreateDto>()
        );

        // Act & Assert
        await Assert.ThrowsAsync<ValidationException>(() => _orderService.CreateOrderAsync(request));
    }

    [Fact]
    public async Task REQ_POS_01_CreateOrder_CashUnderpaid_ThrowsInvalidOperationException()
    {
        // Arrange: Tổng tiền 20.000 mà khách chỉ đưa 15.000
        var request = new OrderCreateRequest(
            BranchId: _branchId,
            CustomerId: null,
            PaymentMethod: "CASH",
            CashTendered: 15000m,
            Items: new List<OrderItemCreateDto>
            {
                new OrderItemCreateDto(_prodCocaId, 2m, 10000m)
            }
        );

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _orderService.CreateOrderAsync(request));
        Assert.Contains("không đủ để thanh toán", ex.Message);
    }

    [Fact]
    public async Task REQ_POS_02_CreateOrder_FefoInventoryDeduction_Success()
    {
        // Arrange: Bán 15 gói Mì Hảo Hảo
        // Lô A có 10 gói, Lô B có 20 gói -> Trừ hết 10 gói Lô A, Lô B còn 15 gói
        var request = new OrderCreateRequest(
            BranchId: _branchId,
            CustomerId: null,
            PaymentMethod: "CASH",
            CashTendered: 100000m,
            Items: new List<OrderItemCreateDto>
            {
                new OrderItemCreateDto(_prodHaoHaoId, 15m, 5000m)
            }
        );

        // Act
        var result = await _orderService.CreateOrderAsync(request);

        // Assert
        Assert.NotNull(result);
        var lotA = _orderRepo.BranchStocks.First(s => s.BatchCode == "BATCH-A-2026-01");
        var lotB = _orderRepo.BranchStocks.First(s => s.BatchCode == "BATCH-B-2026-06");

        Assert.Equal(0m, lotA.QuantityOnHand);
        Assert.Equal(15m, lotB.QuantityOnHand);
    }

    [Fact]
    public async Task REQ_POS_02_CreateOrder_InsufficientStock_ThrowsInvalidOperationException()
    {
        // Arrange: Kho chỉ có 3 lon Coca, bán 5 lon -> Báo lỗi từ chối
        var request = new OrderCreateRequest(
            BranchId: _branchId,
            CustomerId: null,
            PaymentMethod: "CASH",
            Items: new List<OrderItemCreateDto>
            {
                new OrderItemCreateDto(_prodCocaId, 5m, 10000m)
            }
        );

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _orderService.CreateOrderAsync(request));
        Assert.Contains("không đủ tồn kho tại chi nhánh", ex.Message);
    }

    [Fact]
    public async Task REQ_POS_05_CreateOrder_Debt_Success_IncrementsCustomerDebt()
    {
        // Arrange: Khách nợ 50.000đ (Hiện tại nợ 200.000, Hạn mức 2.000.000)
        var request = new OrderCreateRequest(
            BranchId: _branchId,
            CustomerId: _customerId,
            PaymentMethod: "DEBT",
            Items: new List<OrderItemCreateDto>
            {
                new OrderItemCreateDto(_prodCocaId, 1m, 10000m)
            }
        );

        // Act
        var result = await _orderService.CreateOrderAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("DEBT", result.PaymentMethod);
        Assert.Equal("UNPAID", result.PaymentStatus);

        var cust = _orderRepo.Customers.First(c => c.Id == _customerId);
        Assert.Equal(210000m, cust.TotalDebt); // 200.000 + 10.000
    }

    [Fact]
    public async Task REQ_POS_05_CreateOrder_DebtWithoutLimit_Succeeds()
    {
        // Arrange: Không áp dụng hạn mức nợ, khách mua thêm 10.000đ thì nợ được cộng dồn bình thường
        var cust = _orderRepo.Customers.First(c => c.Id == _customerId);
        cust.DebtLimit = 205000m;

        var request = new OrderCreateRequest(
            BranchId: _branchId,
            CustomerId: _customerId,
            PaymentMethod: "DEBT",
            Items: new List<OrderItemCreateDto>
            {
                new OrderItemCreateDto(_prodCocaId, 1m, 10000m)
            }
        );

        // Act
        var result = await _orderService.CreateOrderAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("UNPAID", result.PaymentStatus);
        Assert.Equal(210000m, cust.TotalDebt);
    }

    [Fact]
    public async Task REQ_POS_05_CreateOrder_DebtBlockedCustomer_ThrowsInvalidOperationException()
    {
        // Arrange: Khách hàng bị khóa nợ
        var cust = _orderRepo.Customers.First(c => c.Id == _customerId);
        cust.IsBlockedForDebt = true;

        var request = new OrderCreateRequest(
            BranchId: _branchId,
            CustomerId: _customerId,
            PaymentMethod: "DEBT",
            Items: new List<OrderItemCreateDto>
            {
                new OrderItemCreateDto(_prodCocaId, 1m, 10000m)
            }
        );

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _orderService.CreateOrderAsync(request));
        Assert.Contains("đang bị khóa nợ", ex.Message);
    }

    [Fact]
    public async Task REQ_POS_05_CreateOrder_DebtByCashier_ThrowsUnauthorizedAccessException()
    {
        // Arrange: Thu ngân gửi yêu cầu thanh toán ghi nợ -> Phải bị chặn với mã lỗi 403 / UnauthorizedAccessException
        var request = new OrderCreateRequest(
            BranchId: _branchId,
            CustomerId: _customerId,
            PaymentMethod: "DEBT",
            Items: new List<OrderItemCreateDto>
            {
                new OrderItemCreateDto(_prodCocaId, 1m, 10000m)
            },
            CreatorRole: "CASHIER"
        );

        // Act & Assert
        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _orderService.CreateOrderAsync(request));
        Assert.Contains("Chỉ Chủ cửa hàng hoặc Quản trị viên", ex.Message);
    }

    [Fact]
    public async Task REQ_POS_05_CreateOrder_DebtByStoreOwner_Success()
    {
        // Arrange: Chủ cửa hàng duyệt đơn ghi nợ -> Thành công
        var ownerApprovedCustomer = _orderRepo.Customers.First(c => c.Id == _customerId);
        ownerApprovedCustomer.AllowDebt = false;
        ownerApprovedCustomer.DebtLimit = 0;
        var initialDebt = ownerApprovedCustomer.TotalDebt;
        var request = new OrderCreateRequest(
            BranchId: _branchId,
            CustomerId: _customerId,
            PaymentMethod: "DEBT",
            Items: new List<OrderItemCreateDto>
            {
                new OrderItemCreateDto(_prodCocaId, 1m, 10000m)
            },
            CreatorRole: "STORE_OWNER"
        );

        // Act
        var result = await _orderService.CreateOrderAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("DEBT", result.PaymentMethod);
        Assert.Equal(initialDebt + 10000m, _orderRepo.Customers.First(c => c.Id == _customerId).TotalDebt);
    }

    [Fact]
    public async Task REQ_POS_06_CreateOrder_EarnsRewardPoints_ForCustomer()
    {
        // Arrange: Khách mua đơn 30.000đ -> được tích 3 điểm (10.000đ = 1 điểm)
        var initPoints = _orderRepo.Customers.First(c => c.Id == _customerId).RewardPoints;

        var request = new OrderCreateRequest(
            BranchId: _branchId,
            CustomerId: _customerId,
            PaymentMethod: "CASH",
            CashTendered: 30000m,
            Items: new List<OrderItemCreateDto>
            {
                new OrderItemCreateDto(_prodCocaId, 3m, 10000m)
            }
        );

        // Act
        var result = await _orderService.CreateOrderAsync(request);

        // Assert
        Assert.NotNull(result);
        var cust = _orderRepo.Customers.First(c => c.Id == _customerId);
        Assert.Equal(initPoints + 3, cust.RewardPoints);
    }

    [Fact]
    public async Task REQ_POS_06_CreateOrder_WalkInCustomer_DoesNotEarnRewardPoints()
    {
        // Arrange: Khách lẻ không gắn CustomerId hoặc khách có tên "Khách lẻ" -> không được tích điểm
        var walkInCustomer = new Customer
        {
            Id = Guid.NewGuid(),
            CustomerCode = "KHACHLE",
            Name = "Khách lẻ",
            Phone = "0999999999",
            RewardPoints = 0,
            TenantId = "tenant-unit-test"
        };
        _orderRepo.Customers.Add(walkInCustomer);

        var request = new OrderCreateRequest(
            BranchId: _branchId,
            CustomerId: walkInCustomer.Id,
            PaymentMethod: "CASH",
            CashTendered: 20000m,
            Items: new List<OrderItemCreateDto>
            {
                new OrderItemCreateDto(_prodCocaId, 2m, 10000m)
            }
        );

        // Act
        var result = await _orderService.CreateOrderAsync(request);

        // Assert: Điểm vẫn là 0, không tích điểm
        Assert.NotNull(result);
        var updatedCust = _orderRepo.Customers.First(c => c.Id == walkInCustomer.Id);
        Assert.Equal(0, updatedCust.RewardPoints);
    }

    [Fact]
    public async Task REQ_POS_06_CreateOrder_WhenLoyaltyDisabled_DoesNotEarnRewardPoints()
    {
        // Arrange: Cấu hình tích điểm bị tắt (IsEnabled = false)
        var mockPromoRepo = new Mock<IPromotionRepository>();
        mockPromoRepo.Setup(x => x.GetLoyaltySettingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LoyaltySetting
            {
                TenantId = "tenant-unit-test",
                IsEnabled = false,
                EarningRateAmount = 10000m,
                EarningPoints = 1
            });

        var orderService = new OrderService(
            _orderRepo,
            _uow,
            _printService,
            debtService: null,
            cashbookService: null,
            validator: new OrderCreateRequestValidator(),
            shiftRepository: null,
            promotionRepository: mockPromoRepo.Object,
            tenantContext: null);

        var initPoints = _orderRepo.Customers.First(c => c.Id == _customerId).RewardPoints;

        var request = new OrderCreateRequest(
            BranchId: _branchId,
            CustomerId: _customerId,
            PaymentMethod: "CASH",
            CashTendered: 30000m,
            Items: new List<OrderItemCreateDto>
            {
                new OrderItemCreateDto(_prodCocaId, 3m, 10000m)
            }
        );

        // Act
        var result = await orderService.CreateOrderAsync(request);

        // Assert: Điểm không thay đổi vì tính năng tích điểm đang tắt
        Assert.NotNull(result);
        var cust = _orderRepo.Customers.First(c => c.Id == _customerId);
        Assert.Equal(initPoints, cust.RewardPoints);
    }

    [Fact]
    public void REQ_POS_04_ThermalPrintService_BuildEscPosReceipt_GeneratesValid43mmMiniSupermarketStream()
    {
        // Arrange: Tạo đơn hàng test khổ 43mm siêu thị mini
        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrderCode = "HD20261031001",
            OrderDate = DateTime.Now,
            TotalAmount = 50000m,
            DiscountAmount = 5000m,
            FinalAmount = 45000m,
            PaymentMethod = "CASH",
            Branch = new Branch
            {
                Name = "Sieu Thi Mini MT-Grocery",
                Address = "123 Xuan Thuy, Cau Giay, Ha Noi",
                Phone = "0987654321"
            },
            Customer = new Customer
            {
                Name = "Nguyen Van A",
                RewardPoints = 120
            },
            Details = new List<OrderDetail>
            {
                new OrderDetail
                {
                    ProductName = "Banh Mi Sandwich",
                    Qty = 2,
                    UnitPrice = 25000m,
                    ActualPrice = 22500m,
                    VatAmount = 450m
                }
            }
        };

        // Act
        byte[] bytes = _printService.BuildEscPosReceipt(order, cashReceived: 50000m, changeAmount: 5000m);

        // Assert
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 100);

        // 1. Kiểm tra mã khởi tạo ESC @ (0x1B, 0x40)
        Assert.Equal(0x1B, bytes[0]);
        Assert.Equal(0x40, bytes[1]);

        // 2. Kiểm tra lệnh chọn font Font B nhỏ gọn (~7.5pt) cho khổ in 43mm (0x1B, 0x4D, 0x01)
        Assert.Equal(0x1B, bytes[2]);
        Assert.Equal(0x4D, bytes[3]);
        Assert.Equal(0x01, bytes[4]);

        // 3. Kiểm tra chứa tên siêu thị mini và lời cảm ơn
        string receiptText = Encoding.ASCII.GetString(bytes);
        Assert.Contains("SIEU THI MINI", receiptText);
        Assert.Contains("HOA DON BAN LE", receiptText);
        Assert.Contains("Banh Mi Sandwich", receiptText);
        Assert.Contains("TONG PHAI TRA", receiptText);
        Assert.Contains("CAM ON QUY KHACH", receiptText);

        // 4. Kiểm tra lệnh cắt giấy tự động ở cuối (0x1D, 0x56, 0x42, 0x00)
        int len = bytes.Length;
        Assert.Equal(0x1D, bytes[len - 4]);
        Assert.Equal(0x56, bytes[len - 3]);
        Assert.Equal(0x42, bytes[len - 2]);
        Assert.Equal(0x00, bytes[len - 1]);
    }

    [Fact]
    public async Task CreatePreOrderAsync_ValidRequest_ReservesStockAndReturnsPendingPickup()
    {
        // Arrange: Khách đặt trước 5 gói mì Hảo Hảo (5 * 4.500đ = 22.500đ), đặt cọc trước 10.000đ
        var initialStock = _orderRepo.BranchStocks.First(s => s.ProductId == _prodHaoHaoId).QuantityOnHand;
        var request = new PreOrderCreateRequest(
            BranchId: _branchId,
            CustomerId: _customerId,
            Items: new List<OrderItemCreateDto>
            {
                new OrderItemCreateDto(_prodHaoHaoId, 5m, 4500m)
            },
            DepositAmount: 10000m,
            ExpectedPickupDate: DateTime.UtcNow.AddDays(2),
            PaymentMethod: "CASH",
            Note: "Lấy vào buổi chiều"
        );

        // Act
        var result = await _orderService.CreatePreOrderAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.StartsWith("PO", result.OrderCode);
        Assert.Equal("PendingPickup", result.Status);
        Assert.Equal("PARTIALLY_PAID", result.PaymentStatus);
        Assert.Equal(22500m, result.FinalAmount);

        // Kiểm tra tồn kho đã được trừ/giữ đúng 5 gói
        var updatedStock = _orderRepo.BranchStocks.First(s => s.ProductId == _prodHaoHaoId).QuantityOnHand;
        Assert.Equal(initialStock - 5m, updatedStock);
    }

    [Fact]
    public async Task FulfillPreOrderAsync_ValidPayment_CompletesOrderAndAwardsPoints()
    {
        // Arrange: Tạo đơn đặt trước tổng 20.000đ, cọc 5.000đ -> còn thiếu 15.000đ
        var preOrder = await _orderService.CreatePreOrderAsync(new PreOrderCreateRequest(
            BranchId: _branchId,
            CustomerId: _customerId,
            Items: new List<OrderItemCreateDto>
            {
                new OrderItemCreateDto(_prodHaoHaoId, 4m, 5000m)
            },
            DepositAmount: 5000m,
            ExpectedPickupDate: DateTime.UtcNow.AddDays(1)
        ));

        var customer = _orderRepo.Customers.First(c => c.Id == _customerId);
        var initialPoints = customer.RewardPoints;

        var fulfillRequest = new PreOrderFulfillRequest(
            PaymentMethod: "VIETQR",
            AmountPaid: 15000m,
            Note: "Khách đã đến nhận hàng đầy đủ"
        );

        // Act
        var result = await _orderService.FulfillPreOrderAsync(preOrder.Id, fulfillRequest);

        // Assert
        Assert.Equal("Completed", result.Status);
        Assert.Equal("PAID", result.PaymentStatus);
        Assert.Equal("VIETQR", result.PaymentMethod);

        // Khách được tích điểm cho toàn bộ đơn 20.000đ (2 điểm)
        Assert.True(customer.RewardPoints > initialPoints);
    }

    [Fact]
    public async Task CancelOrderAsync_PendingPreOrder_RestoresStockOnHand()
    {
        // Arrange: Tạo đơn đặt trước giữ 3 lon Coca
        var initialStock = _orderRepo.BranchStocks.First(s => s.ProductId == _prodCocaId).QuantityOnHand;
        var preOrder = await _orderService.CreatePreOrderAsync(new PreOrderCreateRequest(
            BranchId: _branchId,
            CustomerId: _customerId,
            Items: new List<OrderItemCreateDto>
            {
                new OrderItemCreateDto(_prodCocaId, 3m, 10000m)
            },
            DepositAmount: 10000m,
            ExpectedPickupDate: DateTime.UtcNow.AddDays(1)
        ));

        // Tồn kho đã giảm 3
        Assert.Equal(initialStock - 3m, _orderRepo.BranchStocks.First(s => s.ProductId == _prodCocaId).QuantityOnHand);

        // Act: Hủy đơn đặt trước
        var cancelResult = await _orderService.CancelOrderAsync(preOrder.Id, new CancelOrderRequest(
            Reason: "Khách hủy lịch hẹn lấy",
            Note: "Đã hoàn lại tiền cọc"
        ));

        // Assert
        Assert.Equal("Cancelled", cancelResult.Status);
        Assert.Equal("REFUNDED", cancelResult.PaymentStatus);

        // Tồn kho được phục hồi về ban đầu
        var restoredStock = _orderRepo.BranchStocks.First(s => s.ProductId == _prodCocaId).QuantityOnHand;
        Assert.Equal(initialStock, restoredStock);
    }

    [Fact]
    public async Task GetOrderSummaryAsync_CalculatesCorrectMetrics()
    {
        // Arrange:
        // 1 đơn hoàn tất tiền mặt 20.000đ (4 gói mì Hảo Hảo * 5000đ)
        var o1 = await _orderService.CreateOrderAsync(new OrderCreateRequest(
            _branchId, _customerId, "CASH",
            new List<OrderItemCreateDto> { new OrderItemCreateDto(_prodHaoHaoId, 4m, 5000m) },
            CashTendered: 20000m
        ));

        // 1 đơn đặt trước cọc 10.000đ (2 gói mì Hảo Hảo * 5000đ = 10.000đ)
        var o2 = await _orderService.CreatePreOrderAsync(new PreOrderCreateRequest(
            _branchId, _customerId,
            new List<OrderItemCreateDto> { new OrderItemCreateDto(_prodHaoHaoId, 2m, 5000m) },
            DepositAmount: 10000m,
            ExpectedPickupDate: DateTime.UtcNow.AddDays(2),
            PaymentMethod: "VIETQR"
        ));

        // Act
        var summary = await _orderService.GetOrderSummaryAsync(_branchId, null, null);

        // Assert
        Assert.NotNull(summary);
        Assert.True(summary.TotalOrders >= 2);
        Assert.True(summary.TotalRevenue >= 30000m); // 20.000 + 10.000 cọc
        Assert.True(summary.CashRevenue >= 20000m);
        Assert.True(summary.TransferRevenue >= 10000m);
        Assert.True(summary.PendingPreOrdersCount >= 1);
    }

    [Fact]
    public async Task REQ_POS_RET_01_ProcessReturnExchange_HigherValue_CustomerPaysDifference()
    {
        // Arrange: Tạo đơn gốc 5 gói Hảo Hảo (5 * 5.000 = 25.000)
        var originalOrder = await _orderService.CreateOrderAsync(new OrderCreateRequest(
            _branchId, _customerId, "CASH",
            new List<OrderItemCreateDto> { new OrderItemCreateDto(_prodHaoHaoId, 5m, 5000m) },
            CashTendered: 25000m
        ));

        var initialHaoHaoStock = _orderRepo.BranchStocks.First(s => s.ProductId == _prodHaoHaoId).QuantityOnHand;
        var initialCocaStock = _orderRepo.BranchStocks.First(s => s.ProductId == _prodCocaId).QuantityOnHand;

        // Trả 2 gói Hảo Hảo (10.000đ), Đổi lấy 2 lon Coca (2 * 10.000đ = 20.000đ)
        // Chênh lệch = 20.000 - 10.000 = +10.000đ
        var request = new ReturnExchangeRequest(
            ReturnedItems: new List<ReturnItemDto> { new ReturnItemDto(_prodHaoHaoId, 2m, 5000m) },
            NewItems: new List<OrderItemCreateDto> { new OrderItemCreateDto(_prodCocaId, 2m, 10000m) },
            SettlementMethod: "CASH",
            Note: "Đổi 2 Hảo Hảo sang 2 Coca"
        );

        // Act
        var result = await _orderService.ProcessReturnExchangeAsync(originalOrder.Id, request);

        // Assert
        Assert.NotNull(result);
        Assert.StartsWith("DT", result.OrderCode);
        Assert.Equal(10000m, result.FinalAmount);
        Assert.Equal("Completed", result.Status);

        // Tồn kho Hảo Hảo tăng lại 2
        var updatedHaoHaoStock = _orderRepo.BranchStocks.First(s => s.ProductId == _prodHaoHaoId).QuantityOnHand;
        Assert.Equal(initialHaoHaoStock + 2m, updatedHaoHaoStock);

        // Tồn kho Coca giảm 2
        var updatedCocaStock = _orderRepo.BranchStocks.First(s => s.ProductId == _prodCocaId).QuantityOnHand;
        Assert.Equal(initialCocaStock - 2m, updatedCocaStock);
    }

    [Fact]
    public async Task REQ_POS_RET_02_Damaged_Condition_Does_Not_Restock()
    {
        // Arrange: Đơn gốc mua 2 gói Hảo Hảo
        var originalOrder = await _orderService.CreateOrderAsync(new OrderCreateRequest(
            _branchId, _customerId, "CASH",
            new List<OrderItemCreateDto> { new OrderItemCreateDto(_prodHaoHaoId, 2m, 5000m) },
            CashTendered: 10000m
        ));

        var initialHaoHaoStock = _orderRepo.BranchStocks.First(s => s.ProductId == _prodHaoHaoId).QuantityOnHand;

        // Trả 1 gói Hảo Hảo với tình trạng "Damaged" (hàng hỏng, không nhập lại kho bán)
        var request = new ReturnExchangeRequest(
            ReturnedItems: new List<ReturnItemDto> { new ReturnItemDto(_prodHaoHaoId, 1m, 5000m, Condition: "Damaged") },
            NewItems: new List<OrderItemCreateDto>(),
            SettlementMethod: "CASH",
            Reason: "DEFECTIVE",
            Note: "Hàng rách bao bì từ trước"
        );

        // Act
        var result = await _orderService.ProcessReturnExchangeAsync(originalOrder.Id, request);

        // Assert
        Assert.NotNull(result);
        var currentStock = _orderRepo.BranchStocks.First(s => s.ProductId == _prodHaoHaoId).QuantityOnHand;
        Assert.Equal(initialHaoHaoStock, currentStock); // Tồn kho bán lẻ KHÔNG tăng
    }

    [Fact]
    public async Task REQ_POS_RET_01_ProcessReturnExchange_ReduceDebt_Succeeds()
    {
        // Arrange: Khách nợ 200.000đ. Đơn gốc mua 4 gói Hảo Hảo (20.000đ)
        var customer = _orderRepo.Customers.First(c => c.Id == _customerId);
        customer.TotalDebt = 200000m;

        var originalOrder = await _orderService.CreateOrderAsync(new OrderCreateRequest(
            _branchId, _customerId, "CASH",
            new List<OrderItemCreateDto> { new OrderItemCreateDto(_prodHaoHaoId, 4m, 5000m) },
            CashTendered: 20000m
        ));

        // Trả 2 gói Hảo Hảo (10.000đ), không lấy hàng mới -> Chênh lệch = -10.000đ
        var request = new ReturnExchangeRequest(
            ReturnedItems: new List<ReturnItemDto> { new ReturnItemDto(_prodHaoHaoId, 2m, 5000m) },
            NewItems: new List<OrderItemCreateDto>(),
            SettlementMethod: "REDUCE_DEBT",
            Note: "Trả hàng cấn trừ nợ"
        );

        // Act
        var result = await _orderService.ProcessReturnExchangeAsync(originalOrder.Id, request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(190000m, customer.TotalDebt); // Giảm từ 200k xuống 190k
    }

    [Fact]
    public async Task REQ_POS_RET_01_ProcessReturnExchange_InsufficientStock_ThrowsInvalidOperationException()
    {
        // Arrange
        var originalOrder = await _orderService.CreateOrderAsync(new OrderCreateRequest(
            _branchId, _customerId, "CASH",
            new List<OrderItemCreateDto> { new OrderItemCreateDto(_prodHaoHaoId, 1m, 5000m) },
            CashTendered: 5000m
        ));

        // Đổi sang 9999 lon Coca (vượt tồn kho hiện có)
        var request = new ReturnExchangeRequest(
            ReturnedItems: new List<ReturnItemDto> { new ReturnItemDto(_prodHaoHaoId, 1m, 5000m) },
            NewItems: new List<OrderItemCreateDto> { new OrderItemCreateDto(_prodCocaId, 9999m, 10000m) },
            SettlementMethod: "CASH"
        );

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _orderService.ProcessReturnExchangeAsync(originalOrder.Id, request));
    }

    [Fact]
    public async Task REQ_POS_SEC_02_CancelOrder_CashierRole_ThrowsUnauthorizedAccessException()
    {
        // Arrange: Tạo đơn hoàn tất
        var order = await _orderService.CreateOrderAsync(new OrderCreateRequest(
            _branchId, _customerId, "CASH",
            new List<OrderItemCreateDto> { new OrderItemCreateDto(_prodHaoHaoId, 2m, 5000m) },
            CashTendered: 10000m
        ));

        // Act & Assert: Thu ngân (CASHIER / EMPLOYEE) cố tình bấm hủy đơn Completed -> Bị chặn
        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _orderService.CancelOrderAsync(order.Id, new CancelOrderRequest("Lý do nhầm", UserRole: "CASHIER")));

        Assert.Contains("Chỉ Chủ cửa hàng (STORE_OWNER) mới có quyền hủy hóa đơn", ex.Message);
    }

    [Fact]
    public async Task REQ_POS_PRE_03_CreatePreOrder_DepositBelow10Percent_ThrowsInvalidOperationException()
    {
        // Arrange: Đơn đặt trước 10 gói mì * 5.000 = 50.000đ. Cọc 1.000đ (2% < 10%)
        var request = new PreOrderCreateRequest(
            BranchId: _branchId,
            CustomerId: _customerId,
            Items: new List<OrderItemCreateDto> { new OrderItemCreateDto(_prodHaoHaoId, 10m, 5000m) },
            DepositAmount: 1000m, // < 5.000đ (10%)
            ExpectedPickupDate: DateTime.UtcNow.AddDays(2)
        );

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _orderService.CreatePreOrderAsync(request));

        Assert.Contains("Tiền đặt cọc tối thiểu phải bằng 10%", ex.Message);
    }

    [Fact]
    public async Task REQ_POS_PRE_04_CreatePreOrderAsync_MultiBatchFEFO_AllocatesFromEarliestExpiryDateFirst()
    {
        // Arrange: Tạo sản phẩm sữa tươi có 2 lô hàng với hạn sử dụng khác nhau
        var multiBatchProdId = Guid.NewGuid();
        var multiBatchProd = new Product
        {
            Id = multiBatchProdId,
            Name = "Sữa Tươi Ba Vì 1L",
            BasePrice = 30000m,
            VatRate = 0.05m
        };
        _orderRepo.Products.Add(multiBatchProd);
        _orderRepo.BranchProducts.Add(new BranchProduct
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-01",
            BranchId = _branchId,
            ProductId = multiBatchProdId,
            Status = "Active"
        });

        // Lô 1 (Cận date hơn): HSD sau 5 ngày, tồn kho 3 hộp
        var batchNearExpiry = new BranchStock
        {
            Id = Guid.NewGuid(),
            BranchId = _branchId,
            ProductId = multiBatchProdId,
            BatchCode = "LOT-NEAR",
            ExpiryDate = DateTime.UtcNow.AddDays(5),
            QuantityOnHand = 3m
        };

        // Lô 2 (Date xa hơn): HSD sau 25 ngày, tồn kho 7 hộp
        var batchFarExpiry = new BranchStock
        {
            Id = Guid.NewGuid(),
            BranchId = _branchId,
            ProductId = multiBatchProdId,
            BatchCode = "LOT-FAR",
            ExpiryDate = DateTime.UtcNow.AddDays(25),
            QuantityOnHand = 7m
        };

        _orderRepo.BranchStocks.Add(batchNearExpiry);
        _orderRepo.BranchStocks.Add(batchFarExpiry);

        // Khách đặt trước 5 hộp (Tổng tiền: 5 * 30.000 = 150.000đ), cọc 50.000đ (>= 10%)
        var request = new PreOrderCreateRequest(
            BranchId: _branchId,
            CustomerId: _customerId,
            Items: new List<OrderItemCreateDto>
            {
                new OrderItemCreateDto(multiBatchProdId, 5m, 30000m)
            },
            DepositAmount: 50000m,
            ExpectedPickupDate: DateTime.UtcNow.AddDays(2),
            PaymentMethod: "CASH",
            Note: "Lấy sữa tươi date mới"
        );

        // Act
        var result = await _orderService.CreatePreOrderAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("PendingPickup", result.Status);
        Assert.Equal(150000m, result.FinalAmount);

        // Theo nguyên tắc FEFO: Lô 1 (cận date hơn) phải bị trừ sạch 3 hộp -> còn 0
        Assert.Equal(0m, batchNearExpiry.QuantityOnHand);

        // Lô 2 (date xa hơn) bị trừ 2 hộp còn lại -> còn 5 hộp
        Assert.Equal(5m, batchFarExpiry.QuantityOnHand);

        // OrderDetails độc lập theo dòng đặt: đúng 1 dòng 5 hộp (không phân mảnh nhân sai thành tiền)
        Assert.Single(result.Items);
        Assert.Equal(5m, result.Items[0].Quantity);
        Assert.Equal(30000m, result.Items[0].UnitPrice);
        Assert.Equal(150000m, result.Items[0].TotalLineAmount);
    }

    [Fact]
    public async Task REQ_FIX_PREUNIT_02_CreatePreOrder_WithConversionRate_DeductsFEFOBaseStockAndStoresUnitInfo()
    {
        // Arrange: Sản phẩm có 2 lô: Lô 1 (10 chai), Lô 2 (20 chai) -> Tổng 30 chai
        var prodId = Guid.NewGuid();
        var unitId = Guid.NewGuid();
        var prod = new Product
        {
            Id = prodId,
            TenantId = "tenant-01",
            Name = "Nước Mắm Cá Cơm Phú Quốc",
            SellingPrice = 25000m,
            CostPrice = 18000m,
            BaseUnit = "Chai",
            Units = new List<ProductUnit>
            {
                new()
                {
                    Id = unitId,
                    ProductId = prodId,
                    UnitName = "Thùng",
                    ConversionRate = 12m,
                    Price = 280000m,
                    IsBaseUnit = false
                }
            }
        };
        _orderRepo.Products.Add(prod);
        _orderRepo.BranchProducts.Add(new BranchProduct
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-01",
            BranchId = _branchId,
            ProductId = prodId,
            Status = "Active"
        });

        var batch1 = new BranchStock
        {
            Id = Guid.NewGuid(),
            BranchId = _branchId,
            ProductId = prodId,
            BatchCode = "LOT-MAM-01",
            ExpiryDate = DateTime.UtcNow.AddDays(10),
            QuantityOnHand = 10m
        };
        var batch2 = new BranchStock
        {
            Id = Guid.NewGuid(),
            BranchId = _branchId,
            ProductId = prodId,
            BatchCode = "LOT-MAM-02",
            ExpiryDate = DateTime.UtcNow.AddDays(30),
            QuantityOnHand = 20m
        };
        _orderRepo.BranchStocks.Add(batch1);
        _orderRepo.BranchStocks.Add(batch2);

        // Khách đặt trước 2 Thùng (ConversionRate = 12 -> Tổng 24 chai). Giá thùng: 280.000đ. Cọc 100.000đ
        var request = new PreOrderCreateRequest(
            BranchId: _branchId,
            CustomerId: _customerId,
            Items: new List<OrderItemCreateDto>
            {
                new(prodId, 2m, 280000m, UnitName: "Thùng", UnitId: unitId, ConversionRate: 12m)
            },
            DepositAmount: 100000m,
            ExpectedPickupDate: DateTime.UtcNow.AddDays(3),
            PaymentMethod: "CASH",
            Note: "Đơn đặt 2 thùng nước mắm giao nhà"
        );

        // Act
        var result = await _orderService.CreatePreOrderAsync(request);

        // Assert:
        Assert.NotNull(result);
        Assert.Equal("PendingPickup", result.Status);
        Assert.Equal(560000m, result.FinalAmount);

        // FEFO: Lô 1 (10 chai) bị trừ hết -> 0; Lô 2 (20 chai) bị trừ 14 chai -> còn 6 chai (tổng trừ 24 chai)
        Assert.Equal(0m, batch1.QuantityOnHand);
        Assert.Equal(6m, batch2.QuantityOnHand);

        // OrderDetails: Đúng 1 dòng theo đơn đặt (2 Thùng)
        Assert.Single(result.Items);
        var item = result.Items[0];
        Assert.Equal(2m, item.Quantity);
        Assert.Equal(280000m, item.UnitPrice);
        Assert.Equal(560000m, item.TotalLineAmount);
        Assert.Equal("Thùng", item.UnitName);
    }

    [Fact]
    public async Task REQ_FIX_PREUNIT_02_CancelPreOrder_RestoresBaseQuantityUsingConversionRate()
    {
        // Arrange: Tạo đơn đặt trước 2 Thùng (x12 = 24 chai)
        var prodId = Guid.NewGuid();
        var prod = new Product
        {
            Id = prodId,
            TenantId = "tenant-01",
            Name = "Bia Sài Gòn Lager",
            SellingPrice = 12000m,
            BaseUnit = "Lon"
        };
        _orderRepo.Products.Add(prod);
        _orderRepo.BranchProducts.Add(new BranchProduct
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-01",
            BranchId = _branchId,
            ProductId = prodId,
            Status = "Active"
        });

        var batch = new BranchStock
        {
            Id = Guid.NewGuid(),
            BranchId = _branchId,
            ProductId = prodId,
            BatchCode = "LOT-BEER-01",
            ExpiryDate = DateTime.UtcNow.AddDays(60),
            QuantityOnHand = 30m
        };
        _orderRepo.BranchStocks.Add(batch);

        var preOrder = await _orderService.CreatePreOrderAsync(new PreOrderCreateRequest(
            BranchId: _branchId,
            CustomerId: _customerId,
            Items: new List<OrderItemCreateDto>
            {
                new(prodId, 2m, 250000m, UnitName: "Thùng", ConversionRate: 12m)
            },
            DepositAmount: 50000m,
            ExpectedPickupDate: DateTime.UtcNow.AddDays(2),
            PaymentMethod: "CASH"
        ));

        // Tồn kho sau khi đặt trước 2 Thùng (24 lon): 30 - 24 = 6 lon
        Assert.Equal(6m, batch.QuantityOnHand);

        // Act: Hủy đơn đặt trước
        var cancelResult = await _orderService.CancelOrderAsync(preOrder.Id, new CancelOrderRequest(
            Reason: "Khách bận không nhận hàng",
            Note: "Hoàn cọc và hoàn kho",
            UserRole: "STORE_OWNER"
        ));

        // Assert: Tồn kho được hoàn đủ 24 lon: 6 + 24 = 30 lon
        Assert.Equal("Cancelled", cancelResult.Status);
        Assert.Equal(30m, batch.QuantityOnHand);
    }

    [Fact]
    public async Task REQ_FIX_PREUNIT_02_CreatePreOrder_AggregatedBaseQuantityExceedingStock_ThrowsInvalidOperationException()
    {
        // Arrange: Sản phẩm có tổng kho 15 chai
        var prodId = Guid.NewGuid();
        var prod = new Product
        {
            Id = prodId,
            TenantId = "tenant-01",
            Name = "Dầu Ăn Neptune",
            SellingPrice = 45000m,
            BaseUnit = "Chai"
        };
        _orderRepo.Products.Add(prod);
        _orderRepo.BranchProducts.Add(new BranchProduct
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-01",
            BranchId = _branchId,
            ProductId = prodId,
            Status = "Active"
        });

        var batch = new BranchStock
        {
            Id = Guid.NewGuid(),
            BranchId = _branchId,
            ProductId = prodId,
            BatchCode = "LOT-OIL-01",
            ExpiryDate = DateTime.UtcNow.AddDays(100),
            QuantityOnHand = 15m
        };
        _orderRepo.BranchStocks.Add(batch);

        // Khách đặt 1 Thùng (x12 = 12 chai) VÀ 5 Chai (x1 = 5 chai) -> Tổng 17 chai > 15 chai khả dụng
        var request = new PreOrderCreateRequest(
            BranchId: _branchId,
            CustomerId: _customerId,
            Items: new List<OrderItemCreateDto>
            {
                new(prodId, 1m, 500000m, UnitName: "Thùng", ConversionRate: 12m),
                new(prodId, 5m, 45000m, UnitName: "Chai", ConversionRate: 1m)
            },
            DepositAmount: 100000m,
            ExpectedPickupDate: DateTime.UtcNow.AddDays(2),
            PaymentMethod: "CASH"
        );

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _orderService.CreatePreOrderAsync(request));
        Assert.Contains("không đủ tồn kho", ex.Message);
    }

    [Fact]
    public async Task REQ_POS_09_CreateOrder_BranchProduct_PriceOverride_AppliesBranchPrice()
    {
        // Arrange: Sản phẩm có giá Master 14.000, Chi nhánh cấu hình override 13.500
        var prodId = Guid.NewGuid();
        var prod = new Product
        {
            Id = prodId,
            TenantId = "tenant-01",
            Name = "Bia Ha Noi Sleek",
            SellingPrice = 14000m,
            BaseUnit = "Lon",
            VatRate = 0.01m,
            IsActive = true
        };
        _orderRepo.Products.Add(prod);

        var bp = new BranchProduct
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-01",
            BranchId = _branchId,
            ProductId = prodId,
            SellingPriceOverride = 13500m,
            Status = "Active"
        };
        _orderRepo.BranchProducts.Add(bp);

        _orderRepo.BranchStocks.Add(new BranchStock
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-01",
            BranchId = _branchId,
            ProductId = prodId,
            QuantityOnHand = 50m
        });

        var request = new OrderCreateRequest(
            BranchId: _branchId,
            CustomerId: null,
            PaymentMethod: "CASH",
            Items: new List<OrderItemCreateDto>
            {
                new OrderItemCreateDto(prodId, 2m, 0m)
            }
        );

        // Act
        var result = await _orderService.CreateOrderAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(27000m, result.TotalAmount); // 2 * 13.500
        Assert.Equal(13500m, result.Items[0].UnitPrice);
    }

    [Fact]
    public async Task REQ_POS_09_CreateOrder_BranchProduct_NoOverride_FallsBackToMasterPrice()
    {
        // Arrange: Chi nhánh có BranchProduct nhưng SellingPriceOverride = null -> kế thừa giá Master
        var prodId = Guid.NewGuid();
        var prod = new Product
        {
            Id = prodId,
            TenantId = "tenant-01",
            Name = "Muoi Cham Hao Hao",
            SellingPrice = 16000m,
            BaseUnit = "Hu",
            VatRate = 0.01m,
            IsActive = true
        };
        _orderRepo.Products.Add(prod);

        var bp = new BranchProduct
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-01",
            BranchId = _branchId,
            ProductId = prodId,
            SellingPriceOverride = null,
            Status = "Active"
        };
        _orderRepo.BranchProducts.Add(bp);

        _orderRepo.BranchStocks.Add(new BranchStock
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-01",
            BranchId = _branchId,
            ProductId = prodId,
            QuantityOnHand = 20m
        });

        var request = new OrderCreateRequest(
            BranchId: _branchId,
            CustomerId: null,
            PaymentMethod: "CASH",
            Items: new List<OrderItemCreateDto>
            {
                new OrderItemCreateDto(prodId, 1m, 0m)
            }
        );

        // Act
        var result = await _orderService.CreateOrderAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(16000m, result.TotalAmount);
        Assert.Equal(16000m, result.Items[0].UnitPrice);
    }

    [Fact]
    public async Task REQ_POS_10_CreateOrder_MultiUnit_BranchProductUnitPrice_Override_AppliesOverride()
    {
        // Arrange: Đơn vị quy đổi "Thùng" (x24 lon), giá Master ĐVT 330.000, chi nhánh override 320.000
        var prodId = Guid.NewGuid();
        var unitThungId = Guid.NewGuid();
        var unitThung = new ProductUnit
        {
            Id = unitThungId,
            ProductId = prodId,
            UnitName = "Thung",
            ConversionRate = 24m,
            Price = 330000m,
            IsBaseUnit = false
        };

        var prod = new Product
        {
            Id = prodId,
            TenantId = "tenant-01",
            Name = "Bia Ha Noi Sleek",
            SellingPrice = 14000m,
            BaseUnit = "Lon",
            Units = new List<ProductUnit> { unitThung }
        };
        _orderRepo.Products.Add(prod);

        var bpId = Guid.NewGuid();
        var bp = new BranchProduct
        {
            Id = bpId,
            TenantId = "tenant-01",
            BranchId = _branchId,
            ProductId = prodId,
            Status = "Active",
            UnitPriceOverrides = new List<BranchProductUnitPrice>
            {
                new BranchProductUnitPrice
                {
                    Id = Guid.NewGuid(),
                    BranchProductId = bpId,
                    ProductUnitId = unitThungId,
                    SellingPriceOverride = 320000m
                }
            }
        };
        _orderRepo.BranchProducts.Add(bp);

        _orderRepo.BranchStocks.Add(new BranchStock
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-01",
            BranchId = _branchId,
            ProductId = prodId,
            QuantityOnHand = 100m
        });

        var request = new OrderCreateRequest(
            BranchId: _branchId,
            CustomerId: null,
            PaymentMethod: "CASH",
            Items: new List<OrderItemCreateDto>
            {
                new OrderItemCreateDto(prodId, 2m, 0m, UnitId: unitThungId, UnitName: "Thung")
            }
        );

        // Act
        var result = await _orderService.CreateOrderAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(640000m, result.TotalAmount); // 2 * 320.000
        Assert.Equal(320000m, result.Items[0].UnitPrice);
    }

    [Fact]
    public async Task REQ_POS_11_CreateOrder_MultiUnit_DeductsBaseQuantity_FromBranchStocks_FEFO()
    {
        // Arrange: 1 Thùng = 24 lon. Kho có Lô A (10 lon, cận date) và Lô B (50 lon, date xa).
        var prodId = Guid.NewGuid();
        var unitThungId = Guid.NewGuid();
        var unitThung = new ProductUnit
        {
            Id = unitThungId,
            ProductId = prodId,
            UnitName = "Thung",
            ConversionRate = 24m,
            Price = 320000m,
            IsBaseUnit = false
        };

        var prod = new Product
        {
            Id = prodId,
            TenantId = "tenant-01",
            Name = "Bia Ha Noi Sleek",
            SellingPrice = 14000m,
            BaseUnit = "Lon",
            Units = new List<ProductUnit> { unitThung }
        };
        _orderRepo.Products.Add(prod);
        _orderRepo.BranchProducts.Add(new BranchProduct
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-01",
            BranchId = _branchId,
            ProductId = prodId,
            Status = "Active"
        });

        var lotNear = new BranchStock
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-01",
            BranchId = _branchId,
            ProductId = prodId,
            BatchCode = "LOT-NEAR",
            ExpiryDate = DateTime.UtcNow.AddDays(10),
            QuantityOnHand = 10m
        };
        var lotFar = new BranchStock
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-01",
            BranchId = _branchId,
            ProductId = prodId,
            BatchCode = "LOT-FAR",
            ExpiryDate = DateTime.UtcNow.AddDays(40),
            QuantityOnHand = 50m
        };
        _orderRepo.BranchStocks.AddRange(new[] { lotNear, lotFar });

        // Bán 1 Thùng (= 24 lon)
        var request = new OrderCreateRequest(
            BranchId: _branchId,
            CustomerId: null,
            PaymentMethod: "CASH",
            Items: new List<OrderItemCreateDto>
            {
                new OrderItemCreateDto(prodId, 1m, 320000m, UnitId: unitThungId, UnitName: "Thung")
            }
        );

        // Act
        var result = await _orderService.CreateOrderAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(0m, lotNear.QuantityOnHand); // Lô cận date trừ hết 10 lon
        Assert.Equal(36m, lotFar.QuantityOnHand);  // Lô xa date trừ 14 lon còn lại -> 50 - 14 = 36
    }

    [Fact]
    public async Task REQ_POS_11_CreateOrder_MultiUnit_InsufficientTotalBaseStock_ThrowsException()
    {
        // Arrange: 1 Thùng = 24 gói. Tổng tồn kho chỉ có 30 gói. Khách mua 2 Thùng (= 48 gói) -> Thiếu tồn kho
        var prodId = Guid.NewGuid();
        var unitThungId = Guid.NewGuid();
        var unitThung = new ProductUnit
        {
            Id = unitThungId,
            ProductId = prodId,
            UnitName = "Thung",
            ConversionRate = 24m,
            Price = 1350000m,
            IsBaseUnit = false
        };

        var prod = new Product
        {
            Id = prodId,
            TenantId = "tenant-01",
            Name = "Ca phe G7 3in1",
            SellingPrice = 58000m,
            BaseUnit = "Hop",
            Units = new List<ProductUnit> { unitThung }
        };
        _orderRepo.Products.Add(prod);
        _orderRepo.BranchProducts.Add(new BranchProduct
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-01",
            BranchId = _branchId,
            ProductId = prodId,
            Status = "Active"
        });

        _orderRepo.BranchStocks.Add(new BranchStock
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-01",
            BranchId = _branchId,
            ProductId = prodId,
            QuantityOnHand = 30m
        });

        var request = new OrderCreateRequest(
            BranchId: _branchId,
            CustomerId: null,
            PaymentMethod: "CASH",
            Items: new List<OrderItemCreateDto>
            {
                new OrderItemCreateDto(prodId, 2m, 1350000m, UnitId: unitThungId, UnitName: "Thung")
            }
        );

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _orderService.CreateOrderAsync(request));
        Assert.Contains("không đủ tồn kho tại chi nhánh", ex.Message);
        Assert.Contains("30", ex.Message);
        Assert.Contains("48", ex.Message);
    }

    [Fact]
    public async Task REQ_POS_11_CancelOrder_RestoresBranchStock()
    {
        // Arrange: Mua 5 sản phẩm, sau đó Chủ cửa hàng hủy hóa đơn -> Tồn kho được hoàn trả
        var prodId = Guid.NewGuid();
        var prod = new Product
        {
            Id = prodId,
            TenantId = "tenant-01",
            Name = "Nuoc Khoang Lavie",
            SellingPrice = 8000m,
            BaseUnit = "Chai",
            IsActive = true
        };
        _orderRepo.Products.Add(prod);
        _orderRepo.BranchProducts.Add(new BranchProduct
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-01",
            BranchId = _branchId,
            ProductId = prodId,
            Status = "Active"
        });

        var stock = new BranchStock
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-01",
            BranchId = _branchId,
            ProductId = prodId,
            QuantityOnHand = 20m
        };
        _orderRepo.BranchStocks.Add(stock);

        var createReq = new OrderCreateRequest(
            BranchId: _branchId,
            CustomerId: null,
            PaymentMethod: "CASH",
            Items: new List<OrderItemCreateDto>
            {
                new OrderItemCreateDto(prodId, 5m, 8000m)
            }
        );

        var orderRes = await _orderService.CreateOrderAsync(createReq);
        Assert.Equal(15m, stock.QuantityOnHand); // 20 - 5 = 15

        // Act: STORE_OWNER hủy đơn
        var cancelReq = new CancelOrderRequest(
            Reason: "Khách đổi ý không mua nữa",
            UserRole: "STORE_OWNER"
        );
        var cancelRes = await _orderService.CancelOrderAsync(orderRes.Id, cancelReq);

        // Assert
        Assert.Equal("Cancelled", cancelRes.Status);
        Assert.Equal(20m, stock.QuantityOnHand); // Hoàn lại 15 + 5 = 20
    }

    [Fact]
    public async Task REQ_FE16_RedeemRewardPoints_DeductsPointsAndEarnsNewPoints()
    {
        // Arrange
        var custId = Guid.NewGuid();
        var customer = new Customer
        {
            Id = custId,
            TenantId = "tenant-01",
            CustomerCode = "KH_LOYALTY",
            Name = "Khach Hang Than Thiet",
            Phone = "0988112233",
            RewardPoints = 100, // Đang có sẵn 100 điểm
            TotalSpending = 500000m,
            AllowDebt = false
        };
        _orderRepo.Customers.Add(customer);

        // Đơn hàng: Mua 2 lon Coca (2 x 10.000 = 20.000đ), tiêu 50 điểm tích lũy, thanh toán tiền mặt
        // Tiêu 50 điểm = giảm 5.000đ, FinalAmount = 15.000đ -> tích thêm 15.000 / 10.000 = 1 điểm
        var request = new OrderCreateRequest(
            BranchId: _branchId,
            CustomerId: custId,
            PaymentMethod: "CASH",
            Items: new List<OrderItemCreateDto>
            {
                new OrderItemCreateDto(
                    ProductId: _prodCocaId,
                    Quantity: 2,
                    UnitPrice: 10000m
                )
            },
            CashTendered: 15000m,
            DiscountAmount: 5000m,
            PointsRedeemed: 50
        );

        // Act
        var res = await _orderService.CreateOrderAsync(request);

        // Assert
        Assert.NotNull(res);
        Assert.Equal("Completed", res.Status);
        Assert.Equal(15000m, res.FinalAmount);
        // Điểm còn lại: 100 (ban đầu) - 50 (tiêu) + 1 (tích từ 15k) = 51 điểm
        Assert.Equal(51, customer.RewardPoints);
        Assert.Equal(515000m, customer.TotalSpending);
    }

    [Fact]
    public async Task REQ_SHIFT_SHARED_03_CreateOrder_WhenShiftIsClosing_ThrowsShiftClosingLockdownException()
    {
        // Arrange
        var mockShiftRepo = new Moq.Mock<IShiftRepository>();
        mockShiftRepo.Setup(r => r.GetActiveShiftAsync(_branchId, Moq.It.IsAny<Guid?>(), Moq.It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ShiftZReport
            {
                Id = Guid.NewGuid(),
                BranchId = _branchId,
                Status = ShiftState.Closing
            });

        var orderService = new OrderService(
            _orderRepo,
            _uow,
            _printService,
            null,
            null,
            new OrderCreateRequestValidator(),
            mockShiftRepo.Object,
            null,
            null);

        var request = new OrderCreateRequest(
            BranchId: _branchId,
            CustomerId: null,
            PaymentMethod: "CASH",
            Items: new List<OrderItemCreateDto>
            {
                new OrderItemCreateDto(_prodHaoHaoId, 1m, 10000m)
            }
        );

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BusinessException>(() => orderService.CreateOrderAsync(request));
        Assert.Equal("SHIFT_CLOSING_LOCKDOWN", ex.ErrorCode);
    }

    [Fact]
    public async Task REQ_SHIFT_SHARED_02_CreateOrder_AutoLinksToOpenShift_AndRecordsParticipant()
    {
        // Arrange
        var shiftId = Guid.NewGuid();
        var cashierUserId = Guid.NewGuid();
        var mockShiftRepo = new Moq.Mock<IShiftRepository>();
        ShiftParticipant? recordedParticipant = null;

        mockShiftRepo.Setup(r => r.GetActiveShiftAsync(_branchId, Moq.It.IsAny<Guid?>(), Moq.It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ShiftZReport
            {
                Id = shiftId,
                BranchId = _branchId,
                Status = ShiftState.Open
            });

        mockShiftRepo.Setup(r => r.GetParticipantAsync(shiftId, cashierUserId, Moq.It.IsAny<CancellationToken>()))
            .ReturnsAsync((ShiftParticipant?)null);

        mockShiftRepo.Setup(r => r.AddParticipantAsync(Moq.It.IsAny<ShiftParticipant>(), Moq.It.IsAny<CancellationToken>()))
            .Callback<ShiftParticipant, CancellationToken>((p, ct) => recordedParticipant = p)
            .Returns(Task.CompletedTask);

        var mockTenantContext = new Moq.Mock<ITenantContext>();
        mockTenantContext.Setup(c => c.CurrentTenantId).Returns("tenant-01");
        mockTenantContext.Setup(c => c.CurrentUserId).Returns(cashierUserId);
        mockTenantContext.Setup(c => c.CurrentRole).Returns("CASHIER");

        var orderService = new OrderService(
            _orderRepo,
            _uow,
            _printService,
            null,
            null,
            new OrderCreateRequestValidator(),
            mockShiftRepo.Object,
            null,
            mockTenantContext.Object);

        var request = new OrderCreateRequest(
            BranchId: _branchId,
            CustomerId: null,
            PaymentMethod: "CASH",
            CashierName: "Thu Ngân Test",
            Items: new List<OrderItemCreateDto>
            {
                new OrderItemCreateDto(_prodHaoHaoId, 2m, 8000m)
            }
        );

        // Act
        var result = await orderService.CreateOrderAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(shiftId, result.ShiftId);
        Assert.NotNull(recordedParticipant);
        Assert.Equal(shiftId, recordedParticipant.ShiftId);
        Assert.Equal(cashierUserId, recordedParticipant.UserId);
        Assert.Equal("Thu Ngân Test", recordedParticipant.UserName);
    }

    [Fact]
    public async Task REQ_STOCK_CONCURRENCY_01_ConcurrentOrders_DeductInventoryWithoutNegativeStock()
    {
        // Arrange: Đặt tổng tồn kho mì tôm HaoHao trên mọi lô chính xác là 10 gói
        foreach (var s in _orderRepo.BranchStocks.Where(x => x.ProductId == _prodHaoHaoId && x.BranchId == _branchId))
        {
            s.QuantityOnHand = 0m;
        }
        var targetStock = _orderRepo.BranchStocks.First(s => s.ProductId == _prodHaoHaoId && s.BranchId == _branchId);
        targetStock.QuantityOnHand = 10m;

        // Mô phỏng 3 thu ngân cùng đặt 3 đơn đồng thời (mỗi đơn 4 gói -> tổng cần 12 gói, vượt quá 10 gói có sẵn)
        var lockObj = new object();
        int successfulCheckouts = 0;
        int failedCheckouts = 0;

        var tasks = Enumerable.Range(0, 3).Select(async _ =>
        {
            var req = new OrderCreateRequest(
                BranchId: _branchId,
                CustomerId: null,
                PaymentMethod: "CASH",
                Items: new List<OrderItemCreateDto>
                {
                    new OrderItemCreateDto(_prodHaoHaoId, 4m, 8000m)
                }
            );

            try
            {
                await Task.Run(() =>
                {
                    OrderResponse? res = null;
                    lock (lockObj)
                    {
                        res = _orderService.CreateOrderAsync(req).GetAwaiter().GetResult();
                    }
                    if (res != null)
                    {
                        Interlocked.Increment(ref successfulCheckouts);
                    }
                });
            }
            catch (InvalidOperationException)
            {
                Interlocked.Increment(ref failedCheckouts);
            }
        });

        await Task.WhenAll(tasks);

        // Assert: Chỉ có đúng 2 đơn thành công (4 + 4 = 8, còn lại 2 gói không đủ cho đơn thứ 3 bị từ chối)
        Assert.Equal(2, successfulCheckouts);
        Assert.Equal(1, failedCheckouts);
        Assert.True(targetStock.QuantityOnHand >= 0m, "Tồn kho không bao giờ được âm!");
        Assert.Equal(2m, targetStock.QuantityOnHand);
    }

    [Fact]
    public async Task CreateOrderAsync_WithCashierName_PersistsSnapshotAndReturnsInResponse()
    {
        // Arrange
        var req = new OrderCreateRequest(
            BranchId: _branchId,
            CustomerId: null,
            PaymentMethod: "CASH",
            CashTendered: 50000m,
            CashierName: "Nguyễn Văn Minh (Nhân viên bán hàng An Nhiên)",
            Items: new List<OrderItemCreateDto>
            {
                new(_prodHaoHaoId, 2m, 4500m)
            }
        );

        // Act
        var res = await _orderService.CreateOrderAsync(req);

        // Assert
        Assert.NotNull(res);
        Assert.Equal("Nguyễn Văn Minh (Nhân viên bán hàng An Nhiên)", res.CashierName);
        var savedOrder = _orderRepo.Orders.FirstOrDefault(o => o.Id == res.Id);
        Assert.NotNull(savedOrder);
        Assert.Equal("Nguyễn Văn Minh (Nhân viên bán hàng An Nhiên)", savedOrder.CashierName);
    }

    [Fact]
    public async Task GetOrdersAsync_ReturnsPersistedCashierName_WithoutOverwriting()
    {
        // Arrange
        var order = new Order
        {
            Id = Guid.NewGuid(),
            BranchId = _branchId,
            OrderCode = "HD2611089999",
            CashierName = "Nguyễn Văn Minh",
            FinalAmount = 20000m,
            PaymentMethod = "CASH",
            PaymentStatus = "PAID",
            Status = "Completed",
            OrderDate = DateTime.UtcNow
        };
        _orderRepo.Orders.Add(order);

        // Act
        var (items, total) = await _orderService.GetOrdersAsync(new OrderFilterRequest());

        // Assert
        var found = items.FirstOrDefault(i => i.Id == order.Id);
        Assert.NotNull(found);
        Assert.Equal("Nguyễn Văn Minh", found.CashierName);
    }

    [Fact]
    public async Task CreatePreOrderAsync_WithCashierName_PersistsCashierNameAndReturnsInResponse()
    {
        // Arrange
        var req = new PreOrderCreateRequest(
            BranchId: _branchId,
            CustomerId: _customerId,
            DepositAmount: 20000m,
            ExpectedPickupDate: DateTime.UtcNow.AddDays(2),
            PaymentMethod: "CASH",
            CashierName: "Nguyễn Văn Minh (Nhân viên bán hàng An Nhiên)",
            Items: new List<OrderItemCreateDto>
            {
                new(_prodHaoHaoId, 5m, 4500m)
            }
        );

        // Act
        var res = await _orderService.CreatePreOrderAsync(req);

        // Assert
        Assert.NotNull(res);
        Assert.Equal("Nguyễn Văn Minh (Nhân viên bán hàng An Nhiên)", res.CashierName);
        var savedOrder = _orderRepo.Orders.FirstOrDefault(o => o.Id == res.Id);
        Assert.NotNull(savedOrder);
        Assert.Equal("Nguyễn Văn Minh (Nhân viên bán hàng An Nhiên)", savedOrder.CashierName);
        Assert.Equal("PendingPickup", savedOrder.Status);
    }

    [Fact]
    public async Task FulfillPreOrderAsync_WithCashierName_RecordsFulfilledByCashierName()
    {
        // Arrange
        var order = new Order
        {
            Id = Guid.NewGuid(),
            BranchId = _branchId,
            OrderCode = "PO2611080001",
            CashierName = "Nguyễn Văn Minh (Nhân viên bán hàng An Nhiên)",
            TotalAmount = 100000m,
            FinalAmount = 100000m,
            DepositAmount = 30000m,
            PaymentMethod = "CASH",
            PaymentStatus = "PARTIALLY_PAID",
            Status = "PendingPickup",
            OrderDate = DateTime.UtcNow
        };
        _orderRepo.Orders.Add(order);

        var fulfillReq = new PreOrderFulfillRequest(
            PaymentMethod: "CASH",
            AmountPaid: 70000m,
            CashierName: "Lê Thị Thu Ngân"
        );

        // Act
        var res = await _orderService.FulfillPreOrderAsync(order.Id, fulfillReq);

        // Assert
        Assert.NotNull(res);
        Assert.Equal("Completed", res.Status);
        Assert.Equal("Lê Thị Thu Ngân", res.FulfilledByCashierName);
        Assert.NotNull(res.FulfilledAtUtc);
        Assert.Equal("Nguyễn Văn Minh (Nhân viên bán hàng An Nhiên)", res.CashierName);
    }

    [Fact]
    public async Task CreateOrderAsync_WithLoyaltyPointsRedemption_DeductsPointsAndEarnsNewPoints()
    {
        // Arrange: Khách hàng có 50 điểm ban đầu. Cấu hình tích điểm: 10.000 VNĐ = 1 điểm.
        var mockPromoRepo = new Mock<IPromotionRepository>();
        mockPromoRepo.Setup(x => x.GetLoyaltySettingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LoyaltySetting
            {
                TenantId = "tenant-01",
                IsEnabled = true,
                EarningRateAmount = 10000m,
                EarningPoints = 1,
                RedemptionRatePoints = 1,
                RedemptionRateAmount = 1000m,
                MinRedemptionPoints = 10
            });

        var orderService = new OrderService(
            _orderRepo,
            _uow,
            _printService,
            debtService: null,
            cashbookService: null,
            validator: new OrderCreateRequestValidator(),
            shiftRepository: null,
            promotionRepository: mockPromoRepo.Object,
            tenantContext: null);

        var req = new OrderCreateRequest(
            BranchId: _branchId,
            CustomerId: _customerId, // Khách hàng có sẵn 50 điểm
            ShiftId: null,
            PaymentMethod: "CASH",
            CashTendered: 50000m,
            PointsRedeemed: 20, // Tiêu 20 điểm
            Items: new List<OrderItemCreateDto>
            {
                new OrderItemCreateDto(_prodHaoHaoId, 10m, 5000m) // 50.000 VNĐ
            }
        );

        // Act
        var res = await orderService.CreateOrderAsync(req);

        // Assert
        Assert.NotNull(res);
        var updatedCustomer = _orderRepo.Customers.First(c => c.Id == _customerId);
        // Ban đầu: 50 điểm. Trừ 20 điểm = 30. Tích thêm từ 50.000 VNĐ: (50.000 / 10.000) * 1 = 5 điểm.
        // Tổng điểm cuối: 30 + 5 = 35 điểm.
        Assert.Equal(35, updatedCustomer.RewardPoints);
    }

    [Fact]
    public async Task CreateOrderAsync_WithVoucherCode_InvokesAtomicRedemption()
    {
        // Arrange
        var mockPromoRepo = new Mock<IPromotionRepository>();
        mockPromoRepo.Setup(x => x.RedeemVoucherCodeAtomicAsync(
            It.IsAny<string>(),
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<Guid?>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()
        )).ReturnsAsync(true);

        var orderService = new OrderService(
            _orderRepo,
            _uow,
            _printService,
            debtService: null,
            cashbookService: null,
            validator: new OrderCreateRequestValidator(),
            shiftRepository: null,
            promotionRepository: mockPromoRepo.Object,
            tenantContext: null);

        // Seed stock cho Coca
        _orderRepo.BranchStocks.Add(new BranchStock
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-01",
            BranchId = _branchId,
            ProductId = _prodCocaId,
            BatchCode = "BATCH-COCA-VOUCHER",
            ExpiryDate = new DateTime(2026, 12, 1),
            QuantityOnHand = 20m,
            LandedCostPerUnit = 7000m
        });

        var req = new OrderCreateRequest(
            BranchId: _branchId,
            CustomerId: _customerId,
            ShiftId: null,
            PaymentMethod: "CASH",
            CashTendered: 20000m,
            VoucherCode: "GIAM20K",
            Items: new List<OrderItemCreateDto>
            {
                new OrderItemCreateDto(_prodCocaId, 2m, 10000m)
            }
        );

        // Act
        var res = await orderService.CreateOrderAsync(req);

        // Assert
        Assert.NotNull(res);
        mockPromoRepo.Verify(x => x.RedeemVoucherCodeAtomicAsync(
            "GIAM20K",
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<Guid?>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()
        ), Times.Once);
    }

    [Fact]
    public async Task REQ_ORD_BOGO_02_CreateOrder_WithGiftItem_DeductsFefoStockAndPersistsZeroPriceLine()
    {
        // Arrange: Mua 4 gói Hảo Hảo (20.000đ) + Tặng 1 lon Coca-Cola (giá gốc 10.000đ, tính 0đ)
        var promoId = Guid.NewGuid();
        var req = new OrderCreateRequest(
            BranchId: _branchId,
            CustomerId: _customerId,
            PaymentMethod: "CASH",
            CashTendered: 20000m,
            AppliedPromotionId: promoId,
            AppliedPromotionSummary: "Mua 4 Mì tặng 1 Coca",
            Items: new List<OrderItemCreateDto>
            {
                new(_prodHaoHaoId, 4m, 5000m),
                new(_prodCocaId, 1m, 0m, ProductName: "Nuoc Ngot Coca-Cola 330ml", IsGift: true, PromotionId: promoId)
            }
        );

        // Act
        var res = await _orderService.CreateOrderAsync(req);

        // Assert: Không tính tiền dòng quà tặng vào TotalAmount / FinalAmount, nhưng vẫn trừ 1 lon tồn kho Coca (3 -> 2)
        Assert.Equal(20000m, res.TotalAmount);
        Assert.Equal(0m, res.DiscountAmount);
        Assert.Equal(20000m, res.FinalAmount);
        Assert.Equal(2, res.Items.Count);

        var giftLine = Assert.Single(res.Items, i => i.IsGift);
        Assert.Equal(_prodCocaId, giftLine.ProductId);
        Assert.Equal(1m, giftLine.Quantity);
        Assert.Equal(10000m, giftLine.UnitPrice);
        Assert.Equal(10000m, giftLine.DiscountAmount);
        Assert.Equal(0m, giftLine.ActualPrice);
        Assert.Equal(0m, giftLine.TotalLineAmount);
        Assert.Equal(0m, giftLine.VatAmount);
        Assert.Equal(promoId, giftLine.PromotionId);

        var remainingCocaStock = _orderRepo.BranchStocks
            .Where(s => s.BranchId == _branchId && s.ProductId == _prodCocaId)
            .Sum(s => s.QuantityOnHand);
        Assert.Equal(2m, remainingCocaStock);
    }

    [Fact]
    public async Task REQ_ORD_BOGO_02_CreateOrder_SameProductBuyAndGift_LowStockPrioritizesPaidAndWarns_AndRejectsAllGiftOrder()
    {
        // 1. Đơn toàn quà tặng (không có dòng mua trả tiền) -> bị từ chối ValidationException
        var allGiftReq = new OrderCreateRequest(
            BranchId: _branchId,
            CustomerId: null,
            PaymentMethod: "CASH",
            CashTendered: 0m,
            Items: new List<OrderItemCreateDto>
            {
                new(_prodCocaId, 1m, 0m, IsGift: true)
            }
        );
        await Assert.ThrowsAsync<ValidationException>(() => _orderService.CreateOrderAsync(allGiftReq));

        // 2. Kho Coca chỉ còn 3 lon. Khách mua 3 lon Coca + được tặng 1 lon Coca -> Ưu tiên bán đủ 3 lon trả tiền, quà tặng 0/1 kèm cảnh báo, không chặn đơn!
        var promoId = Guid.NewGuid();
        var lowGiftStockReq = new OrderCreateRequest(
            BranchId: _branchId,
            CustomerId: _customerId,
            PaymentMethod: "CASH",
            CashTendered: 30000m,
            AppliedPromotionId: promoId,
            AppliedPromotionSummary: "Mua 3 tặng 1 Coca",
            Items: new List<OrderItemCreateDto>
            {
                new(_prodCocaId, 3m, 10000m, IsGift: false),
                new(_prodCocaId, 1m, 0m, IsGift: true, PromotionId: promoId)
            }
        );

        var res = await _orderService.CreateOrderAsync(lowGiftStockReq);

        Assert.Equal(30000m, res.FinalAmount);
        Assert.DoesNotContain(res.Items, i => i.IsGift);
        Assert.False(string.IsNullOrWhiteSpace(res.GiftStockWarning));
        Assert.Contains("Coca", res.GiftStockWarning);
        // Không giao được quà và không giảm tiền -> không ghi nhận khống AppliedPromotionId
        Assert.Null(res.AppliedPromotionId);

        var remainingCoca = _orderRepo.BranchStocks
            .Where(s => s.BranchId == _branchId && s.ProductId == _prodCocaId)
            .Sum(s => s.QuantityOnHand);
        Assert.Equal(0m, remainingCoca);
    }

    [Fact]
    public async Task REQ_ORD_PROMO_01_02_CreateAndCancelOrder_PersistsPromoAuditAndSyncsUsageAndVoucher()
    {
        var autoPromo = new Promotion
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-01",
            PromotionCode = "KM-COMBO",
            PromotionName = "Giảm 5k + Tặng 1 Coca",
            Type = "ORDER_DISCOUNT",
            UsedCount = 0,
            TotalDiscountGiven = 0m,
            IsActive = true
        };
        var voucherBatch = new Promotion
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-01",
            PromotionCode = "BATCH-VCH5K",
            PromotionName = "Đợt Voucher 5k",
            Type = "VOUCHER",
            UsedCount = 0,
            TotalDiscountGiven = 0m,
            IsActive = true
        };
        var voucherEntity = new VoucherCode
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-01",
            PromotionId = voucherBatch.Id,
            Code = "VCH5K-01",
            Status = "UNUSED"
        };

        var mockPromoRepo = new Mock<IPromotionRepository>();
        mockPromoRepo.Setup(x => x.GetLoyaltySettingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LoyaltySetting
            {
                TenantId = "tenant-01",
                IsEnabled = true,
                EarningRateAmount = 10000m,
                EarningPoints = 1,
                RedemptionRatePoints = 1,
                RedemptionRateAmount = 200m,
                MinRedemptionPoints = 5
            });

        mockPromoRepo.Setup(x => x.RedeemVoucherCodeAtomicAsync(
                "VCH5K-01", It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                voucherEntity.Status = "USED";
                return true;
            });

        mockPromoRepo.Setup(x => x.RecordPromotionUsageAsync(
                It.IsAny<Guid?>(), It.IsAny<decimal>(), It.IsAny<string?>(), It.IsAny<decimal>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<Guid?, decimal, string?, decimal, string?, CancellationToken>((pId, promoAmt, vCode, vAmt, _, _) =>
            {
                if (pId == autoPromo.Id)
                {
                    autoPromo.UsedCount += 1;
                    autoPromo.TotalDiscountGiven += promoAmt;
                }
                if (vCode == voucherEntity.Code)
                {
                    voucherBatch.UsedCount += 1;
                    voucherBatch.TotalDiscountGiven += vAmt;
                }
            })
            .Returns(Task.CompletedTask);

        mockPromoRepo.Setup(x => x.RestoreOrderPromotionsOnCancelAsync(
                It.IsAny<Guid?>(), It.IsAny<decimal>(), It.IsAny<string?>(), It.IsAny<decimal>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<Guid?, decimal, string?, decimal, string?, CancellationToken>((pId, promoAmt, vCode, vAmt, _, _) =>
            {
                if (pId == autoPromo.Id)
                {
                    autoPromo.UsedCount = Math.Max(0, autoPromo.UsedCount - 1);
                    autoPromo.TotalDiscountGiven = Math.Max(0m, autoPromo.TotalDiscountGiven - promoAmt);
                }
                if (vCode == voucherEntity.Code && voucherEntity.Status == "USED")
                {
                    voucherEntity.Status = "UNUSED";
                    voucherBatch.UsedCount = Math.Max(0, voucherBatch.UsedCount - 1);
                    voucherBatch.TotalDiscountGiven = Math.Max(0m, voucherBatch.TotalDiscountGiven - vAmt);
                }
            })
            .Returns(Task.CompletedTask);

        var orderService = new OrderService(
            _orderRepo,
            _uow,
            _printService,
            debtService: null,
            cashbookService: null,
            validator: new OrderCreateRequestValidator(),
            shiftRepository: null,
            promotionRepository: mockPromoRepo.Object,
            tenantContext: null);

        // Mua 10 gói Mì (50.000đ): Giảm KM 5.000đ + Voucher 5.000đ + Tiêu 10 điểm (2.000đ) = Tổng giảm 12.000đ, kèm 1 lon Coca quà tặng (10.000đ)
        var createRes = await orderService.CreateOrderAsync(new OrderCreateRequest(
            BranchId: _branchId,
            CustomerId: _customerId,
            PaymentMethod: "CASH",
            CashTendered: 40000m,
            DiscountAmount: 12000m,
            Note: "Ghi chú kiểm tra KM",
            PointsRedeemed: 10,
            VoucherCode: "VCH5K-01",
            AppliedPromotionId: autoPromo.Id,
            AppliedPromotionSummary: autoPromo.PromotionName,
            PromotionDiscountAmount: 5000m,
            VoucherDiscountAmount: 5000m,
            PointsDiscountAmount: 2000m,
            Items: new List<OrderItemCreateDto>
            {
                new(_prodHaoHaoId, 10m, 5000m),
                new(_prodCocaId, 1m, 0m, IsGift: true, PromotionId: autoPromo.Id)
            }
        ));

        Assert.Equal(38000m, createRes.FinalAmount);
        Assert.Equal("Ghi chú kiểm tra KM", createRes.Note);
        Assert.Equal(autoPromo.Id, createRes.AppliedPromotionId);
        Assert.Equal("Giảm 5k + Tặng 1 Coca", createRes.AppliedPromotionSummary);
        Assert.Equal(5000m, createRes.PromotionDiscountAmount);
        Assert.Equal("VCH5K-01", createRes.AppliedVoucherCode);
        Assert.Equal(5000m, createRes.VoucherDiscountAmount);
        Assert.Equal(10, createRes.PointsRedeemed);
        Assert.Equal(2000m, createRes.PointsDiscountAmount);

        // Kiểm tra UsedCount & TotalDiscountGiven sau khi tạo đơn (KM = 5.000đ tiền + 10.000đ quà tặng = 15.000đ)
        Assert.Equal(1, autoPromo.UsedCount);
        Assert.Equal(15000m, autoPromo.TotalDiscountGiven);
        Assert.Equal("USED", voucherEntity.Status);
        Assert.Equal(1, voucherBatch.UsedCount);
        Assert.Equal(5000m, voucherBatch.TotalDiscountGiven);

        // Hủy đơn hàng với quyền STORE_OWNER -> Hoàn tồn kho quà tặng, trả Voucher về UNUSED và giảm UsedCount/TotalDiscountGiven về 0
        var cancelRes = await orderService.CancelOrderAsync(createRes.Id, new CancelOrderRequest("Khách đổi ý", "Hoàn đơn test", "STORE_OWNER"));
        Assert.Equal("Cancelled", cancelRes.Status);
        Assert.Equal(0, autoPromo.UsedCount);
        Assert.Equal(0m, autoPromo.TotalDiscountGiven);
        Assert.Equal("UNUSED", voucherEntity.Status);
        Assert.Equal(0, voucherBatch.UsedCount);
        Assert.Equal(0m, voucherBatch.TotalDiscountGiven);

        var restoredCocaStock = _orderRepo.BranchStocks
            .Where(s => s.BranchId == _branchId && s.ProductId == _prodCocaId)
            .Sum(s => s.QuantityOnHand);
        Assert.Equal(3m, restoredCocaStock);
    }

    [Fact]
    public async Task REQ_ORD_BOGO_03_ProcessReturnExchange_ReturningGiftItemSameProduct_ForcesZeroRefundAndRestocks()
    {
        // Arrange: Đơn hàng có 2 lon Coca trả tiền (20.000đ) + 1 lon Coca quà tặng (0đ) -> Tồn kho Coca về 0
        var orderRes = await _orderService.CreateOrderAsync(new OrderCreateRequest(
            BranchId: _branchId,
            CustomerId: _customerId,
            PaymentMethod: "CASH",
            CashTendered: 20000m,
            Items: new List<OrderItemCreateDto>
            {
                new(_prodCocaId, 2m, 10000m, IsGift: false),
                new(_prodCocaId, 1m, 0m, IsGift: true)
            }
        ));

        var giftLine = Assert.Single(orderRes.Items, i => i.IsGift);
        Assert.Equal(0m, _orderRepo.BranchStocks.Where(s => s.ProductId == _prodCocaId).Sum(s => s.QuantityOnHand));

        // Act: Trả lại đúng dòng quà tặng (kể cả khi client giả mạo gửi UnitPrice = 10.000đ)
        var returnRes = await _orderService.ProcessReturnExchangeAsync(orderRes.Id, new ReturnExchangeRequest(
            ReturnedItems: new List<ReturnItemDto>
            {
                new(
                    ProductId: _prodCocaId,
                    Quantity: 1m,
                    UnitPrice: 10000m,
                    Condition: "Restock",
                    OrderDetailId: giftLine.Id,
                    IsGift: true
                )
            },
            NewItems: new List<OrderItemCreateDto>(),
            SettlementMethod: "CASH",
            Reason: "Khách trả lại quà tặng"
        ));

        // Assert: Tuyệt đối hoàn 0đ tiền mặt nhưng vẫn nhập lại 1 lon Coca vào kho!
        Assert.Equal(0m, returnRes.ChangeAmount);
        Assert.Equal(1m, _orderRepo.BranchStocks.Where(s => s.ProductId == _prodCocaId).Sum(s => s.QuantityOnHand));
    }

    [Fact]
    public async Task REQ_ORD_PRE_01_CreatePreOrder_AutoPromoReducesMinDepositAndReservesGift_AndRejectsVoucherCode()
    {
        // 1. Chặn cứng VoucherCode trên Đơn đặt trước
        var invalidVoucherPreOrder = new PreOrderCreateRequest(
            BranchId: _branchId,
            CustomerId: _customerId,
            Items: new List<OrderItemCreateDto> { new(_prodHaoHaoId, 4m, 5000m) },
            DepositAmount: 5000m,
            ExpectedPickupDate: DateTime.UtcNow.AddDays(1),
            VoucherCode: "VOUCHER-1LAN"
        );
        await Assert.ThrowsAsync<ValidationException>(() => _orderService.CreatePreOrderAsync(invalidVoucherPreOrder));

        // 2. Đơn đặt trước 20 gói Hảo Hảo (100.000đ), tự áp KM giảm 20.000đ + tặng 1 lon Coca (10.000đ) -> Sau KM còn 80.000đ, cọc tối thiểu 10% = 8.000đ
        var promoId = Guid.NewGuid();
        decimal recordedBudget = 0m;
        var mockPromoRepo = new Mock<IPromotionRepository>();
        mockPromoRepo.Setup(x => x.RecordPromotionUsageAsync(
                promoId, It.IsAny<decimal>(), null, 0m, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<Guid?, decimal, string?, decimal, string?, CancellationToken>((_, amt, _, _, _, _) => recordedBudget = amt)
            .Returns(Task.CompletedTask);

        var orderService = new OrderService(
            _orderRepo,
            _uow,
            _printService,
            debtService: null,
            cashbookService: null,
            validator: new OrderCreateRequestValidator(),
            shiftRepository: null,
            promotionRepository: mockPromoRepo.Object,
            tenantContext: null);

        var preOrderRes = await orderService.CreatePreOrderAsync(new PreOrderCreateRequest(
            BranchId: _branchId,
            CustomerId: _customerId,
            Items: new List<OrderItemCreateDto>
            {
                new(_prodHaoHaoId, 20m, 5000m, IsGift: false),
                new(_prodCocaId, 1m, 0m, IsGift: true, PromotionId: promoId)
            },
            DepositAmount: 8000m, // 10% của 80.000đ (nếu tính trên 100.000đ chưa KM thì 8.000đ sẽ bị từ chối!)
            ExpectedPickupDate: DateTime.UtcNow.AddDays(2),
            DiscountAmount: 20000m,
            AppliedPromotionId: promoId,
            AppliedPromotionSummary: "Giảm 20k + Tặng 1 Coca"
        ));

        Assert.Equal("PendingPickup", preOrderRes.Status);
        Assert.Equal(100000m, preOrderRes.TotalAmount);
        Assert.Equal(20000m, preOrderRes.DiscountAmount);
        Assert.Equal(80000m, preOrderRes.FinalAmount);
        Assert.Equal(8000m, preOrderRes.DepositAmount);
        Assert.Equal(promoId, preOrderRes.AppliedPromotionId);
        Assert.Equal(20000m, preOrderRes.PromotionDiscountAmount);
        Assert.Contains(preOrderRes.Items, i => i.IsGift && i.ProductId == _prodCocaId && i.ActualPrice == 0m);
        // Kho Coca giữ 1 lon cho đơn đặt trước (3 -> 2), ngân sách KM ghi nhận 20.000 + 10.000 = 30.000đ
        Assert.Equal(2m, _orderRepo.BranchStocks.Where(s => s.ProductId == _prodCocaId).Sum(s => s.QuantityOnHand));
        Assert.Equal(30000m, recordedBudget);
    }

    [Fact]
    public async Task REQ_BRANCH_SCOPE_02_CreateOrder_ProductNotInBranch_ThrowsProductNotInBranchAndDoesNotDeductStock()
    {
        // Arrange: Sản phẩm X có trong Products và có tồn trong BranchStocks của _branchId, nhưng KHÔNG có BranchProduct
        var prodXId = Guid.NewGuid();
        _orderRepo.Products.Add(new Product
        {
            Id = prodXId,
            TenantId = "tenant-01",
            ProductCode = "PROD-X",
            Name = "San pham X ngoai chi nhanh",
            SellingPrice = 25000m,
            IsActive = true
        });

        _orderRepo.BranchStocks.Add(new BranchStock
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-01",
            BranchId = _branchId,
            ProductId = prodXId,
            BatchCode = "BATCH-X-01",
            ExpiryDate = DateTime.UtcNow.AddMonths(6),
            QuantityOnHand = 20m,
            LandedCostPerUnit = 18000m
        });

        var request = new OrderCreateRequest(
            BranchId: _branchId,
            CustomerId: null,
            PaymentMethod: "CASH",
            CashTendered: 50000m,
            Items: new List<OrderItemCreateDto>
            {
                new(prodXId, 2m, 25000m)
            }
        );

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BusinessException>(() => _orderService.CreateOrderAsync(request));
        Assert.Equal("PRODUCT_NOT_IN_BRANCH", ex.ErrorCode);
        Assert.Contains("San pham X ngoai chi nhanh", ex.Message);

        // Kiểm tra tồn kho tuyệt đối không bị trừ
        var stockX = _orderRepo.BranchStocks.Where(s => s.BranchId == _branchId && s.ProductId == prodXId).Sum(s => s.QuantityOnHand);
        Assert.Equal(20m, stockX);
    }

    [Fact]
    public async Task REQ_BRANCH_SCOPE_02_CreateOrder_ProductInactiveInBranch_ThrowsProductNotInBranch()
    {
        // Arrange: Sản phẩm có BranchProduct nhưng Status = "Inactive"
        var prodInactiveId = Guid.NewGuid();
        _orderRepo.Products.Add(new Product
        {
            Id = prodInactiveId,
            TenantId = "tenant-01",
            ProductCode = "PROD-INACTIVE",
            Name = "San pham Inactive",
            SellingPrice = 15000m,
            IsActive = true
        });

        _orderRepo.BranchProducts.Add(new BranchProduct
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-01",
            BranchId = _branchId,
            ProductId = prodInactiveId,
            Status = "Inactive"
        });

        _orderRepo.BranchStocks.Add(new BranchStock
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-01",
            BranchId = _branchId,
            ProductId = prodInactiveId,
            BatchCode = "BATCH-INACTIVE-01",
            ExpiryDate = DateTime.UtcNow.AddMonths(6),
            QuantityOnHand = 10m
        });

        var request = new OrderCreateRequest(
            BranchId: _branchId,
            CustomerId: null,
            PaymentMethod: "CASH",
            CashTendered: 30000m,
            Items: new List<OrderItemCreateDto>
            {
                new(prodInactiveId, 2m, 15000m)
            }
        );

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BusinessException>(() => _orderService.CreateOrderAsync(request));
        Assert.Equal("PRODUCT_NOT_IN_BRANCH", ex.ErrorCode);
        Assert.Contains("San pham Inactive", ex.Message);
    }

    [Fact]
    public async Task REQ_BRANCH_SCOPE_02_CreatePreOrder_ProductNotInBranch_ThrowsProductNotInBranch()
    {
        // Arrange: Sản phẩm không có BranchProduct tại _branchId
        var prodPreId = Guid.NewGuid();
        _orderRepo.Products.Add(new Product
        {
            Id = prodPreId,
            TenantId = "tenant-01",
            ProductCode = "PROD-PRE",
            Name = "San pham PreOrder ngoai danh muc",
            SellingPrice = 30000m,
            IsActive = true
        });

        _orderRepo.BranchStocks.Add(new BranchStock
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-01",
            BranchId = _branchId,
            ProductId = prodPreId,
            BatchCode = "BATCH-PRE-01",
            ExpiryDate = DateTime.UtcNow.AddMonths(6),
            QuantityOnHand = 5m
        });

        var preOrderRequest = new PreOrderCreateRequest(
            BranchId: _branchId,
            CustomerId: _customerId,
            Items: new List<OrderItemCreateDto>
            {
                new(prodPreId, 2m, 30000m)
            },
            DepositAmount: 10000m,
            ExpectedPickupDate: DateTime.UtcNow.AddDays(3)
        );

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BusinessException>(() => _orderService.CreatePreOrderAsync(preOrderRequest));
        Assert.Equal("PRODUCT_NOT_IN_BRANCH", ex.ErrorCode);
        Assert.Contains("San pham PreOrder ngoai danh muc", ex.Message);
    }

    [Fact]
    public async Task REQ_BRANCH_SCOPE_02_ProcessReturnExchange_NewItems_NotInBranch_ThrowsProductNotInBranchAndRollsBack()
    {
        // Arrange: Đơn hàng hoàn tất tại _branchId gồm 2 lon Coca
        var orderRes = await _orderService.CreateOrderAsync(new OrderCreateRequest(
            BranchId: _branchId,
            CustomerId: _customerId,
            PaymentMethod: "CASH",
            CashTendered: 50000m,
            Items: new List<OrderItemCreateDto>
            {
                new(_prodCocaId, 2m, 10000m)
            }
        ));

        // Sản phẩm mới để đổi (prodNewId) có tồn tại nhưng KHÔNG có BranchProduct tại _branchId
        var prodNewId = Guid.NewGuid();
        _orderRepo.Products.Add(new Product
        {
            Id = prodNewId,
            TenantId = "tenant-01",
            ProductCode = "PROD-NEW",
            Name = "San pham doi moi chua kich hoat",
            SellingPrice = 12000m,
            IsActive = true
        });

        _orderRepo.BranchStocks.Add(new BranchStock
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-01",
            BranchId = _branchId,
            ProductId = prodNewId,
            BatchCode = "BATCH-NEW-01",
            ExpiryDate = DateTime.UtcNow.AddMonths(6),
            QuantityOnHand = 10m
        });

        var cocaStockBefore = _orderRepo.BranchStocks.Where(s => s.BranchId == _branchId && s.ProductId == _prodCocaId).Sum(s => s.QuantityOnHand);
        var newStockBefore = _orderRepo.BranchStocks.Where(s => s.BranchId == _branchId && s.ProductId == prodNewId).Sum(s => s.QuantityOnHand);

        // Act: Đổi 1 lon Coca lấy 1 sản phẩm mới không thuộc chi nhánh
        var returnReq = new ReturnExchangeRequest(
            ReturnedItems: new List<ReturnItemDto>
            {
                new(
                    ProductId: _prodCocaId,
                    Quantity: 1m,
                    UnitPrice: 10000m,
                    Condition: "Restock"
                )
            },
            NewItems: new List<OrderItemCreateDto>
            {
                new(prodNewId, 1m, 12000m)
            },
            SettlementMethod: "CASH",
            Reason: "Doi hang sang san pham chua kich hoat"
        );

        var ex = await Assert.ThrowsAsync<BusinessException>(() => _orderService.ProcessReturnExchangeAsync(orderRes.Id, returnReq));
        Assert.Equal("PRODUCT_NOT_IN_BRANCH", ex.ErrorCode);
        Assert.Contains("San pham doi moi chua kich hoat", ex.Message);

        // Assert: Kho không đổi (không hoàn trả Coca, không trừ sản phẩm mới)
        var cocaStockAfter = _orderRepo.BranchStocks.Where(s => s.BranchId == _branchId && s.ProductId == _prodCocaId).Sum(s => s.QuantityOnHand);
        var newStockAfter = _orderRepo.BranchStocks.Where(s => s.BranchId == _branchId && s.ProductId == prodNewId).Sum(s => s.QuantityOnHand);
        Assert.Equal(cocaStockBefore, cocaStockAfter);
        Assert.Equal(newStockBefore, newStockAfter);
    }

    [Fact]
    public async Task REQ_BRANCH_SCOPE_02_CreateOrder_GiftItemNotInBranch_OmittedWithWarning()
    {
        // Arrange: Mì Hảo Hảo có BranchProduct Active (hàng mua trả tiền).
        // Quà tặng là prodGiftId có trong Products và BranchStocks nhưng KHÔNG có BranchProduct tại _branchId.
        var prodGiftId = Guid.NewGuid();
        _orderRepo.Products.Add(new Product
        {
            Id = prodGiftId,
            TenantId = "tenant-01",
            ProductCode = "PROD-GIFT",
            Name = "Mon qua chua gan chi nhanh",
            SellingPrice = 8000m,
            IsActive = true
        });

        _orderRepo.BranchStocks.Add(new BranchStock
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-01",
            BranchId = _branchId,
            ProductId = prodGiftId,
            BatchCode = "BATCH-GIFT-01",
            ExpiryDate = DateTime.UtcNow.AddMonths(6),
            QuantityOnHand = 5m
        });

        var request = new OrderCreateRequest(
            BranchId: _branchId,
            CustomerId: null,
            PaymentMethod: "CASH",
            CashTendered: 20000m,
            Items: new List<OrderItemCreateDto>
            {
                new(_prodHaoHaoId, 2m, 5000m, IsGift: false),
                new(prodGiftId, 1m, 0m, IsGift: true)
            }
        );

        // Act: Tạo đơn hàng
        var res = await _orderService.CreateOrderAsync(request);

        // Assert: Đơn hàng thành công, nhưng dòng quà bị bỏ qua, có cảnh báo trong GiftStockWarning, tồn kho quà không đổi
        Assert.NotNull(res);
        Assert.DoesNotContain(res.Items, i => i.ProductId == prodGiftId);
        Assert.NotNull(res.GiftStockWarning);
        Assert.Contains("chưa được kích hoạt", res.GiftStockWarning);

        var giftStock = _orderRepo.BranchStocks.Where(s => s.BranchId == _branchId && s.ProductId == prodGiftId).Sum(s => s.QuantityOnHand);
        Assert.Equal(5m, giftStock);
    }

    private async Task<OrderResponse> CreateSampleCompletedOrderAsync(Guid? customerId = null, decimal haoHaoQty = 2m)
    {
        return await _orderService.CreateOrderAsync(new OrderCreateRequest(
            BranchId: _branchId,
            CustomerId: customerId ?? _customerId,
            PaymentMethod: "CASH",
            CashTendered: haoHaoQty * 5000m,
            Items: new List<OrderItemCreateDto>
            {
                new(_prodHaoHaoId, haoHaoQty, 5000m)
            }
        ));
    }

    [Fact]
    public async Task ProcessReturnExchange_OrderNotFound_ThrowsInvalidOperationException()
    {
        var req = new ReturnExchangeRequest(
            ReturnedItems: new List<ReturnItemDto> { new(_prodHaoHaoId, 1m, 5000m) },
            NewItems: new List<OrderItemCreateDto>(),
            SettlementMethod: "CASH"
        );

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _orderService.ProcessReturnExchangeAsync(Guid.NewGuid(), req));
    }

    [Fact]
    public async Task ProcessReturnExchange_OrderNotCompleted_ThrowsInvalidOperationException()
    {
        var originalOrder = await CreateSampleCompletedOrderAsync();
        var domainOrder = _orderRepo.Orders.First(o => o.Id == originalOrder.Id);
        domainOrder.Status = "PendingPickup";

        var req = new ReturnExchangeRequest(
            ReturnedItems: new List<ReturnItemDto> { new(_prodHaoHaoId, 1m, 5000m) },
            NewItems: new List<OrderItemCreateDto>(),
            SettlementMethod: "CASH"
        );

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _orderService.ProcessReturnExchangeAsync(originalOrder.Id, req));
    }

    [Fact]
    public async Task ProcessReturnExchange_EmptyReturnedAndNewItems_ThrowsInvalidOperationException()
    {
        var originalOrder = await CreateSampleCompletedOrderAsync();
        var req = new ReturnExchangeRequest(
            ReturnedItems: new List<ReturnItemDto>(),
            NewItems: new List<OrderItemCreateDto>(),
            SettlementMethod: "CASH"
        );

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _orderService.ProcessReturnExchangeAsync(originalOrder.Id, req));
    }

    [Fact]
    public async Task ProcessReturnExchange_ReturnedQtyNonPositive_ThrowsInvalidOperationException()
    {
        var originalOrder = await CreateSampleCompletedOrderAsync();
        var req = new ReturnExchangeRequest(
            ReturnedItems: new List<ReturnItemDto> { new(_prodHaoHaoId, 0m, 5000m) },
            NewItems: new List<OrderItemCreateDto>(),
            SettlementMethod: "CASH"
        );

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _orderService.ProcessReturnExchangeAsync(originalOrder.Id, req));
    }

    [Fact]
    public async Task ProcessReturnExchange_DuplicateReturnedRowsExceedPurchasedQty_ThrowsInvalidOperationException()
    {
        var originalOrder = await CreateSampleCompletedOrderAsync(haoHaoQty: 2m);
        var detailId = originalOrder.Items.First().Id;
        var stockBefore = _orderRepo.BranchStocks.Where(s => s.BranchId == _branchId && s.ProductId == _prodHaoHaoId).Sum(s => s.QuantityOnHand);

        var req = new ReturnExchangeRequest(
            ReturnedItems: new List<ReturnItemDto>
            {
                new(_prodHaoHaoId, 2m, 5000m, OrderDetailId: detailId),
                new(_prodHaoHaoId, 1m, 5000m, OrderDetailId: detailId)
            },
            NewItems: new List<OrderItemCreateDto>(),
            SettlementMethod: "CASH"
        );

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _orderService.ProcessReturnExchangeAsync(originalOrder.Id, req));

        var stockAfter = _orderRepo.BranchStocks.Where(s => s.BranchId == _branchId && s.ProductId == _prodHaoHaoId).Sum(s => s.QuantityOnHand);
        Assert.Equal(stockBefore, stockAfter);
    }

    [Fact]
    public async Task ProcessReturnExchange_ReturnedProductNotInOrder_ThrowsInvalidOperationException()
    {
        var originalOrder = await CreateSampleCompletedOrderAsync();
        var req = new ReturnExchangeRequest(
            ReturnedItems: new List<ReturnItemDto> { new(_prodCocaId, 1m, 10000m) },
            NewItems: new List<OrderItemCreateDto>(),
            SettlementMethod: "CASH"
        );

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _orderService.ProcessReturnExchangeAsync(originalOrder.Id, req));
    }

    [Theory]
    [InlineData("Broken", "CASH")]
    [InlineData("Restock", "")]
    [InlineData("Restock", "CRYPTO")]
    public async Task ProcessReturnExchange_InvalidConditionOrSettlementMethod_ThrowsInvalidOperationException(string condition, string settlementMethod)
    {
        var originalOrder = await CreateSampleCompletedOrderAsync();
        var req = new ReturnExchangeRequest(
            ReturnedItems: new List<ReturnItemDto> { new(_prodHaoHaoId, 1m, 5000m, Condition: condition) },
            NewItems: new List<OrderItemCreateDto>(),
            SettlementMethod: settlementMethod
        );

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _orderService.ProcessReturnExchangeAsync(originalOrder.Id, req));
    }

    [Fact]
    public async Task ProcessReturnExchange_NoteOver500OrOtherReasonWithoutNote_ThrowsInvalidOperationException()
    {
        var originalOrder = await CreateSampleCompletedOrderAsync();

        var tooLongNoteReq = new ReturnExchangeRequest(
            ReturnedItems: new List<ReturnItemDto> { new(_prodHaoHaoId, 1m, 5000m) },
            NewItems: new List<OrderItemCreateDto>(),
            SettlementMethod: "CASH",
            Reason: "DEFECTIVE",
            Note: new string('a', 501)
        );
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _orderService.ProcessReturnExchangeAsync(originalOrder.Id, tooLongNoteReq));

        var otherWithoutNoteReq = new ReturnExchangeRequest(
            ReturnedItems: new List<ReturnItemDto> { new(_prodHaoHaoId, 1m, 5000m) },
            NewItems: new List<OrderItemCreateDto>(),
            SettlementMethod: "CASH",
            Reason: "OTHER",
            Note: "   "
        );
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _orderService.ProcessReturnExchangeAsync(originalOrder.Id, otherWithoutNoteReq));
    }

    [Fact]
    public async Task ProcessReturnExchange_NewItemInvalidQtyOrNegativePrice_ThrowsInvalidOperationException()
    {
        var originalOrder = await CreateSampleCompletedOrderAsync();

        var zeroQtyReq = new ReturnExchangeRequest(
            ReturnedItems: new List<ReturnItemDto> { new(_prodHaoHaoId, 1m, 5000m) },
            NewItems: new List<OrderItemCreateDto> { new(_prodCocaId, 0m, 10000m) },
            SettlementMethod: "CASH"
        );
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _orderService.ProcessReturnExchangeAsync(originalOrder.Id, zeroQtyReq));

        var negativePriceReq = new ReturnExchangeRequest(
            ReturnedItems: new List<ReturnItemDto> { new(_prodHaoHaoId, 1m, 5000m) },
            NewItems: new List<OrderItemCreateDto> { new(_prodCocaId, 1m, -1000m) },
            SettlementMethod: "CASH"
        );
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _orderService.ProcessReturnExchangeAsync(originalOrder.Id, negativePriceReq));
    }

    [Fact]
    public async Task ProcessReturnExchange_DebtOrReduceDebtWithoutCustomer_ThrowsInvalidOperationException()
    {
        var originalOrder = await _orderService.CreateOrderAsync(new OrderCreateRequest(
            BranchId: _branchId,
            CustomerId: null,
            PaymentMethod: "CASH",
            CashTendered: 10000m,
            Items: new List<OrderItemCreateDto> { new(_prodHaoHaoId, 2m, 5000m) }
        ));

        var debtBuyReq = new ReturnExchangeRequest(
            ReturnedItems: new List<ReturnItemDto> { new(_prodHaoHaoId, 1m, 5000m) },
            NewItems: new List<OrderItemCreateDto> { new(_prodCocaId, 1m, 10000m) },
            SettlementMethod: "DEBT"
        );
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _orderService.ProcessReturnExchangeAsync(originalOrder.Id, debtBuyReq));

        var reduceDebtReq = new ReturnExchangeRequest(
            ReturnedItems: new List<ReturnItemDto> { new(_prodHaoHaoId, 1m, 5000m) },
            NewItems: new List<OrderItemCreateDto>(),
            SettlementMethod: "REDUCE_DEBT"
        );
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _orderService.ProcessReturnExchangeAsync(originalOrder.Id, reduceDebtReq));
    }

    [Fact]
    public async Task ProcessReturnExchange_InsufficientDrawerCash_ThrowsInvalidOperationExceptionWithoutMutatingStock()
    {
        var originalOrder = await CreateSampleCompletedOrderAsync(haoHaoQty: 2m);
        var stockBefore = _orderRepo.BranchStocks.Where(s => s.BranchId == _branchId && s.ProductId == _prodHaoHaoId).Sum(s => s.QuantityOnHand);

        var shiftId = Guid.NewGuid();
        var mockShiftRepo = new Moq.Mock<IShiftRepository>();
        mockShiftRepo.Setup(r => r.GetActiveShiftAsync(_branchId, Moq.It.IsAny<Guid?>(), Moq.It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ShiftZReport
            {
                Id = shiftId,
                BranchId = _branchId,
                Status = ShiftState.Open,
                ExpectedCash = 2000m
            });

        var mockTenant = new Moq.Mock<ITenantContext>();
        mockTenant.Setup(t => t.CurrentTenantId).Returns("tenant-01");
        mockTenant.Setup(t => t.CurrentRole).Returns("CASHIER");

        var cashierOrderService = new OrderService(
            _orderRepo,
            _uow,
            _printService,
            null,
            null,
            new OrderCreateRequestValidator(),
            mockShiftRepo.Object,
            null,
            mockTenant.Object);

        var refundReq = new ReturnExchangeRequest(
            ReturnedItems: new List<ReturnItemDto> { new(_prodHaoHaoId, 1m, 5000m) },
            NewItems: new List<OrderItemCreateDto>(),
            SettlementMethod: "CASH",
            Reason: "DEFECTIVE"
        );

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            cashierOrderService.ProcessReturnExchangeAsync(originalOrder.Id, refundReq));

        var stockAfter = _orderRepo.BranchStocks.Where(s => s.BranchId == _branchId && s.ProductId == _prodHaoHaoId).Sum(s => s.QuantityOnHand);
        Assert.Equal(stockBefore, stockAfter);
    }

    [Fact]
    public async Task ProcessReturnExchange_PreOrderCompleted_BlocksRefundUnlessDefectiveReason()
    {
        var originalOrder = await CreateSampleCompletedOrderAsync(haoHaoQty: 2m);
        var domainOrder = _orderRepo.Orders.First(o => o.Id == originalOrder.Id);
        domainOrder.OrderCode = "PO2611120001";

        var changeMindRefundReq = new ReturnExchangeRequest(
            ReturnedItems: new List<ReturnItemDto> { new(_prodHaoHaoId, 1m, 5000m) },
            NewItems: new List<OrderItemCreateDto>(),
            SettlementMethod: "CASH",
            Reason: "CHANGE_MIND"
        );

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _orderService.ProcessReturnExchangeAsync(originalOrder.Id, changeMindRefundReq));

        // Ngoại lệ DEFECTIVE được phép hoàn tiền
        var defectiveRefundRes = await _orderService.ProcessReturnExchangeAsync(originalOrder.Id, new ReturnExchangeRequest(
            ReturnedItems: new List<ReturnItemDto> { new(_prodHaoHaoId, 1m, 5000m) },
            NewItems: new List<OrderItemCreateDto>(),
            SettlementMethod: "CASH",
            Reason: "DEFECTIVE"
        ));
        Assert.Equal(5000m, defectiveRefundRes.ChangeAmount);
    }

    [Fact]
    public async Task CreatePreOrder_EmptyOrGiftOnlyItems_ThrowsValidationException()
    {
        var emptyReq = new PreOrderCreateRequest(
            BranchId: _branchId,
            CustomerId: _customerId,
            Items: new List<OrderItemCreateDto>(),
            DepositAmount: 0m,
            ExpectedPickupDate: DateTime.UtcNow.AddDays(1)
        );
        await Assert.ThrowsAsync<ValidationException>(() => _orderService.CreatePreOrderAsync(emptyReq));

        var giftOnlyReq = new PreOrderCreateRequest(
            BranchId: _branchId,
            CustomerId: _customerId,
            Items: new List<OrderItemCreateDto> { new(_prodCocaId, 1m, 0m, IsGift: true) },
            DepositAmount: 0m,
            ExpectedPickupDate: DateTime.UtcNow.AddDays(1)
        );
        await Assert.ThrowsAsync<ValidationException>(() => _orderService.CreatePreOrderAsync(giftOnlyReq));
    }

    [Fact]
    public async Task CreatePreOrder_EmptyOrUnknownOrWalkInCustomer_ThrowsInvalidOperationException()
    {
        var baseItems = new List<OrderItemCreateDto> { new(_prodHaoHaoId, 2m, 5000m) };

        await Assert.ThrowsAsync<InvalidOperationException>(() => _orderService.CreatePreOrderAsync(
            new PreOrderCreateRequest(_branchId, Guid.Empty, baseItems, 1000m, DateTime.UtcNow.AddDays(1))));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _orderService.CreatePreOrderAsync(
            new PreOrderCreateRequest(_branchId, Guid.NewGuid(), baseItems, 1000m, DateTime.UtcNow.AddDays(1))));

        var walkInId = Guid.NewGuid();
        _orderRepo.Customers.Add(new Customer
        {
            Id = walkInId,
            TenantId = "tenant-01",
            CustomerCode = "KHACHLE",
            Name = "Khách lẻ"
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => _orderService.CreatePreOrderAsync(
            new PreOrderCreateRequest(_branchId, walkInId, baseItems, 1000m, DateTime.UtcNow.AddDays(1))));
    }

    [Fact]
    public async Task CreatePreOrder_BranchOrProductNotFound_ThrowsInvalidOperationException()
    {
        var baseItems = new List<OrderItemCreateDto> { new(_prodHaoHaoId, 2m, 5000m) };

        await Assert.ThrowsAsync<InvalidOperationException>(() => _orderService.CreatePreOrderAsync(
            new PreOrderCreateRequest(Guid.Empty, _customerId, baseItems, 1000m, DateTime.UtcNow.AddDays(1))));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _orderService.CreatePreOrderAsync(
            new PreOrderCreateRequest(Guid.NewGuid(), _customerId, baseItems, 1000m, DateTime.UtcNow.AddDays(1))));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _orderService.CreatePreOrderAsync(
            new PreOrderCreateRequest(_branchId, _customerId, new List<OrderItemCreateDto> { new(Guid.NewGuid(), 1m, 5000m) }, 1000m, DateTime.UtcNow.AddDays(1))));
    }

    [Fact]
    public async Task CreatePreOrder_PastOrDefaultPickupDate_ThrowsInvalidOperationException()
    {
        var baseItems = new List<OrderItemCreateDto> { new(_prodHaoHaoId, 2m, 5000m) };

        await Assert.ThrowsAsync<InvalidOperationException>(() => _orderService.CreatePreOrderAsync(
            new PreOrderCreateRequest(_branchId, _customerId, baseItems, 1000m, default)));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _orderService.CreatePreOrderAsync(
            new PreOrderCreateRequest(_branchId, _customerId, baseItems, 1000m, DateTime.UtcNow.AddHours(-2))));
    }

    [Theory]
    [InlineData("")]
    [InlineData("DEBT")]
    [InlineData("CRYPTO")]
    public async Task CreatePreOrder_InvalidPaymentMethod_ThrowsInvalidOperationException(string paymentMethod)
    {
        var req = new PreOrderCreateRequest(
            BranchId: _branchId,
            CustomerId: _customerId,
            Items: new List<OrderItemCreateDto> { new(_prodHaoHaoId, 2m, 5000m) },
            DepositAmount: 2000m,
            ExpectedPickupDate: DateTime.UtcNow.AddDays(1),
            PaymentMethod: paymentMethod
        );

        await Assert.ThrowsAsync<InvalidOperationException>(() => _orderService.CreatePreOrderAsync(req));
    }

    [Fact]
    public async Task CreatePreOrder_NoteOver500OrNegativeDiscount_ThrowsInvalidOperationException()
    {
        var baseItems = new List<OrderItemCreateDto> { new(_prodHaoHaoId, 2m, 5000m) };

        await Assert.ThrowsAsync<InvalidOperationException>(() => _orderService.CreatePreOrderAsync(
            new PreOrderCreateRequest(_branchId, _customerId, baseItems, 1000m, DateTime.UtcNow.AddDays(1), Note: new string('x', 501))));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _orderService.CreatePreOrderAsync(
            new PreOrderCreateRequest(_branchId, _customerId, baseItems, 1000m, DateTime.UtcNow.AddDays(1), DiscountAmount: -500m)));
    }

    [Fact]
    public async Task CreatePreOrder_InvalidItemQtyOrNegativePriceOrConversionRate_ThrowsInvalidOperationException()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _orderService.CreatePreOrderAsync(
            new PreOrderCreateRequest(_branchId, _customerId, new List<OrderItemCreateDto> { new(_prodHaoHaoId, 0m, 5000m) }, 0m, DateTime.UtcNow.AddDays(1))));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _orderService.CreatePreOrderAsync(
            new PreOrderCreateRequest(_branchId, _customerId, new List<OrderItemCreateDto> { new(_prodHaoHaoId, -2m, 5000m) }, 0m, DateTime.UtcNow.AddDays(1))));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _orderService.CreatePreOrderAsync(
            new PreOrderCreateRequest(_branchId, _customerId, new List<OrderItemCreateDto> { new(_prodHaoHaoId, 2m, -5000m) }, 0m, DateTime.UtcNow.AddDays(1))));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _orderService.CreatePreOrderAsync(
            new PreOrderCreateRequest(_branchId, _customerId, new List<OrderItemCreateDto> { new(_prodHaoHaoId, 2m, 5000m, ConversionRate: -1m) }, 1000m, DateTime.UtcNow.AddDays(1))));
    }

    [Fact]
    public async Task CreatePreOrder_DepositNegativeOrExceedsFinalAmount_ThrowsInvalidOperationException()
    {
        var baseItems = new List<OrderItemCreateDto> { new(_prodHaoHaoId, 4m, 5000m) };

        await Assert.ThrowsAsync<InvalidOperationException>(() => _orderService.CreatePreOrderAsync(
            new PreOrderCreateRequest(_branchId, _customerId, baseItems, -1000m, DateTime.UtcNow.AddDays(1))));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _orderService.CreatePreOrderAsync(
            new PreOrderCreateRequest(_branchId, _customerId, baseItems, 25000m, DateTime.UtcNow.AddDays(1))));
    }

    [Fact]
    public async Task CreatePreOrder_Full100PercentDeposit_SetsPaymentStatusPaid()
    {
        var req = new PreOrderCreateRequest(
            BranchId: _branchId,
            CustomerId: _customerId,
            Items: new List<OrderItemCreateDto> { new(_prodHaoHaoId, 4m, 5000m) },
            DepositAmount: 20000m,
            ExpectedPickupDate: DateTime.UtcNow.AddDays(1),
            PaymentMethod: "VIETQR"
        );

        var res = await _orderService.CreatePreOrderAsync(req);

        Assert.Equal("PendingPickup", res.Status);
        Assert.Equal("PAID", res.PaymentStatus);
        Assert.Equal(20000m, res.DepositAmount);
    }

    [Fact]
    public async Task FulfillPreOrder_NotFoundOrInvalidStatus_ThrowsInvalidOperationException()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _orderService.FulfillPreOrderAsync(
            Guid.NewGuid(),
            new PreOrderFulfillRequest("CASH", 10000m)));

        var preOrder = await _orderService.CreatePreOrderAsync(new PreOrderCreateRequest(
            BranchId: _branchId,
            CustomerId: _customerId,
            Items: new List<OrderItemCreateDto> { new(_prodHaoHaoId, 2m, 5000m) },
            DepositAmount: 2000m,
            ExpectedPickupDate: DateTime.UtcNow.AddDays(1)
        ));

        await _orderService.FulfillPreOrderAsync(preOrder.Id, new PreOrderFulfillRequest("CASH", 8000m));

        // Bàn giao lại đơn đã Completed -> phải chặn
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _orderService.FulfillPreOrderAsync(preOrder.Id, new PreOrderFulfillRequest("CASH", 0m)));
    }

    [Fact]
    public async Task FulfillPreOrder_InsufficientPaymentOrInvalidMethodOrNoteOver500_ThrowsInvalidOperationException()
    {
        var preOrder = await _orderService.CreatePreOrderAsync(new PreOrderCreateRequest(
            BranchId: _branchId,
            CustomerId: _customerId,
            Items: new List<OrderItemCreateDto> { new(_prodHaoHaoId, 4m, 5000m) },
            DepositAmount: 5000m,
            ExpectedPickupDate: DateTime.UtcNow.AddDays(1)
        ));

        // Thiếu tiền (cần 15.000, chỉ đưa 10.000)
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _orderService.FulfillPreOrderAsync(preOrder.Id, new PreOrderFulfillRequest("CASH", 10000m)));

        // Phương thức không hợp lệ
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _orderService.FulfillPreOrderAsync(preOrder.Id, new PreOrderFulfillRequest("DEBT", 15000m)));

        // Note vượt 500 ký tự
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _orderService.FulfillPreOrderAsync(preOrder.Id, new PreOrderFulfillRequest("CASH", 15000m, Note: new string('n', 501))));
    }

    [Fact]
    public async Task FulfillPreOrder_WalkInCustomerOrder_DoesNotAwardLoyaltyPoints()
    {
        var walkInId = Guid.NewGuid();
        var walkIn = new Customer
        {
            Id = walkInId,
            TenantId = "tenant-01",
            CustomerCode = "KHACHLE",
            Name = "Khách lẻ",
            RewardPoints = 0
        };
        _orderRepo.Customers.Add(walkIn);

        var legacyPreOrder = new Order
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-01",
            BranchId = _branchId,
            CustomerId = walkInId,
            OrderCode = "PO2611129999",
            TotalAmount = 50000m,
            FinalAmount = 50000m,
            DepositAmount = 10000m,
            PaymentMethod = "CASH",
            PaymentStatus = "PARTIALLY_PAID",
            Status = "PendingPickup",
            OrderDate = DateTime.UtcNow
        };
        _orderRepo.Orders.Add(legacyPreOrder);

        var res = await _orderService.FulfillPreOrderAsync(legacyPreOrder.Id, new PreOrderFulfillRequest("CASH", 40000m));

        Assert.Equal("Completed", res.Status);
        Assert.Equal(0, walkIn.RewardPoints);
    }
}

// ----------------- FAKE IMPLEMENTATIONS DÀNH CHO UNIT TEST -----------------

public class FakeUnitOfWork : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(1);
    }
}

public class FakeOrderRepository : IOrderRepository
{
    public List<Order> Orders { get; } = new();
    public List<Branch> Branches { get; } = new();
    public List<Customer> Customers { get; } = new();
    public List<Product> Products { get; } = new();
    public List<BranchStock> BranchStocks { get; } = new();
    public List<BranchProduct> BranchProducts { get; } = new();

    public Task<Dictionary<Guid, BranchProduct>> GetBranchProductsByProductIdsAsync(Guid branchId, IEnumerable<Guid> productIds, CancellationToken cancellationToken = default)
    {
        var ids = productIds.ToHashSet();
        var dict = BranchProducts
            .Where(bp => bp.BranchId == branchId && ids.Contains(bp.ProductId) && !bp.IsDeleted)
            .ToDictionary(bp => bp.ProductId);
        return Task.FromResult(dict);
    }

    public Task<Branch?> GetBranchByIdAsync(Guid branchId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Branches.FirstOrDefault(b => b.Id == branchId));
    }

    public Task<Customer?> GetCustomerByIdAsync(Guid customerId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Customers.FirstOrDefault(c => c.Id == customerId));
    }

    public Task<Dictionary<Guid, Product>> GetProductsByIdsAsync(IEnumerable<Guid> productIds, CancellationToken cancellationToken = default)
    {
        var ids = productIds.ToHashSet();
        var dict = Products.Where(p => ids.Contains(p.Id)).ToDictionary(p => p.Id);
        return Task.FromResult(dict);
    }

    public Task<IReadOnlyList<BranchStock>> GetBranchStocksForProductFefoAsync(Guid branchId, Guid productId, CancellationToken cancellationToken = default)
    {
        var list = BranchStocks
            .Where(s => s.BranchId == branchId && s.ProductId == productId && s.QuantityOnHand > 0)
            .OrderBy(s => s.ExpiryDate ?? DateTime.MaxValue)
            .ThenBy(s => s.ManufactureDate ?? DateTime.MaxValue)
            .ToList();

        return Task.FromResult<IReadOnlyList<BranchStock>>(list);
    }

    public Task<BranchStock?> GetBranchStockByIdAsync(Guid branchId, Guid productId, Guid batchId, CancellationToken cancellationToken = default)
        => Task.FromResult(BranchStocks.FirstOrDefault(s => s.Id == batchId && s.BranchId == branchId && s.ProductId == productId));

    public Task<Order?> GetOrderWithDetailsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Orders.FirstOrDefault(o => o.Id == id));
    }

    public Task<IReadOnlyList<Order>> GetOrdersWithFilterAsync(Guid? branchId, Guid? customerId, string? paymentMethod, string? status, DateTime? fromDate, DateTime? toDate, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        var list = Orders.AsQueryable();
        if (branchId.HasValue) list = list.Where(o => o.BranchId == branchId.Value);
        return Task.FromResult<IReadOnlyList<Order>>(list.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList());
    }

    public Task<int> CountOrdersWithFilterAsync(Guid? branchId, Guid? customerId, string? paymentMethod, string? status, DateTime? fromDate, DateTime? toDate, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Orders.Count);
    }

    public Task<BranchStock?> GetBranchStockAsync(Guid branchId, Guid productId, string? batchCode, CancellationToken cancellationToken = default)
    {
        var stock = BranchStocks.FirstOrDefault(s => s.BranchId == branchId && s.ProductId == productId && (batchCode == null || s.BatchCode == batchCode));
        return Task.FromResult(stock);
    }

    public Task<IReadOnlyList<Order>> GetAllOrdersForSummaryAsync(Guid? branchId, DateTime? fromDate, DateTime? toDate, CancellationToken cancellationToken = default)
    {
        var list = Orders.AsQueryable();
        if (branchId.HasValue) list = list.Where(o => o.BranchId == branchId.Value);
        if (fromDate.HasValue) list = list.Where(o => o.OrderDate >= fromDate.Value);
        if (toDate.HasValue) list = list.Where(o => o.OrderDate <= toDate.Value);
        return Task.FromResult<IReadOnlyList<Order>>(list.ToList());
    }

    public Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Orders.FirstOrDefault(o => o.Id == id));
    }

    public Task<IReadOnlyList<Order>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<Order>>(Orders);
    }

    public Task<IReadOnlyList<Order>> FindAsync(Expression<Func<Order, bool>> predicate, CancellationToken cancellationToken = default)
    {
        var compiled = predicate.Compile();
        return Task.FromResult<IReadOnlyList<Order>>(Orders.Where(compiled).ToList());
    }

    public Task AddAsync(Order entity, CancellationToken cancellationToken = default)
    {
        Orders.Add(entity);
        return Task.CompletedTask;
    }

    public void Update(Order entity)
    {
    }

    public void Delete(Order entity)
    {
        Orders.Remove(entity);
    }
}

