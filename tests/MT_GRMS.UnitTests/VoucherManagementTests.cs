using System.Linq.Expressions;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Promotions;
using MT_GRMS.Application.Promotions.Dtos;
using MT_GRMS.Domain.Entities.Sales;
using Xunit;

namespace MT_GRMS.UnitTests;

public class VoucherManagementTests
{
    private readonly FakePromotionRepository _repo;
    private readonly FakeUnitOfWork _uow;
    private readonly FakeTenantContext _tenantContext;
    private readonly PromotionService _service;

    public VoucherManagementTests()
    {
        _repo = new FakePromotionRepository();
        _uow = new FakeUnitOfWork();
        _tenantContext = new FakeTenantContext("tenant-test-1");
        _service = new PromotionService(_repo, _uow, _tenantContext);

        // Seed a voucher batch
        var batchId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var batch = new Promotion
        {
            Id = batchId,
            TenantId = "tenant-test-1",
            PromotionCode = "VOUCHER-TET26",
            PromotionName = "Voucher Đón Tết 2026",
            Type = "VOUCHER",
            DiscountValue = 50000,
            MinOrderValue = 200000,
            UsageLimit = 100,
            UsedCount = 1,
            StartDate = DateTime.UtcNow.AddDays(-10),
            EndDate = DateTime.UtcNow.AddDays(20),
            IsActive = true,
            VoucherPrefix = "TET26",
            TotalVoucherQuantity = 5
        };
        _repo.Promotions.Add(batch);

        // Seed sample voucher codes
        _repo.VoucherCodes.AddRange(new[]
        {
            new VoucherCode
            {
                Id = Guid.NewGuid(),
                TenantId = "tenant-test-1",
                PromotionId = batchId,
                Code = "TET26-ABC123",
                Status = "UNUSED"
            },
            new VoucherCode
            {
                Id = Guid.NewGuid(),
                TenantId = "tenant-test-1",
                PromotionId = batchId,
                Code = "TET26-XYZ999",
                Status = "USED",
                OrderCode = "HD-00100",
                UsedAt = DateTime.UtcNow.AddHours(-3)
            }
        });
    }

    [Fact]
    public async Task REQ_PV04_CreateVoucherBatch_AutoGeneratesCodesWithPrefix()
    {
        // Arrange
        var request = new VoucherBatchCreateRequest(
            BatchCode: "BATCH-TEST",
            Name: "Đợt Voucher Test",
            DiscountType: "FIXED_AMOUNT",
            DiscountValue: 20000,
            MinOrderValue: 100000,
            MaxDiscountAmount: null,
            TotalQuantity: 10,
            StartDate: DateTime.UtcNow,
            EndDate: DateTime.UtcNow.AddDays(30),
            Prefix: "TEST20K",
            Description: "Sinh mã test"
        );

        // Act
        var result = await _service.CreateVoucherBatchAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("BATCH-TEST", result.BatchCode);
        Assert.Equal(10, result.TotalQuantity);

        // Kiểm tra đúng 10 mã đã được sinh
        var codes = _repo.VoucherCodes.Where(v => v.PromotionId == result.Id).ToList();
        Assert.Equal(10, codes.Count);
        Assert.All(codes, c => Assert.StartsWith("TEST20K-", c.Code));
        Assert.All(codes, c => Assert.Equal("UNUSED", c.Status));
        // Kiểm tra tính độc nhất
        Assert.Equal(10, codes.Select(c => c.Code).Distinct().Count());
    }

    [Fact]
    public async Task REQ_PV05_ValidateVoucher_ValidCodeAndOrder_ReturnsValidDiscount()
    {
        // Arrange
        var request = new ValidateVoucherRequest(
            Code: "TET26-ABC123",
            CartTotal: 300000
        );

        // Act
        var result = await _service.ValidateVoucherAsync(request);

        // Assert
        Assert.True(result.IsValid);
        Assert.Equal(50000, result.DiscountAmount);
        Assert.Equal("TET26-ABC123", result.Code);
    }

    [Fact]
    public async Task REQ_PV05_ValidateVoucher_OrderBelowMinOrderValue_ReturnsInvalid()
    {
        // Arrange: Đơn hàng chỉ 150k trong khi minOrderValue là 200k
        var request = new ValidateVoucherRequest(
            Code: "TET26-ABC123",
            CartTotal: 150000
        );

        // Act
        var result = await _service.ValidateVoucherAsync(request);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal(0, result.DiscountAmount);
        Assert.Contains("tối thiểu", result.Message);
    }

    [Fact]
    public async Task REQ_PV05_ValidateVoucher_AlreadyUsedCode_ReturnsInvalid()
    {
        // Arrange
        var request = new ValidateVoucherRequest(
            Code: "TET26-XYZ999", // Đã USED
            CartTotal: 500000
        );

        // Act
        var result = await _service.ValidateVoucherAsync(request);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("USED", result.Message);
    }

    [Fact]
    public async Task REQ_PV05_RedeemVoucher_ValidCode_ChangesStatusToUsed()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var request = new RedeemVoucherRequest(
            Code: "TET26-ABC123",
            OrderId: orderId,
            OrderCode: "HD-99999",
            CustomerId: Guid.NewGuid(),
            CustomerName: "Trần Văn B",
            CustomerPhone: "0912345678"
        );

        // Act
        var result = await _service.RedeemVoucherAsync(request);

        // Assert
        Assert.True(result.Success);

        // Kiểm tra trong repo trạng thái đã chuyển sang USED
        var code = _repo.VoucherCodes.First(v => v.Code == "TET26-ABC123");
        Assert.Equal("USED", code.Status);
        Assert.Equal("HD-99999", code.OrderCode);
        Assert.NotNull(code.UsedAt);
    }

    [Fact]
    public async Task REQ_PV05_RedeemVoucher_AlreadyUsedCode_ReturnsFailure()
    {
        // Arrange
        var request = new RedeemVoucherRequest(
            Code: "TET26-XYZ999", // Đã USED từ trước
            OrderId: Guid.NewGuid(),
            OrderCode: "HD-88888"
        );

        // Act
        var result = await _service.RedeemVoucherAsync(request);

        // Assert
        Assert.False(result.Success);
    }

    #region Fakes
    private class FakePromotionRepository : IPromotionRepository
    {
        public List<Promotion> Promotions { get; } = new();
        public List<VoucherCode> VoucherCodes { get; } = new();

        public Task<Promotion?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(Promotions.FirstOrDefault(p => p.Id == id && !p.IsDeleted));

        public Task<Promotion?> GetByIdWithConditionsAsync(Guid id, CancellationToken cancellationToken = default)
            => GetByIdAsync(id, cancellationToken);

        public Task<bool> ExistsByCodeAsync(string code, CancellationToken cancellationToken = default)
            => Task.FromResult(Promotions.Any(p => p.PromotionCode.Equals(code, StringComparison.OrdinalIgnoreCase) && !p.IsDeleted));

        public Task<(IReadOnlyList<Promotion> Items, int TotalCount)> GetPagedListAsync(PromotionFilterQuery query, CancellationToken cancellationToken = default)
            => Task.FromResult(((IReadOnlyList<Promotion>)Promotions, Promotions.Count));

        public Task<IReadOnlyList<Promotion>> GetActivePromotionsAsync(DateTime atUtc, string? branchId = null, CancellationToken cancellationToken = default)
            => Task.FromResult((IReadOnlyList<Promotion>)Promotions.Where(p => p.IsActive && p.StartDate <= atUtc && p.EndDate >= atUtc).ToList());

        public Task ReplaceConditionsAsync(Guid promotionId, IEnumerable<PromotionCondition> conditions, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

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
            => Task.FromResult(VoucherCodes.Any(v => v.Code.Equals(code, StringComparison.OrdinalIgnoreCase) && !v.IsDeleted));

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
        public void SetContext(string tenantId, Guid userId, Guid? branchId) { }
        public void SetRole(string? role) { }
        public FakeTenantContext(string tenantId) => CurrentTenantId = tenantId;
    }
    #endregion
}

