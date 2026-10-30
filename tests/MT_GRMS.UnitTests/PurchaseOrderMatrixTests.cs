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
