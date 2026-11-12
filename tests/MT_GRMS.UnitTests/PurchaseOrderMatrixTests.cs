using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.PurchaseOrders;
using MT_GRMS.Application.PurchaseOrders.Dispatch;
using MT_GRMS.Application.PurchaseOrders.Dtos;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Entities.Supplier;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Domain.Exceptions;
using Xunit;

namespace MT_GRMS.UnitTests;

public class PurchaseOrderMatrixTests
{
    private readonly FakePurchaseOrderRepository _repo;
    private readonly FakeUnitOfWork _uow;
    private readonly FakeTenantContext _tenantContext;
    private readonly PurchaseOrderService _service;

    private readonly Guid _branchId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private readonly Guid _supplier1Id = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private readonly Guid _supplier2Id = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private readonly Guid _prod1Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private readonly Guid _prod2Id = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    // Ngày giao tương lai để vượt LeadTime/Cutoff khi chạy test bất kỳ ngày nào
    private readonly DateTime _viewFrom = DateTime.Today.AddDays(1);
    private readonly DateTime _deliveryA = DateTime.Today.AddDays(2);
    private readonly DateTime _deliveryB = DateTime.Today.AddDays(4);

    public PurchaseOrderMatrixTests()
    {
        _repo = new FakePurchaseOrderRepository();
        _uow = new FakeUnitOfWork();
        _tenantContext = new FakeTenantContext("tenant-test-1");
        _service = new PurchaseOrderService(_repo, _uow, _tenantContext, new FakeSupplierDispatchPackageService());

        // Seed Branch
        _repo.Branches.Add(new Branch
        {
            Id = _branchId,
            TenantId = "tenant-test-1",
            Code = "BR-01",
            Name = "Chi nhánh Nguyễn Trãi",
            IsActive = true
        });

        // Seed Suppliers
        _repo.Suppliers.AddRange(new[]
        {
            new Supplier { Id = _supplier1Id, TenantId = "tenant-test-1", Code = "NCC-01", Name = "Nông Sản Tươi Sạch Khải Anh" },
            new Supplier { Id = _supplier2Id, TenantId = "tenant-test-1", Code = "NCC-02", Name = "Bánh Kẹo SnackVN" }
        });

        // Seed Products
        _repo.Products.AddRange(new[]
        {
            new Product { Id = _prod1Id, TenantId = "tenant-test-1", ProductCode = "SP01", Name = "Gạo ST25 5kg", BaseUnit = "Túi", CostPrice = 160000m, SellingPrice = 195000m, MinStock = 10m },
            new Product { Id = _prod2Id, TenantId = "tenant-test-1", ProductCode = "SP02", Name = "Bánh Snack Khoai Tây 50g", BaseUnit = "Gói", CostPrice = 12000m, SellingPrice = 16000m, MinStock = 30m }
        });

        // Seed Matrix products
        _repo.MatrixProducts.AddRange(new[]
        {
            new OrderMatrixProductDto { Id = _prod1Id, SupplierId = _supplier1Id, SupplierName = "Nông Sản Tươi Sạch Khải Anh", Name = "Gạo ST25 5kg", Sku = "SP01", Price = 160000m, Moq = 5, Soh = 20m, Unit = "Túi" },
            new OrderMatrixProductDto { Id = _prod2Id, SupplierId = _supplier2Id, SupplierName = "Bánh Kẹo SnackVN", Name = "Bánh Snack Khoai Tây 50g", Sku = "SP02", Price = 12000m, Moq = 20, Soh = 50m, Unit = "Gói" }
        });
    }

    [Fact]
    public void OrderMatrixRules_MinimumLeadTime_BlocksToday_AllowsTomorrow()
    {
        var supplierId = _supplier1Id;
        var config = new OrderMatrixSupplierDeliveryConfigDto
        {
            SupplierId = supplierId,
            IsActive = true,
            IsAssignedToBranch = true,
            CutoffTime = "23:59",
            CutoffDayOffset = 1,
            ActiveDeliveryDays = new List<int> { 0, 1, 2, 3, 4, 5, 6 },
            BlackoutDates = new List<string>()
        };
        var now = new DateTime(2026, 11, 8, 10, 30, 0);

        var todayStatus = OrderMatrixRules.EvaluateDayStatus(supplierId, now.Date, config, now);
        Assert.Equal(OrderMatrixRules.StatusSupplierOff, todayStatus.DeliveryDateStatus);
        Assert.Equal("LEAD_TIME_VIOLATION", todayStatus.PrimaryReasonCode);
        Assert.False(OrderMatrixRules.IsEditableStatus(todayStatus.DeliveryDateStatus));

        var tomorrowStatus = OrderMatrixRules.EvaluateDayStatus(supplierId, now.Date.AddDays(1), config, now);
        Assert.True(OrderMatrixRules.IsEditableStatus(tomorrowStatus.DeliveryDateStatus));
        // offset=1 → cutoff ngày giao mai = hôm nay 23:59
        Assert.Equal(new DateTime(2026, 11, 8, 23, 59, 0), tomorrowStatus.CutoffDateTime);
    }

    [Fact]
    public void OrderMatrixRules_GetCutoffDateTime_UsesDayOffsetBeforeDelivery()
    {
        var delivery = new DateTime(2026, 11, 9);
        var cutoffOffset1 = OrderMatrixRules.GetCutoffDateTime(delivery, "16:00", cutoffDayOffset: 1);
        Assert.Equal(new DateTime(2026, 11, 8, 16, 0, 0), cutoffOffset1);

        var cutoffOffset0 = OrderMatrixRules.GetCutoffDateTime(delivery, "16:00", cutoffDayOffset: 0);
        Assert.Equal(new DateTime(2026, 11, 9, 16, 0, 0), cutoffOffset0);

        var cutoffOffset2 = OrderMatrixRules.GetCutoffDateTime(delivery, "16:00", cutoffDayOffset: 2);
        Assert.Equal(new DateTime(2026, 11, 7, 16, 0, 0), cutoffOffset2);
    }

    [Fact]
    public async Task REQ_PO_01_GetOrderMatrix_ReturnsProductsAndBranchDetails()
    {
        // Act
        var res = await _service.GetOrderMatrixAsync(new OrderMatrixQuery
        {
            BranchId = _branchId,
            ViewFrom = _viewFrom,
            DurationDays = 7
        });

        // Assert
        Assert.NotNull(res);
        Assert.Equal(_branchId, res.BranchId);
        Assert.Equal("Chi nhánh Nguyễn Trãi", res.BranchName);
        Assert.True(res.BranchIsActive);
        Assert.Equal(2, res.Products.Count);
        Assert.NotNull(res.SupplierConfigs);
        Assert.Equal(2, res.SupplierConfigs.Count);
        Assert.NotEmpty(res.DayStatuses);
    }

    [Fact]
    public async Task REQ_PO_05_OrderMatrix_ReturnsSupplierConfigsAndActiveStatusRules()
    {
        // Arrange
        _repo.MatrixProducts.Clear();
        _repo.MatrixProducts.Add(new OrderMatrixProductDto
        {
            Id = _prod1Id,
            SupplierId = _supplier1Id,
            SupplierName = "Nông Sản Tươi Sạch Khải Anh",
            Name = "Sản phẩm tạm ngưng",
            Sku = "SP-INACTIVE",
            IsProductActive = false,
            ProductInactiveReason = "Sản phẩm đang tạm ngưng kinh doanh",
            IsSupplierActive = true,
            IsSupplierProductActive = true,
            IsSupplierBranchActive = true,
            IsBranchProductActive = true,
            RowDisabledReason = "Sản phẩm đang tạm ngưng kinh doanh"
        });

        // Act
        var res = await _service.GetOrderMatrixAsync(new OrderMatrixQuery
        {
            BranchId = _branchId,
            ViewFrom = _viewFrom,
            DurationDays = 7
        });

        // Assert
        Assert.NotNull(res);
        var product = Assert.Single(res.Products);
        Assert.False(product.IsProductActive);
        Assert.True(product.IsRowDisabled);
        Assert.Equal("Sản phẩm đang tạm ngưng kinh doanh", product.RowDisabledReason);

        // Kiểm tra cấu hình giao hàng NCC
        var supConfig = res.SupplierConfigs.First(c => c.SupplierId == _supplier1Id);
        Assert.True(supConfig.IsActive);
        Assert.Contains(1, supConfig.ActiveDeliveryDays); // Thứ 2
        Assert.Contains(2, supConfig.ActiveDeliveryDays); // Thứ 3
        Assert.NotEmpty(res.DayStatuses);
    }

    [Fact]
    public async Task REQ_PO_02_SaveMatrixDraft_SavesDraftPoAndDetails()
    {
        // Act
        var command = new SaveMatrixDraftCommand
        {
            BranchId = _branchId,
            SupplierId = _supplier1Id,
            ViewFrom = _viewFrom,
            ViewTo = _viewFrom.AddDays(7),
            Note = "Kế hoạch tuần đặt hàng",
            Cells = new List<OrderMatrixCellDto>
            {
                new OrderMatrixCellDto
                {
                    ProductId = _prod1Id,
                    DeliveryDate = _deliveryA,
                    EmployeeQty = 10m,
                    StoreQty = 15m,
                    IsOwnerAdjusted = true,
                    FinalQty = 15m
                }
            }
        };

        var draft = await _service.SaveMatrixDraftAsync(command);

        // Assert
        Assert.NotNull(draft);
        Assert.Equal("DRAFT", draft.Status);
        Assert.Equal(15m, draft.TotalQty);
        Assert.Equal(15m * 160000m, draft.TotalAmount);
        Assert.True(_uow.HasSaved);
    }

    [Fact]
    public async Task REQ_PO_03_SplitMatrixPo_GeneratesDistinctPoPerDeliveryDateAndSupplier()
    {
        // Arrange: 2 delivery dates × 2 suppliers -> Expected 3 distinct POs
        var command = new SplitMatrixPoCommand
        {
            BranchId = _branchId,
            Note = "Chốt đơn từ ma trận",
            Groups = new List<SplitMatrixPoGroupCommand>
            {
                new SplitMatrixPoGroupCommand
                {
                    SupplierId = _supplier1Id,
                    DeliveryDate = _deliveryA,
                    Items = new List<SplitMatrixPoItemCommand>
                    {
                        new SplitMatrixPoItemCommand { ProductId = _prod1Id, Quantity = 20m, UnitPrice = 160000m, Unit = "Túi" }
                    }
                },
                new SplitMatrixPoGroupCommand
                {
                    SupplierId = _supplier1Id,
                    DeliveryDate = _deliveryB,
                    Items = new List<SplitMatrixPoItemCommand>
                    {
                        new SplitMatrixPoItemCommand { ProductId = _prod1Id, Quantity = 15m, UnitPrice = 160000m, Unit = "Túi" }
                    }
                },
                new SplitMatrixPoGroupCommand
                {
                    SupplierId = _supplier2Id,
                    DeliveryDate = _deliveryA,
                    Items = new List<SplitMatrixPoItemCommand>
                    {
                        new SplitMatrixPoItemCommand { ProductId = _prod2Id, Quantity = 50m, UnitPrice = 12000m, Unit = "Gói" }
                    }
                }
            }
        };

        // Act
        var createdPos = await _service.SplitMatrixPoAsync(command);

        // Assert
        Assert.Equal(3, createdPos.Count);
        Assert.All(createdPos, po =>
        {
            Assert.Equal("PENDING_APPROVAL", po.Status);
            Assert.Equal(_branchId, po.BranchId);
            Assert.StartsWith("PO-", po.Code);
            Assert.NotEmpty(po.Details);
        });
        Assert.True(_uow.HasSaved);
    }

    [Fact]
    public async Task REQ_PO_04_UpdatePurchaseOrderStatus_ApprovedSuccessfully()
    {
        // Arrange
        var poId = Guid.NewGuid();
        _repo.PurchaseOrders.Add(new PurchaseOrder
        {
            Id = poId,
            TenantId = "tenant-test-1",
            Code = "PO-20261103-0001",
            BranchId = _branchId,
            SupplierId = _supplier1Id,
            Status = "PENDING_APPROVAL",
            DeliveryDate = new DateTime(2026, 11, 3)
        });

        // Act
        var updated = await _service.UpdatePurchaseOrderStatusAsync(poId, "APPROVED");

        // Assert
        Assert.Equal("APPROVED", updated.Status);
        Assert.True(_uow.HasSaved);
    }

    [Fact]
    public async Task REQ_PO_06_EmployeeAndOwnerQuantity_3TierLogicAndPermissions()
    {
        // 1. Employee nhập số lượng đề xuất 20
        _tenantContext.SetRole("EMPLOYEE");
        var cellDate = _deliveryA;
        var empCommand = new SaveMatrixDraftCommand
        {
            BranchId = _branchId,
            SupplierId = _supplier1Id,
            ViewFrom = cellDate,
            ViewTo = cellDate.AddDays(7),
            Cells = new List<OrderMatrixCellDto>
            {
                new OrderMatrixCellDto
                {
                    ProductId = _prod1Id,
                    DeliveryDate = cellDate,
                    EmployeeQty = 20m,
                    StoreQty = null,
                    IsOwnerAdjusted = false,
                    FinalQty = 20m
                }
            }
        };

        var draft1 = await _service.SaveMatrixDraftAsync(empCommand);
        var detail1 = draft1.Details.First();
        Assert.Equal(20m, detail1.EmployeeQty);
        Assert.Null(detail1.StoreQty);
        Assert.False(detail1.FinalQty == 15m);
        Assert.Equal(20m, detail1.FinalQty);

        // 2. Store Owner xem và điều chỉnh số lượng thành 15
        _tenantContext.SetRole("STORE_OWNER");
        var ownerCommand = new SaveMatrixDraftCommand
        {
            BranchId = _branchId,
            SupplierId = _supplier1Id,
            ViewFrom = cellDate,
            ViewTo = cellDate.AddDays(7),
            Cells = new List<OrderMatrixCellDto>
            {
                new OrderMatrixCellDto
                {
                    ProductId = _prod1Id,
                    DeliveryDate = cellDate,
                    EmployeeQty = 20m,
                    StoreQty = 15m,
                    IsOwnerAdjusted = true,
                    FinalQty = 15m
                }
            }
        };

        var draft2 = await _service.SaveMatrixDraftAsync(ownerCommand);
        var detail2 = draft2.Details.First();
        Assert.Equal(20m, detail2.EmployeeQty); // Employee đề xuất ban đầu KHÔNG bị ghi đè
        Assert.Equal(15m, detail2.StoreQty);
        Assert.Equal(15m, detail2.FinalQty);

        // 3. Employee tiếp tục cập nhật đề xuất lên 25 -> Không được ghi đè số lượng Owner đã chốt
        _tenantContext.SetRole("EMPLOYEE");
        var empCommand2 = new SaveMatrixDraftCommand
        {
            BranchId = _branchId,
            SupplierId = _supplier1Id,
            ViewFrom = cellDate,
            ViewTo = cellDate.AddDays(7),
            Cells = new List<OrderMatrixCellDto>
            {
                new OrderMatrixCellDto
                {
                    ProductId = _prod1Id,
                    DeliveryDate = cellDate,
                    EmployeeQty = 25m,
                    StoreQty = null,
                    IsOwnerAdjusted = false,
                    FinalQty = 25m
                }
            }
        };

        var draft3 = await _service.SaveMatrixDraftAsync(empCommand2);
        var detail3 = draft3.Details.First();
        Assert.Equal(25m, detail3.EmployeeQty); // Đề xuất mới được lưu
        Assert.Equal(15m, detail3.StoreQty);    // StoreQty của Owner vẫn giữ nguyên 15
        Assert.Equal(15m, detail3.FinalQty);    // FinalQty vẫn là 15 theo quyết định của Owner

        // 4. Store Owner khôi phục (Reset Override) -> finalQuantity tự động quay về đề xuất mới nhất của Employee (25)
        _tenantContext.SetRole("STORE_OWNER");
        var revertCommand = new SaveMatrixDraftCommand
        {
            BranchId = _branchId,
            SupplierId = _supplier1Id,
            ViewFrom = cellDate,
            ViewTo = cellDate.AddDays(7),
            Cells = new List<OrderMatrixCellDto>
            {
                new OrderMatrixCellDto
                {
                    ProductId = _prod1Id,
                    DeliveryDate = cellDate,
                    EmployeeQty = 25m,
                    StoreQty = null,
                    IsOwnerAdjusted = false,
                    FinalQty = 25m
                }
            }
        };

        var draft4 = await _service.SaveMatrixDraftAsync(revertCommand);
        var detail4 = draft4.Details.First();
        Assert.Null(detail4.StoreQty);
        Assert.Equal(25m, detail4.EmployeeQty);
        Assert.Equal(25m, detail4.FinalQty); // Khôi phục lấy số lượng Employee mới nhất
    }

    [Fact]
    public async Task REQ_PO_07_StoreOwner_CanAdjustQuantityToZero()
    {
        // Arrange: Employee đề xuất 20, Owner chủ động gán 0
        _tenantContext.SetRole("STORE_OWNER");
        var cellDate = _deliveryA;
        var command = new SaveMatrixDraftCommand
        {
            BranchId = _branchId,
            SupplierId = _supplier1Id,
            ViewFrom = cellDate,
            ViewTo = cellDate.AddDays(7),
            Cells = new List<OrderMatrixCellDto>
            {
                new OrderMatrixCellDto
                {
                    ProductId = _prod1Id,
                    DeliveryDate = cellDate,
                    EmployeeQty = 20m,
                    StoreQty = 0m,
                    IsOwnerAdjusted = true,
                    FinalQty = 0m
                }
            }
        };

        // Act
        var draft = await _service.SaveMatrixDraftAsync(command);
        var detail = draft.Details.First();

        // Assert
        Assert.Equal(20m, detail.EmployeeQty);
        Assert.Equal(0m, detail.StoreQty);
        Assert.Equal(0m, detail.FinalQty);
    }

    [Fact]
    public async Task REQ_PO_08_DraftIdempotency_MultipleSavesDoNotDuplicateDetails()
    {
        // Arrange
        _tenantContext.SetRole("EMPLOYEE");
        var cellDate = _deliveryA;
        var command1 = new SaveMatrixDraftCommand
        {
            BranchId = _branchId,
            SupplierId = _supplier1Id,
            ViewFrom = cellDate,
            ViewTo = cellDate.AddDays(7),
            Cells = new List<OrderMatrixCellDto>
            {
                new OrderMatrixCellDto
                {
                    ProductId = _prod1Id,
                    DeliveryDate = cellDate,
                    EmployeeQty = 12m,
                    FinalQty = 12m
                }
            }
        };

        // Act - Save 1st time
        var draft1 = await _service.SaveMatrixDraftAsync(command1);
        Assert.Single(draft1.Details);
        Assert.Equal(12m, draft1.TotalQty);

        // Act - Save 2nd time with updated value
        var command2 = new SaveMatrixDraftCommand
        {
            BranchId = _branchId,
            SupplierId = _supplier1Id,
            ViewFrom = cellDate,
            ViewTo = cellDate.AddDays(7),
            Cells = new List<OrderMatrixCellDto>
            {
                new OrderMatrixCellDto
                {
                    ProductId = _prod1Id,
                    DeliveryDate = cellDate,
                    EmployeeQty = 18m,
                    FinalQty = 18m
                }
            }
        };
        var draft2 = await _service.SaveMatrixDraftAsync(command2);

        // Assert: No duplicates created, only updated existing detail
        Assert.Single(draft2.Details);
        Assert.Equal(18m, draft2.TotalQty);
        Assert.Equal(18m * 160000m, draft2.TotalAmount);
    }

    [Fact]
    public async Task REQ_PO_09_DispatchHub_ScopeNearest_SelectsEarliestDeliveryDateWithQty()
    {
        // Arrange
        var nearDate = DateTime.Today.AddDays(3);
        var farDate = DateTime.Today.AddDays(7);
        var zeroDate = DateTime.Today.AddDays(2);

        await _service.SaveMatrixDraftAsync(new SaveMatrixDraftCommand
        {
            BranchId = _branchId,
            SupplierId = _supplier1Id,
            ViewFrom = zeroDate,
            ViewTo = farDate.AddDays(1),
            Cells = new List<OrderMatrixCellDto>
            {
                new OrderMatrixCellDto { ProductId = _prod1Id, DeliveryDate = zeroDate, EmployeeQty = 0m, FinalQty = 0m },
                new OrderMatrixCellDto { ProductId = _prod1Id, DeliveryDate = nearDate, EmployeeQty = 10m, FinalQty = 10m },
                new OrderMatrixCellDto { ProductId = _prod1Id, DeliveryDate = farDate, EmployeeQty = 20m, FinalQty = 20m }
            }
        });

        // Act
        var result = await _service.SplitMatrixPoBatchAsync(new SplitMatrixPoCommand
        {
            BranchId = _branchId,
            Scope = OrderMatrixRules.ScopeNearest
        });

        // Assert
        Assert.Equal(1, result.TotalGroups);
        Assert.Equal(1, result.SuccessCount);
        var group = result.Results.Single();
        Assert.Equal(nearDate.Date, group.DeliveryDate.Date);
        Assert.Equal(OrderMatrixRules.ResultSuccess, group.Status);
        Assert.NotNull(group.PurchaseOrderId);
    }

    [Fact]
    public async Task REQ_PO_10_DispatchHub_ScopeWeek_ProcessesAllDaysInMondayToSundayWeek()
    {
        // Arrange: Chọn tuần tới bắt đầu từ Monday
        var nextMonday = OrderMatrixRules.NormalizeToMonday(DateTime.Today.AddDays(7));
        var tuesday = nextMonday.AddDays(1);
        var thursday = nextMonday.AddDays(3);
        var nextWeekMonday = nextMonday.AddDays(7);

        await _service.SaveMatrixDraftAsync(new SaveMatrixDraftCommand
        {
            BranchId = _branchId,
            SupplierId = _supplier1Id,
            ViewFrom = nextMonday,
            ViewTo = nextWeekMonday.AddDays(2),
            Cells = new List<OrderMatrixCellDto>
            {
                new OrderMatrixCellDto { ProductId = _prod1Id, DeliveryDate = tuesday, EmployeeQty = 5m, FinalQty = 5m },
                new OrderMatrixCellDto { ProductId = _prod1Id, DeliveryDate = thursday, EmployeeQty = 8m, FinalQty = 8m },
                new OrderMatrixCellDto { ProductId = _prod1Id, DeliveryDate = nextWeekMonday, EmployeeQty = 12m, FinalQty = 12m }
            }
        });

        // Act
        var result = await _service.SplitMatrixPoBatchAsync(new SplitMatrixPoCommand
        {
            BranchId = _branchId,
            Scope = OrderMatrixRules.ScopeWeek,
            WeekStartDate = nextMonday.ToString("yyyy-MM-dd")
        });

        // Assert
        Assert.Equal(2, result.TotalGroups);
        Assert.Equal(2, result.SuccessCount);
        var dates = result.Results.Select(g => g.DeliveryDate.Date).OrderBy(d => d).ToList();
        Assert.Equal(tuesday.Date, dates[0]);
        Assert.Equal(thursday.Date, dates[1]);
    }

    [Fact]
    public async Task REQ_PO_11_DispatchHub_PartialSuccess_OneGroupFailsMoqOthersSucceed()
    {
        // Arrange: Matrix product 1 có Moq = 50, nhưng đặt FinalQty = 5 -> Vi phạm MOQ -> FAIL
        var p1 = _repo.MatrixProducts.First(p => p.Id == _prod1Id);
        p1.Moq = 50;

        var deliveryDate = DateTime.Today.AddDays(4);

        await _service.SaveMatrixDraftAsync(new SaveMatrixDraftCommand
        {
            BranchId = _branchId,
            ViewFrom = deliveryDate,
            ViewTo = deliveryDate.AddDays(2),
            Cells = new List<OrderMatrixCellDto>
            {
                new OrderMatrixCellDto { ProductId = _prod1Id, DeliveryDate = deliveryDate, EmployeeQty = 5m, FinalQty = 5m },  // 5 < 50 MOQ -> FAILED
                new OrderMatrixCellDto { ProductId = _prod2Id, DeliveryDate = deliveryDate, EmployeeQty = 20m, FinalQty = 20m }  // MOQ 20 -> SUCCESS
            }
        });

        // Act
        var result = await _service.SplitMatrixPoBatchAsync(new SplitMatrixPoCommand
        {
            BranchId = _branchId,
            Scope = OrderMatrixRules.ScopeNearest
        });

        // Assert: Group 1 FAILED, Group 2 SUCCESS
        var sup1Result = result.Results.FirstOrDefault(g => g.SupplierId == _supplier1Id);
        var sup2Result = result.Results.FirstOrDefault(g => g.SupplierId == _supplier2Id);

        Assert.NotNull(sup1Result);
        Assert.Equal(OrderMatrixRules.ResultFailed, sup1Result.Status);
        Assert.Equal("VALIDATION_FAILED", sup1Result.ErrorCode);

        Assert.NotNull(sup2Result);
        Assert.Equal(OrderMatrixRules.ResultSuccess, sup2Result.Status);
        Assert.NotNull(sup2Result.PurchaseOrderId);

        // Teardown
        p1.Moq = 1;
    }

    [Fact]
    public async Task REQ_PO_12_BeforeCutoff_ReSplitSameQty_ReturnsNoChanges()
    {
        // Arrange
        var deliveryDate = DateTime.Today.AddDays(5);
        await _service.SaveMatrixDraftAsync(new SaveMatrixDraftCommand
        {
            BranchId = _branchId,
            SupplierId = _supplier2Id,
            ViewFrom = deliveryDate,
            ViewTo = deliveryDate.AddDays(1),
            Cells = new List<OrderMatrixCellDto>
            {
                new OrderMatrixCellDto { ProductId = _prod2Id, DeliveryDate = deliveryDate, EmployeeQty = 20m, FinalQty = 20m }
            }
        });

        // Act 1: Chốt lần 1
        var res1 = await _service.SplitMatrixPoBatchAsync(new SplitMatrixPoCommand
        {
            BranchId = _branchId,
            Scope = OrderMatrixRules.ScopeNearest
        });
        Assert.Equal(1, res1.SuccessCount);
        var firstPoId = res1.Results.First().PurchaseOrderId;
        var firstPoCode = res1.Results.First().PurchaseOrderCode;
        Assert.Equal(1, res1.Results.First().RevisionNumber);

        // Act 2: Chốt lại cùng số lượng → NO_CHANGES (không tạo PO mới)
        var res2 = await _service.SplitMatrixPoBatchAsync(new SplitMatrixPoCommand
        {
            BranchId = _branchId,
            Scope = OrderMatrixRules.ScopeNearest
        });

        Assert.Equal(1, res2.NoChangesCount);
        var group2 = res2.Results.First();
        Assert.Equal(OrderMatrixRules.ResultNoChanges, group2.Status);
        Assert.Equal(firstPoId, group2.PurchaseOrderId);
        Assert.Equal(firstPoCode, group2.PurchaseOrderCode);
        Assert.Equal(1, _repo.PurchaseOrders.Count(p => p.Status != "DRAFT"));
    }

    [Fact]
    public async Task REQ_PO_12b_BeforeCutoff_AmendQty_UpdatesSamePoAndBumpsRevision()
    {
        var deliveryDate = DateTime.Today.AddDays(5);
        await _service.SaveMatrixDraftAsync(new SaveMatrixDraftCommand
        {
            BranchId = _branchId,
            SupplierId = _supplier2Id,
            ViewFrom = deliveryDate,
            ViewTo = deliveryDate.AddDays(1),
            Cells = new List<OrderMatrixCellDto>
            {
                new OrderMatrixCellDto { ProductId = _prod2Id, DeliveryDate = deliveryDate, EmployeeQty = 20m, FinalQty = 20m }
            }
        });

        var res1 = await _service.SplitMatrixPoBatchAsync(new SplitMatrixPoCommand
        {
            BranchId = _branchId,
            Scope = OrderMatrixRules.ScopeNearest
        });
        var poId = res1.Results.First().PurchaseOrderId!.Value;

        await _service.SaveMatrixDraftAsync(new SaveMatrixDraftCommand
        {
            BranchId = _branchId,
            SupplierId = _supplier2Id,
            ViewFrom = deliveryDate,
            ViewTo = deliveryDate.AddDays(1),
            Cells = new List<OrderMatrixCellDto>
            {
                new OrderMatrixCellDto
                {
                    ProductId = _prod2Id,
                    DeliveryDate = deliveryDate,
                    StoreQty = 40m,
                    IsOwnerAdjusted = true,
                    FinalQty = 40m
                }
            }
        });

        var res2 = await _service.SplitMatrixPoBatchAsync(new SplitMatrixPoCommand
        {
            BranchId = _branchId,
            Scope = OrderMatrixRules.ScopeNearest
        });

        Assert.Equal(1, res2.AmendedCount);
        var group = res2.Results.First();
        Assert.Equal(OrderMatrixRules.ResultAmended, group.Status);
        Assert.Equal(poId, group.PurchaseOrderId);
        Assert.Equal(2, group.RevisionNumber);
        Assert.Equal(1, _repo.PurchaseOrders.Count(p => p.Status != "DRAFT"));
        Assert.Single(_repo.Revisions);
        var po = _repo.PurchaseOrders.First(p => p.Id == poId);
        Assert.Equal(2, po.RevisionNumber);
        Assert.Equal(40m, po.Details.Where(d => !d.IsDeleted).Sum(d => d.OrderedQty ?? 0m));
    }

    [Fact]
    public async Task REQ_PO_12c_AfterCutoff_ExistingPo_ReturnsAlreadyFinalized()
    {
        // Cutoff cùng ngày giao @ 00:00 hôm nay → đã qua
        var deliveryDate = DateTime.Today;
        _repo.CustomSupplierConfigs = new List<OrderMatrixSupplierDeliveryConfigDto>
        {
            new OrderMatrixSupplierDeliveryConfigDto
            {
                SupplierId = _supplier2Id,
                SupplierCode = "NCC-02",
                SupplierName = "Bánh Kẹo SnackVN",
                IsActive = true,
                IsAssignedToBranch = true,
                CutoffTime = "00:00",
                BlackoutDates = new List<string>(),
                ActiveDeliveryDays = new List<int> { 0, 1, 2, 3, 4, 5, 6 }
            }
        };

        var po = new PurchaseOrder
        {
            TenantId = "tenant-test-1",
            Code = "PO-LOCKED-001",
            BranchId = _branchId,
            SupplierId = _supplier2Id,
            DeliveryDate = deliveryDate,
            Status = "PENDING_APPROVAL",
            RevisionNumber = 1,
            TotalQty = 20m,
            Details =
            {
                new PurchaseOrderDetail
                {
                    TenantId = "tenant-test-1",
                    ProductId = _prod2Id,
                    DeliveryDate = deliveryDate,
                    OrderedQty = 20m,
                    FinalQty = 20m,
                    UnitPrice = 12000m,
                    LineAmount = 240000m
                }
            }
        };
        _repo.PurchaseOrders.Add(po);

        await _service.SaveMatrixDraftAsync(new SaveMatrixDraftCommand
        {
            BranchId = _branchId,
            SupplierId = _supplier2Id,
            ViewFrom = deliveryDate,
            ViewTo = deliveryDate.AddDays(1),
            Cells = new List<OrderMatrixCellDto>
            {
                new OrderMatrixCellDto { ProductId = _prod2Id, DeliveryDate = deliveryDate, EmployeeQty = 30m, FinalQty = 30m }
            }
        });

        var res = await _service.SplitMatrixPoBatchAsync(new SplitMatrixPoCommand
        {
            BranchId = _branchId,
            SupplierId = _supplier2Id,
            Groups = new List<SplitMatrixPoGroupCommand>
            {
                new SplitMatrixPoGroupCommand { SupplierId = _supplier2Id, DeliveryDate = deliveryDate }
            }
        });

        Assert.Equal(1, res.AlreadyFinalizedCount);
        Assert.Equal(OrderMatrixRules.ResultAlreadyFinalized, res.Results.First().Status);
        Assert.Equal(20m, po.Details.Sum(d => d.OrderedQty ?? 0m));
    }

    [Fact]
    public async Task REQ_PO_13_DispatchHub_CutoffExpired_SkipsExpiredGroup()
    {
        // Cutoff = hôm nay 00:00 (LeadTime 0) → LOCKED / CUTOFF_EXPIRED
        var today = DateTime.Today;
        _repo.CustomSupplierConfigs = new List<OrderMatrixSupplierDeliveryConfigDto>
        {
            new OrderMatrixSupplierDeliveryConfigDto
            {
                SupplierId = _supplier1Id,
                SupplierCode = "SUP-01",
                SupplierName = "Công ty Hải Nam",
                IsActive = true,
                IsAssignedToBranch = true,
                CutoffTime = "00:00",
                BlackoutDates = new List<string>(),
                ActiveDeliveryDays = new List<int> { 0, 1, 2, 3, 4, 5, 6 }
            }
        };

        await _service.SaveMatrixDraftAsync(new SaveMatrixDraftCommand
        {
            BranchId = _branchId,
            SupplierId = _supplier1Id,
            ViewFrom = today,
            ViewTo = today.AddDays(2),
            Cells = new List<OrderMatrixCellDto>
            {
                new OrderMatrixCellDto { ProductId = _prod1Id, DeliveryDate = today, EmployeeQty = 10m, FinalQty = 10m }
            }
        });

        // Act — Groups buộc đưa ngày đã cutoff vào engine (NEAREST sẽ bỏ qua ngày LOCKED)
        var result = await _service.SplitMatrixPoBatchAsync(new SplitMatrixPoCommand
        {
            BranchId = _branchId,
            SupplierId = _supplier1Id,
            Groups = new List<SplitMatrixPoGroupCommand>
            {
                new SplitMatrixPoGroupCommand { SupplierId = _supplier1Id, DeliveryDate = today }
            }
        });

        // Assert
        Assert.Equal(1, result.SkippedCount);
        var group = result.Results.Single();
        Assert.Equal(OrderMatrixRules.ResultSkipped, group.Status);
        Assert.Equal("CUTOFF_EXPIRED", group.ErrorCode);

        _repo.CustomSupplierConfigs = null;
    }

    // =========================================================================
    // FAKE REPOSITORY
    // =========================================================================

    private class FakePurchaseOrderRepository : IPurchaseOrderRepository
    {
        public List<Branch> Branches { get; } = new();
        public List<OrderMatrixSupplierDeliveryConfigDto>? CustomSupplierConfigs { get; set; }
        public List<Supplier> Suppliers { get; } = new();
        public List<Product> Products { get; } = new();
        public List<OrderMatrixProductDto> MatrixProducts { get; } = new();
        public List<PurchaseOrder> PurchaseOrders { get; } = new();
        private int _poCounter = 0;

        public Task<IReadOnlyDictionary<Guid, (Guid ReceiptId, string Code, string Status)>> GetActiveGoodsReceiptsByPoIdsAsync(
            IReadOnlyCollection<Guid> purchaseOrderIds,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyDictionary<Guid, (Guid ReceiptId, string Code, string Status)> res =
                new Dictionary<Guid, (Guid, string, string)>();
            return Task.FromResult(res);
        }


        public Task<Branch?> GetBranchByIdAsync(Guid branchId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Branches.FirstOrDefault(b => b.Id == branchId));
        }

        public Task<Supplier?> GetSupplierByIdAsync(Guid supplierId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Suppliers.FirstOrDefault(s => s.Id == supplierId));
        }

        public Task<Product?> GetProductByIdAsync(Guid productId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Products.FirstOrDefault(p => p.Id == productId));
        }

        public Task<IReadOnlyList<OrderMatrixProductDto>> GetMatrixProductsAsync(
            Guid branchId,
            Guid? supplierId,
            CancellationToken cancellationToken = default)
        {
            var query = MatrixProducts.AsEnumerable();
            if (supplierId.HasValue && supplierId.Value != Guid.Empty)
            {
                query = query.Where(p => p.SupplierId == supplierId.Value);
            }
            return Task.FromResult((IReadOnlyList<OrderMatrixProductDto>)query.ToList());
        }

        public Task<IReadOnlyList<OrderMatrixSupplierDeliveryConfigDto>> GetMatrixSupplierConfigsAsync(
            Guid branchId,
            Guid? supplierId,
            CancellationToken cancellationToken = default)
        {
            if (CustomSupplierConfigs != null)
            {
                return Task.FromResult((IReadOnlyList<OrderMatrixSupplierDeliveryConfigDto>)CustomSupplierConfigs);
            }
            var query = Suppliers.AsEnumerable();
            if (supplierId.HasValue && supplierId.Value != Guid.Empty)
            {
                query = query.Where(s => s.Id == supplierId.Value);
            }

            var list = query.Select(s => new OrderMatrixSupplierDeliveryConfigDto
            {
                SupplierId = s.Id,
                SupplierCode = s.Code,
                SupplierName = s.Name,
                IsActive = s.Status == SupplierStatus.Active,
                IsAssignedToBranch = true,
                CutoffTime = "23:59",
                BlackoutDates = new List<string>(),
                ActiveDeliveryDays = new List<int> { 0, 1, 2, 3, 4, 5, 6 }
            }).ToList();

            return Task.FromResult((IReadOnlyList<OrderMatrixSupplierDeliveryConfigDto>)list);
        }

        public Task<PurchaseOrder?> FindCommittedPoAsync(
            Guid branchId,
            Guid supplierId,
            DateTime deliveryDate,
            CancellationToken cancellationToken = default)
        {
            var day = deliveryDate.Date;
            var po = PurchaseOrders.FirstOrDefault(p =>
                p.BranchId == branchId
                && p.SupplierId == supplierId
                && p.Status != "DRAFT"
                && p.Status != "CANCELLED"
                && p.Status != "REJECTED"
                && p.DeliveryDate.HasValue
                && p.DeliveryDate.Value.Date == day);
            return Task.FromResult(po);
        }

        public Task<IReadOnlyList<PurchaseOrder>> GetCommittedPosInRangeAsync(
            Guid branchId,
            Guid? supplierId,
            DateTime viewFrom,
            int durationDays,
            CancellationToken cancellationToken = default)
        {
            var from = viewFrom.Date;
            var to = from.AddDays(Math.Clamp(durationDays, 1, 31));
            var query = PurchaseOrders.Where(p =>
                p.BranchId == branchId
                && p.Status != "DRAFT"
                && p.Status != "CANCELLED"
                && p.Status != "REJECTED"
                && p.DeliveryDate.HasValue
                && p.DeliveryDate.Value.Date >= from
                && p.DeliveryDate.Value.Date < to);
            if (supplierId.HasValue && supplierId.Value != Guid.Empty)
            {
                query = query.Where(p => p.SupplierId == supplierId.Value);
            }

            return Task.FromResult((IReadOnlyList<PurchaseOrder>)query.ToList());
        }

        public Task<SupplierProduct?> GetSupplierProductAsync(
            Guid supplierId,
            Guid productId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<SupplierProduct?>(null);
        }

        public Task<PurchaseOrder?> GetDraftMatrixPoAsync(
            Guid branchId,
            Guid? supplierId,
            DateTime viewFrom,
            CancellationToken cancellationToken = default)
        {
            var q = PurchaseOrders.Where(po => po.BranchId == branchId && po.Status == "DRAFT");
            if (supplierId.HasValue && supplierId.Value != Guid.Empty)
            {
                q = q.Where(po => po.SupplierId == supplierId.Value);
            }
            return Task.FromResult(q.FirstOrDefault());
        }

        public Task<IReadOnlyList<PurchaseOrder>> GetDraftMatrixPosAsync(
            Guid branchId,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<PurchaseOrder> list = PurchaseOrders
                .Where(po => po.BranchId == branchId && po.Status == "DRAFT" && !po.IsDeleted)
                .ToList();
            return Task.FromResult(list);
        }

        public Task<(IReadOnlyList<PurchaseOrder> Items, int TotalCount)> GetPagedPurchaseOrdersAsync(
            Guid? branchId,
            Guid? supplierId,
            string? status,
            DateTime? dateFrom,
            DateTime? dateTo,
            int pageIndex,
            int pageSize,
            string? keyword,
            CancellationToken cancellationToken = default)
        {
            var query = PurchaseOrders.Where(p => p.Status != "DRAFT").AsEnumerable();
            if (branchId.HasValue) query = query.Where(p => p.BranchId == branchId.Value);
            if (supplierId.HasValue) query = query.Where(p => p.SupplierId == supplierId.Value);
            if (!string.IsNullOrEmpty(status) && status != "ALL") query = query.Where(p => p.Status == status);

            var list = query.ToList();
            return Task.FromResult(((IReadOnlyList<PurchaseOrder>)list, list.Count));
        }

        public Task<PurchaseOrder?> GetPurchaseOrderByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(PurchaseOrders.FirstOrDefault(p => p.Id == id));
        }

        public Task AddPurchaseOrderAsync(PurchaseOrder purchaseOrder, CancellationToken cancellationToken = default)
        {
            PurchaseOrders.Add(purchaseOrder);
            return Task.CompletedTask;
        }

        public Task<string> GeneratePoCodeAsync(DateTime deliveryDate, CancellationToken cancellationToken = default)
        {
            _poCounter++;
            return Task.FromResult($"PO-{deliveryDate:yyyyMMdd}-{_poCounter:D4}");
        }

        public Task<bool> UserExistsAsync(string userId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }

        public void AddPurchaseOrderDetail(PurchaseOrderDetail detail)
        {
            // No-op in memory test
        }

        public List<PurchaseOrderRevision> Revisions { get; } = new();

        public void AddPurchaseOrderRevision(PurchaseOrderRevision revision)
        {
            Revisions.Add(revision);
        }

        public Task<IReadOnlyList<Branch>> GetActiveBranchesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult((IReadOnlyList<Branch>)Branches.Where(b => b.IsActive && !b.IsDeleted).ToList());
        }

        public Task<IReadOnlyList<Guid>> GetActiveBranchIdsForSupplierAsync(
            Guid supplierId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult((IReadOnlyList<Guid>)Branches.Where(b => b.IsActive && !b.IsDeleted).Select(b => b.Id).ToList());
        }
    }

    private class FakeSupplierDispatchPackageService : ISupplierDispatchPackageService
    {
        public Task<SupplierDispatchPackageDto?> CreatePackageForDispatchAsync(
            SplitMatrixPoCommand command,
            SplitMatrixBatchResultDto batchResult,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<SupplierDispatchPackageDto?>(null);

        public Task<SupplierDispatchPackageDto?> GetByIdAsync(Guid packageId, CancellationToken cancellationToken = default) =>
            Task.FromResult<SupplierDispatchPackageDto?>(null);

        public Task<IReadOnlyList<SupplierDispatchPackageDto>> ListAsync(
            Guid? supplierId,
            int take = 50,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SupplierDispatchPackageDto>>(Array.Empty<SupplierDispatchPackageDto>());

        public Task RetryGenerationAsync(Guid packageId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RetryEmailAsync(Guid packageId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CancelScheduledAsync(Guid packageId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<(byte[] Content, string FileName, string ContentType)> DownloadAsync(
            Guid packageId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult((Array.Empty<byte>(), "empty.xlsx", "application/octet-stream"));

        public Task ProcessPackageAsync(Guid packageId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private class FakeUnitOfWork : IUnitOfWork
    {
        public bool HasSaved { get; private set; }
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            HasSaved = true;
            return Task.FromResult(1);
        }

        public Task BeginTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CommitTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RollbackTransactionAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public async Task ExecuteInTransactionAsync(
            Func<CancellationToken, Task> action,
            CancellationToken cancellationToken = default)
        {
            await action(cancellationToken);
        }
    }

    private class FakeTenantContext : ITenantContext
    {
        public string CurrentTenantId { get; set; } = "tenant-test-1";
        public Guid CurrentUserId { get; set; } = Guid.Parse("11111111-1111-1111-1111-111111111111");
        public Guid? CurrentBranchId { get; set; } = null;
        public string CurrentRole { get; set; } = "STORE_OWNER";

        public void SetContext(string tenantId, Guid userId, Guid? branchId)
        {
            CurrentTenantId = tenantId;
            CurrentUserId = userId;
            CurrentBranchId = branchId;
        }

        public void SetRole(string? role) => CurrentRole = role?.Trim() ?? string.Empty;

        public FakeTenantContext(string tenantId) => CurrentTenantId = tenantId;
    }
}
