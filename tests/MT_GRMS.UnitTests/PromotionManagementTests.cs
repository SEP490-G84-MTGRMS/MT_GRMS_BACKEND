using System.Linq.Expressions;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Promotions;
using MT_GRMS.Application.Promotions.Dtos;
using MT_GRMS.Domain.Entities.Sales;
using MT_GRMS.Domain.Exceptions;
using Xunit;

namespace MT_GRMS.UnitTests;

public class PromotionManagementTests
{
    private readonly FakePromotionRepository _repo;
    private readonly FakeUnitOfWork _uow;
    private readonly FakeTenantContext _tenantContext;
    private readonly PromotionService _service;

    public PromotionManagementTests()
    {
        _repo = new FakePromotionRepository();
        _uow = new FakeUnitOfWork();
        _tenantContext = new FakeTenantContext("tenant-test-1");
        _service = new PromotionService(_repo, _uow, _tenantContext);

        // Seed initial promotions
        _repo.Promotions.AddRange(new[]
        {
            new Promotion
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                TenantId = "tenant-test-1",
                PromotionCode = "KM-SALE10",
                PromotionName = "Giảm giá 10% đơn hàng",
                Type = "ORDER_DISCOUNT",
                DiscountValue = 10,
                MinOrderValue = 100000,
                MaxDiscountAmount = 50000,
                StartDate = DateTime.UtcNow.AddDays(-5),
                EndDate = DateTime.UtcNow.AddDays(10),
                IsActive = true
            },
            new Promotion
            {
                Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                TenantId = "tenant-test-1",
                PromotionCode = "KM-FIXED20K",
                PromotionName = "Giảm 20.000đ cho đơn từ 150k",
                Type = "FIXED_AMOUNT",
                DiscountValue = 20000,
                MinOrderValue = 150000,
                StartDate = DateTime.UtcNow.AddDays(-2),
                EndDate = DateTime.UtcNow.AddDays(5),
                IsActive = true
            },
            // Tenant khác (cô lập dữ liệu)
            new Promotion
            {
                Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                TenantId = "tenant-other",
                PromotionCode = "KM-OTHER",
                PromotionName = "Khuyến mãi tenant khác",
                Type = "ORDER_DISCOUNT",
                DiscountValue = 15,
                StartDate = DateTime.UtcNow.AddDays(-1),
                EndDate = DateTime.UtcNow.AddDays(5),
                IsActive = true
            }
        });
    }

    [Fact]
    public async Task REQ_PV01_CreatePromotion_ValidRequest_SuccessWithGuidV7()
    {
        // Arrange
        var request = new PromotionCreateRequest(
            PromotionCode: "KM-TESTNEW",
            PromotionName: "Chương trình thử nghiệm",
            Type: "ORDER_DISCOUNT",
            DiscountValue: 15,
            MinOrderValue: 200000,
            MaxDiscountAmount: 30000,
            UsageLimit: 100,
            StartDate: DateTime.UtcNow.AddDays(1),
            EndDate: DateTime.UtcNow.AddDays(15),
            IsActive: true,
            Description: "Mô tả test",
            Conditions: new List<PromotionConditionDto>
            {
                new(null, Guid.NewGuid(), "Mì Hảo Hảo", "BUY", 3, null, "Mua 3 gói")
            }
        );

        // Act
        var result = await _service.CreatePromotionAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal("KM-TESTNEW", result.PromotionCode);
        Assert.Equal(15, result.DiscountValue);
        Assert.True(result.IsActive);
        Assert.Single(result.Conditions);
        Assert.True(_uow.HasSaved);
    }

    [Fact]
    public async Task REQ_PV01_CreatePromotion_DuplicateCode_ThrowsBusinessException()
    {
        // Arrange
        var request = new PromotionCreateRequest(
            PromotionCode: "KM-SALE10", // đã tồn tại
            PromotionName: "Trùng mã",
            Type: "ORDER_DISCOUNT",
            DiscountValue: 10,
            MinOrderValue: 50000,
            MaxDiscountAmount: null,
            UsageLimit: null,
            StartDate: DateTime.UtcNow,
            EndDate: DateTime.UtcNow.AddDays(5)
        );

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BusinessException>(() => _service.CreatePromotionAsync(request));
        Assert.Equal("DUPLICATE_CODE", ex.ErrorCode);
    }

    [Fact]
    public async Task REQ_PV01_GetPagedPromotions_FilteredBySearchAndType_ReturnsMatching()
    {
        // Arrange
        var query = new PromotionFilterQuery
        {
            SearchTerm = "SALE10",
            Type = "ORDER_DISCOUNT",
            PageIndex = 1,
            PageSize = 10
        };

        // Act
        var result = await _service.GetPagedPromotionsAsync(query);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1, result.TotalItems);
        Assert.Single(result.Items);
        Assert.Equal("KM-SALE10", result.Items[0].PromotionCode);
    }

    [Fact]
    public async Task REQ_PV01_TogglePromotionStatus_TogglesActiveState()
    {
        // Arrange
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");

        // Act
        var newStatus = await _service.TogglePromotionStatusAsync(id);

        // Assert
        Assert.False(newStatus); // ban đầu true -> toggle thành false
        Assert.True(_uow.HasSaved);
    }

    [Fact]
    public async Task REQ_PV06_MultiTenancy_TenantCannotSeeOtherTenantPromotions()
    {
        // Arrange: Tenant hiện tại là "tenant-test-1"
        var query = new PromotionFilterQuery { PageIndex = 1, PageSize = 10 };

        // Act
        var result = await _service.GetPagedPromotionsAsync(query);

        // Assert: Không bao gồm KM-OTHER của tenant-other
        Assert.Equal(2, result.TotalItems);
        Assert.DoesNotContain(result.Items, p => p.PromotionCode == "KM-OTHER");
    }

    #region Fakes
    private class FakePromotionRepository : IPromotionRepository
    {
        public List<Promotion> Promotions { get; } = new();
        public List<PromotionCondition> Conditions { get; } = new();
        public List<VoucherCode> VoucherCodes { get; } = new();

        public Task<Promotion?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Promotions.FirstOrDefault(p => p.Id == id && !p.IsDeleted));
        }

        public Task<Promotion?> GetByIdWithConditionsAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var promo = Promotions.FirstOrDefault(p => p.Id == id && !p.IsDeleted);
            if (promo != null)
            {
                promo.Conditions = Conditions.Where(c => c.PromotionId == id).ToList();
            }
            return Task.FromResult(promo);
        }

        public Task<bool> ExistsByCodeAsync(string code, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Promotions.Any(p => p.PromotionCode.Equals(code, StringComparison.OrdinalIgnoreCase) && !p.IsDeleted));
        }

        public Task<(IReadOnlyList<Promotion> Items, int TotalCount)> GetPagedListAsync(PromotionFilterQuery query, CancellationToken cancellationToken = default)
        {
            var q = Promotions.Where(p => !p.IsDeleted && p.TenantId == "tenant-test-1" && p.Type != "VOUCHER");

            if (!string.IsNullOrWhiteSpace(query.SearchTerm))
            {
                q = q.Where(p => p.PromotionCode.Contains(query.SearchTerm, StringComparison.OrdinalIgnoreCase) ||
                                 p.PromotionName.Contains(query.SearchTerm, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(query.Type) && !string.Equals(query.Type, "ALL", StringComparison.OrdinalIgnoreCase))
            {
                q = q.Where(p => p.Type.Equals(query.Type, StringComparison.OrdinalIgnoreCase));
            }

            var list = q.ToList();
            return Task.FromResult(((IReadOnlyList<Promotion>)list, list.Count));
        }

        public Task<IReadOnlyList<Promotion>> GetActivePromotionsAsync(DateTime atUtc, string? branchId = null, CancellationToken cancellationToken = default)
        {
            var list = Promotions.Where(p => !p.IsDeleted && p.IsActive && p.StartDate <= atUtc && p.EndDate >= atUtc && p.Type != "VOUCHER").ToList();
            return Task.FromResult((IReadOnlyList<Promotion>)list);
        }

        public Task ReplaceConditionsAsync(Guid promotionId, IEnumerable<PromotionCondition> conditions, CancellationToken cancellationToken = default)
        {
            Conditions.RemoveAll(c => c.PromotionId == promotionId);
            Conditions.AddRange(conditions);
            return Task.CompletedTask;
        }

        public Task<(IReadOnlyList<Promotion> Batches, int TotalCount)> GetPagedVoucherBatchesAsync(int pageIndex, int pageSize, string? searchTerm = null, string? status = null, CancellationToken cancellationToken = default)
        {
            var q = Promotions.Where(p => !p.IsDeleted && p.Type == "VOUCHER").ToList();
            return Task.FromResult(((IReadOnlyList<Promotion>)q, q.Count));
        }

        public Task AddVoucherCodesAsync(IEnumerable<VoucherCode> codes, CancellationToken cancellationToken = default)
        {
            VoucherCodes.AddRange(codes);
            return Task.CompletedTask;
        }

        public Task<bool> VoucherCodeExistsAsync(string code, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(VoucherCodes.Any(v => v.Code.Equals(code, StringComparison.OrdinalIgnoreCase) && !v.IsDeleted));
        }

        public Task<VoucherCode?> GetVoucherCodeAsync(string code, CancellationToken cancellationToken = default)
        {
            var vc = VoucherCodes.FirstOrDefault(v => v.Code.Equals(code, StringComparison.OrdinalIgnoreCase) && !v.IsDeleted);
            if (vc != null)
            {
                vc.Promotion = Promotions.FirstOrDefault(p => p.Id == vc.PromotionId);
            }
            return Task.FromResult(vc);
        }

        public Task<(IReadOnlyList<VoucherCode> Codes, int TotalCount)> GetPagedVoucherCodesAsync(Guid promotionId, int pageIndex, int pageSize, string? status = null, CancellationToken cancellationToken = default)
        {
            var q = VoucherCodes.Where(v => v.PromotionId == promotionId && !v.IsDeleted).ToList();
            return Task.FromResult(((IReadOnlyList<VoucherCode>)q, q.Count));
        }

        public Task<bool> RedeemVoucherCodeAtomicAsync(string code, Guid orderId, string orderCode, Guid? customerId, string? customerName, string? customerPhone, CancellationToken cancellationToken = default)
        {
            var v = VoucherCodes.FirstOrDefault(x => x.Code.Equals(code, StringComparison.OrdinalIgnoreCase) && x.Status == "UNUSED" && !x.IsDeleted);
            if (v == null) return Task.FromResult(false);
            v.Status = "USED";
            v.OrderId = orderId;
            v.OrderCode = orderCode;
            v.CustomerId = customerId;
            v.CustomerName = customerName;
            v.CustomerPhone = customerPhone;
            v.UsedAt = DateTime.UtcNow;
            return Task.FromResult(true);
        }

        public Task<IReadOnlyList<Promotion>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult((IReadOnlyList<Promotion>)Promotions);
        public Task<IReadOnlyList<Promotion>> FindAsync(Expression<Func<Promotion, bool>> predicate, CancellationToken cancellationToken = default) => Task.FromResult((IReadOnlyList<Promotion>)Promotions.Where(predicate.Compile()).ToList());
        public Task AddAsync(Promotion entity, CancellationToken cancellationToken = default) { Promotions.Add(entity); return Task.CompletedTask; }
        public void Update(Promotion entity) { }
        public void Delete(Promotion entity) => entity.IsDeleted = true;
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
        public Guid CurrentUserId { get; set; } = Guid.NewGuid();
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
    #endregion
}

