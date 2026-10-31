using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.PurchaseOrders;
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

    public PurchaseOrderMatrixTests()
    {
        _repo = new FakePurchaseOrderRepository();
        _uow = new FakeUnitOfWork();
        _tenantContext = new FakeTenantContext("tenant-test-1");
        _service = new PurchaseOrderService(_repo, _uow, _tenantContext);

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
    public async Task REQ_PO_01_GetOrderMatrix_ReturnsProductsAndBranchDetails()
    {
        // Act
        var res = await _service.GetOrderMatrixAsync(new OrderMatrixQuery
        {
            BranchId = _branchId,
            ViewFrom = new DateTime(2026, 11, 2),
            DurationDays = 7
        });

        // Assert
        Assert.NotNull(res);
        Assert.Equal(_branchId, res.BranchId);
        Assert.Equal("Chi nhánh Nguyễn Trãi", res.BranchName);
        Assert.Equal(2, res.Products.Count);
        Assert.NotNull(res.SupplierConfigs);
        Assert.Equal(2, res.SupplierConfigs.Count);
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
            ViewFrom = new DateTime(2026, 11, 2),
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
    }

    [Fact]
    public async Task REQ_PO_02_SaveMatrixDraft_SavesDraftPoAndDetails()
    {
        // Act
        var command = new SaveMatrixDraftCommand
        {
            BranchId = _branchId,
            SupplierId = _supplier1Id,
            ViewFrom = new DateTime(2026, 11, 2),
            ViewTo = new DateTime(2026, 11, 8),
            Note = "Kế hoạch tuần 1 tháng 11",
            Cells = new List<OrderMatrixCellDto>
            {
                new OrderMatrixCellDto
                {
                    ProductId = _prod1Id,
                    DeliveryDate = new DateTime(2026, 11, 3),
                    EmployeeQty = 10m,
                    StoreQty = 15m,
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
        // Arrange: 2 delivery dates (T3 03/11, T5 05/11) with 2 suppliers -> Expected 3 distinct POs
        var command = new SplitMatrixPoCommand
        {
            BranchId = _branchId,
            Note = "Chốt đơn từ ma trận",
            Groups = new List<SplitMatrixPoGroupCommand>
            {
                // Group 1: NCC 1 - Date 03/11
                new SplitMatrixPoGroupCommand
                {
                    SupplierId = _supplier1Id,
                    DeliveryDate = new DateTime(2026, 11, 3),
                    Items = new List<SplitMatrixPoItemCommand>
                    {
                        new SplitMatrixPoItemCommand { ProductId = _prod1Id, Quantity = 20m, UnitPrice = 160000m, Unit = "Túi" }
                    }
                },
                // Group 2: NCC 1 - Date 05/11
                new SplitMatrixPoGroupCommand
                {
                    SupplierId = _supplier1Id,
                    DeliveryDate = new DateTime(2026, 11, 5),
                    Items = new List<SplitMatrixPoItemCommand>
                    {
                        new SplitMatrixPoItemCommand { ProductId = _prod1Id, Quantity = 15m, UnitPrice = 160000m, Unit = "Túi" }
                    }
                },
                // Group 3: NCC 2 - Date 03/11
                new SplitMatrixPoGroupCommand
                {
                    SupplierId = _supplier2Id,
                    DeliveryDate = new DateTime(2026, 11, 3),
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
        var cellDate = new DateTime(2026, 11, 3);
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
        var cellDate = new DateTime(2026, 11, 3);
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
        var cellDate = new DateTime(2026, 11, 3);
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

    // =========================================================================
    // FAKE REPOSITORY
    // =========================================================================

    private class FakePurchaseOrderRepository : IPurchaseOrderRepository
    {
        public List<Branch> Branches { get; } = new();
        public List<Supplier> Suppliers { get; } = new();
        public List<Product> Products { get; } = new();
        public List<OrderMatrixProductDto> MatrixProducts { get; } = new();
        public List<PurchaseOrder> PurchaseOrders { get; } = new();
        private int _poCounter = 0;

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
                ActiveDeliveryDays = new List<int> { 1, 2, 3, 4, 5, 6 },
                LeadTimeDays = 1,
                CutoffTime = "15:00",
                BlackoutDates = new List<string>()
            }).ToList();

            return Task.FromResult((IReadOnlyList<OrderMatrixSupplierDeliveryConfigDto>)list);
        }

        public Task<PurchaseOrder?> GetDraftMatrixPoAsync(
            Guid branchId,
            Guid? supplierId,
            DateTime viewFrom,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(PurchaseOrders.FirstOrDefault(po => po.BranchId == branchId && po.Status == "DRAFT"));
        }

        public Task<(IReadOnlyList<PurchaseOrder> Items, int TotalCount)> GetPagedPurchaseOrdersAsync(
            Guid? branchId,
            Guid? supplierId,
            string? status,
            DateTime? dateFrom,
            DateTime? dateTo,
            int pageIndex,
            int pageSize,
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
    }

    private class FakeUnitOfWork : IUnitOfWork
    {
        public bool HasSaved { get; private set; }
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            HasSaved = true;
            return Task.FromResult(1);
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
