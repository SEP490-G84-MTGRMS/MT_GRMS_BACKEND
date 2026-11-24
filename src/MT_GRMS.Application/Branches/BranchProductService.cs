using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MT_GRMS.Application.Branches.Dtos;
using MT_GRMS.Application.Branches.Validators;
using MT_GRMS.Application.Catalog;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Inventory;
using MT_GRMS.Application.Suppliers;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Domain.Entities.Supplier;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Domain.Exceptions;

namespace MT_GRMS.Application.Branches;

public class BranchProductService : IBranchProductService
{
    private readonly IBranchProductRepository _repository;
    private readonly IProductPriceHistoryRepository? _priceHistoryRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITenantContext _tenantContext;

    public BranchProductService(
        IBranchProductRepository repository,
        IUnitOfWork unitOfWork,
        ITenantContext tenantContext)
        : this(repository, null, unitOfWork, tenantContext)
    {
    }

    public BranchProductService(
        IBranchProductRepository repository,
        IProductPriceHistoryRepository? priceHistoryRepository,
        IUnitOfWork unitOfWork,
        ITenantContext tenantContext)
    {
        _repository = repository;
        _priceHistoryRepository = priceHistoryRepository;
        _unitOfWork = unitOfWork;
        _tenantContext = tenantContext;
    }

    public async Task<PagedResult<BranchProductDto>> GetBranchProductsAsync(
        Guid branchId,
        BranchProductQueryFilter filter,
        CancellationToken cancellationToken = default)
    {
        var effectiveBranchId = BranchScopeGuard.ResolveEffectiveBranchId(
            _tenantContext,
            branchId == Guid.Empty ? null : branchId) ?? Guid.Empty;

        if (effectiveBranchId != Guid.Empty)
        {
            var branch = await _repository.GetBranchByIdAsync(effectiveBranchId, cancellationToken);
            if (branch == null || branch.IsDeleted)
            {
                throw new NotFoundException("Chi nhánh", effectiveBranchId);
            }
        }

        var pageIndex = Math.Max(1, filter.PageIndex);
        var pageSize = Math.Clamp(filter.PageSize, 1, 1000);

        var (items, totalCount) = await _repository.GetPagedBranchProductsAsync(
            effectiveBranchId,
            filter,
            pageIndex,
            pageSize,
            cancellationToken);

        return new PagedResult<BranchProductDto>
        {
            Items = items,
            Page = pageIndex,
            PageSize = pageSize,
            TotalItems = totalCount
        };
    }

    public async Task<IReadOnlyList<BranchSummaryDto>> GetBranchSummariesAsync(
        CancellationToken cancellationToken = default)
    {
        var summaries = await _repository.GetBranchSummariesAsync(cancellationToken);
        if (BranchScopeGuard.IsBranchScopedEmployee(_tenantContext))
        {
            var assigned = BranchScopeGuard.ResolveRequiredBranchId(_tenantContext, Guid.Empty);
            return summaries.Where(s => s.Id == assigned).ToList();
        }
        return summaries;
    }

    public async Task<IReadOnlyList<AssignableBranchProductDto>> GetAssignableProductsAsync(
        Guid branchId,
        CancellationToken cancellationToken = default)
    {
        BranchScopeGuard.EnsureCanAccessBranch(_tenantContext, branchId);

        var branch = await _repository.GetBranchByIdAsync(branchId, cancellationToken);
        if (branch == null || branch.IsDeleted)
        {
            throw new NotFoundException("Chi nhánh", branchId);
        }

        return await _repository.GetAssignableProductsForBranchAsync(branchId, cancellationToken);
    }

    public async Task<IReadOnlyList<BranchProductDto>> AssignProductsAsync(
        Guid branchId,
        AssignBranchProductsCommand command,
        CancellationToken cancellationToken = default)
    {
        var validator = new AssignBranchProductsValidator();
        var validationResult = await validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
        {
            throw new BusinessException("VALIDATION_ERROR", validationResult.Errors.First().ErrorMessage);
        }

        var branch = await _repository.GetBranchByIdAsync(branchId, cancellationToken);
        if (branch == null || branch.IsDeleted)
        {
            throw new NotFoundException("Chi nhánh", branchId);
        }

        var linkedSupplierIds = await _repository.GetActiveSupplierIdsForBranchAsync(branchId, cancellationToken);
        if (linkedSupplierIds.Count == 0)
        {
            throw new BusinessException(
                "NO_SUPPLIER_LINKED",
                "Chi nhánh chưa liên kết nhà cung cấp Active. Hãy gán NCC cho cửa hàng trước khi thêm sản phẩm.");
        }

        var assignable = await _repository.GetAssignableProductsForBranchAsync(branchId, cancellationToken);
        var requestedItems = (command.Items is { Count: > 0 }
            ? command.Items
                .Where(i => i != null && i.ProductId != Guid.Empty)
                .Select(i => (ProductId: i.ProductId, SupplierId: i.SupplierId.HasValue && i.SupplierId.Value != Guid.Empty ? i.SupplierId : null))
            : (command.ProductIds ?? new List<Guid>())
                .Where(id => id != Guid.Empty)
                .Select(id => (ProductId: id, SupplierId: (Guid?)null)))
            .Distinct()
            .ToList();

        var assignedList = new List<BranchProductDto>();
        var createdCatalogs = new Dictionary<Guid, BranchProduct>();

        foreach (var (productId, requestedSupplierId) in requestedItems)
        {
            AssignableBranchProductDto? eligible;
            if (requestedSupplierId.HasValue)
            {
                eligible = assignable.FirstOrDefault(x =>
                    x.ProductId == productId && x.SupplierId == requestedSupplierId.Value);
            }
            else
            {
                eligible = assignable.FirstOrDefault(x => x.ProductId == productId && !x.IsAlreadyAssigned)
                    ?? assignable.FirstOrDefault(x => x.ProductId == productId);
            }

            if (!createdCatalogs.TryGetValue(productId, out var existing))
            {
                existing = await _repository.GetBranchProductAsync(branchId, productId, cancellationToken);
            }

            // Gọi legacy chỉ truyền ProductIds (không chỉ định SupplierId) và SP đã gán → bỏ qua (idempotent)
            if (existing != null && !requestedSupplierId.HasValue)
            {
                continue;
            }

            if (eligible == null)
            {
                var productName = (await _repository.GetProductByIdAsync(productId, cancellationToken))?.Name
                    ?? productId.ToString();
                throw new BusinessException(
                    "PRODUCT_NOT_SUPPLIED_TO_BRANCH",
                    $"Sản phẩm '{productName}' không thuộc nhà cung cấp đang cung cấp cho chi nhánh này.");
            }

            if (eligible.IsAlreadyAssigned && !createdCatalogs.ContainsKey(productId))
            {
                continue;
            }

            var product = await _repository.GetProductByIdAsync(productId, cancellationToken);
            if (product == null || product.IsDeleted)
            {
                continue;
            }

            if (product.Status != ProductStatus.Active)
            {
                throw new BusinessException(
                    "PRODUCT_INACTIVE",
                    $"Không thể gán sản phẩm '{product.Name}' đang ngưng kinh doanh vào chi nhánh.");
            }

            var sp = await _repository.GetSupplierProductAsync(productId, eligible.SupplierId, cancellationToken);
            var purchaseRate = ResolvePurchaseToStockRate(product, sp);
            var baseUnitCost = sp?.AgreedWholesalePrice is > 0m
                ? Math.Round(sp.AgreedWholesalePrice.Value / purchaseRate, 2)
                : (product.CostPrice ?? 0m);
            var effectiveCost = sp?.AgreedWholesalePrice is > 0m
                ? sp.AgreedWholesalePrice.Value
                : Math.Round((product.CostPrice ?? 0m) * purchaseRate, 2);
            var moq = sp != null && sp.MinimumOrderQuantity > 0 ? (int)sp.MinimumOrderQuantity : eligible.Moq;

            BranchProduct catalog;
            decimal effectiveRetail;

            if (existing != null)
            {
                // Bản ghi cũ chưa có AllowedSupplierIdsJson → bảo toàn các NCC đã gán hiện có trước khi thêm NCC mới
                if (string.IsNullOrWhiteSpace(existing.AllowedSupplierIdsJson))
                {
                    var currentIds = assignable
                        .Where(x => x.ProductId == productId && x.IsAlreadyAssigned && x.SupplierId != Guid.Empty)
                        .Select(x => x.SupplierId)
                        .Distinct()
                        .ToList();
                    if (currentIds.Count > 0)
                    {
                        existing.SetAllowedSupplierIds(currentIds);
                    }
                }

                if (!existing.AddAllowedSupplierId(eligible.SupplierId))
                {
                    continue;
                }

                existing.UpdatedAt = DateTime.UtcNow;
                catalog = existing;
                effectiveRetail = catalog.SellingPriceOverride ?? product.SellingPrice;
            }
            else
            {
                // Không copy cứng Master → SellingPriceOverride = null (hiệu lực = Master).
                // Chỉ ghi override khi client gửi DefaultRetailPrice > 0 và khác Master.
                decimal? sellingOverride = null;
                if (command.DefaultRetailPrice is > 0 && command.DefaultRetailPrice.Value != product.SellingPrice)
                {
                    sellingOverride = command.DefaultRetailPrice.Value;
                }

                effectiveRetail = sellingOverride ?? product.SellingPrice;

                catalog = new BranchProduct
                {
                    TenantId = _tenantContext.CurrentTenantId,
                    BranchId = branchId,
                    ProductId = productId,
                    SellingPriceOverride = sellingOverride,
                    Status = "Active",
                    ShelfLocation = command.ShelfLocation ?? "Kệ hàng"
                };
                if (eligible.SupplierId != Guid.Empty)
                {
                    catalog.SetAllowedSupplierIds(new[] { eligible.SupplierId });
                }

                await _repository.AddBranchProductAsync(catalog, cancellationToken);
                createdCatalogs[productId] = catalog;

                // Seed tồn 0 để GRN/FEFO có chỗ gắn lô (không thay catalog).
                var existingStocks = await _repository.GetBranchStocksByProductAsync(branchId, productId, cancellationToken);
                if (!existingStocks.Any())
                {
                    await _repository.AddBranchStockAsync(new BranchStock
                    {
                        TenantId = _tenantContext.CurrentTenantId,
                        BranchId = branchId,
                        ProductId = productId,
                        SupplierId = eligible.SupplierId,
                        QuantityOnHand = 0.000m,
                        QuantityReserved = 0.000m,
                        LandedCostPerUnit = baseUnitCost,
                        RetailPrice = effectiveRetail,
                        Status = "Active",
                        ShelfLocation = catalog.ShelfLocation,
                        ZoneArea = "Khu vực trưng bày"
                    }, cancellationToken);
                }
            }

            assignedList.Add(new BranchProductDto
            {
                Id = catalog.Id,
                BranchId = branch.Id,
                BranchCode = branch.BranchCode,
                BranchName = branch.Name,
                ProductId = product.Id,
                Name = product.Name,
                Sku = product.ProductCode,
                Barcode = product.Barcode ?? string.Empty,
                Category = product.Category?.Name ?? "Khác",
                Unit = product.BaseUnit,
                StockUnit = product.BaseUnit,
                PurchaseToStockRate = 1m,
                RetailPrice = effectiveRetail,
                CostPrice = effectiveCost,
                AverageCost = baseUnitCost,
                CurrentStock = 0,
                MinStockLevel = product.MinStock,
                SupplierId = eligible.SupplierId,
                PrimarySupplierCode = eligible.SupplierCode,
                PrimarySupplierName = eligible.SupplierName,
                MinOrderQuantity = moq,
                Status = catalog.Status,
                ShelfLocation = catalog.ShelfLocation,
                AssignedDate = DateTime.UtcNow
            });
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return assignedList;
    }

    public async Task<BranchProductDto> UpdateProductAsync(
        Guid branchId,
        Guid productId,
        UpdateBranchProductCommand command,
        CancellationToken cancellationToken = default)
    {
        var validator = new UpdateBranchProductValidator();
        var validationResult = await validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
        {
            throw new BusinessException("VALIDATION_ERROR", validationResult.Errors.First().ErrorMessage);
        }

        var branch = await _repository.GetBranchByIdAsync(branchId, cancellationToken);
        if (branch == null || branch.IsDeleted)
        {
            throw new NotFoundException("Chi nhánh", branchId);
        }

        var product = await _repository.GetProductByIdAsync(productId, cancellationToken);
        if (product == null || product.IsDeleted)
        {
            throw new NotFoundException("Sản phẩm", productId);
        }

        var catalog = await _repository.GetBranchProductAsync(branchId, productId, cancellationToken);
        if (catalog == null)
        {
            throw new NotFoundException($"Sản phẩm '{product.Name}' chưa được gán vào chi nhánh '{branch.Name}'.");
        }

        if (!string.IsNullOrWhiteSpace(command.Status)
            && string.Equals(command.Status, "Active", StringComparison.OrdinalIgnoreCase)
            && product.Status != ProductStatus.Active)
        {
            throw new BusinessException(
                "PRODUCT_INACTIVE",
                "Không thể kích hoạt sản phẩm tại chi nhánh khi hàng hóa đang ngưng kinh doanh.");
        }

        var productForUnits = catalog.Product ?? product;
        var oldUnitPrices = (productForUnits.Units ?? [])
            .Where(u => !u.IsDeleted)
            .ToDictionary(
                u => u.Id,
                u => catalog.UnitPriceOverrides?.FirstOrDefault(o => !o.IsDeleted && o.ProductUnitId == u.Id)?.SellingPriceOverride ?? u.Price);
        await ApplyUnitPriceOverridesAsync(catalog, productForUnits, command.UnitPrices, cancellationToken);

        var oldEffective = catalog.SellingPriceOverride ?? product.SellingPrice;

        var retailToPersist = ResolveBranchRetailPrice(command, product.SellingPrice);
        if (retailToPersist > 0)
        {
            // Giá ĐVT cơ sở: khác Master → override; trùng Master → NULL (dùng Master).
            catalog.SellingPriceOverride = retailToPersist == product.SellingPrice ? null : retailToPersist;
        }

        if (!string.IsNullOrWhiteSpace(command.Status))
        {
            catalog.Status = string.Equals(command.Status, "Active", StringComparison.OrdinalIgnoreCase) ? "Active" : "Inactive";
        }

        if (!string.IsNullOrWhiteSpace(command.ShelfLocation))
        {
            catalog.ShelfLocation = command.ShelfLocation.Trim();
        }

        // Giá đóng khay (đ/kg): chỉnh từ popup CH → cập nhật Master (AllowRepack).
        if (product.AllowRepack && command.ProcessedSellingPricePerKg.HasValue)
        {
            var processed = command.ProcessedSellingPricePerKg.Value;
            if (processed <= 0)
            {
                throw new BusinessException(
                    "VALIDATION_ERROR",
                    "Giá khi đóng khay (đ/kg) phải lớn hơn 0.");
            }

            if (processed > 1_000_000_000m)
            {
                throw new BusinessException(
                    "VALIDATION_ERROR",
                    "Giá khi đóng khay không được vượt quá 1.000.000.000 đ.");
            }

            var oldProcessed = product.ProcessedSellingPricePerKg;
            product.ProcessedSellingPricePerKg = processed;
            product.UpdatedAt = DateTime.UtcNow;

            if (_priceHistoryRepository != null
                && oldProcessed.GetValueOrDefault() != processed)
            {
                var costSnapshot = product.CostPrice;
                await _priceHistoryRepository.AddAsync(new ProductPriceHistory
                {
                    TenantId = _tenantContext.CurrentTenantId,
                    ProductId = product.Id,
                    BranchId = branchId,
                    PriceScope = "BRANCH",
                    PriceType = "PROCESSED_SELLING_PRICE",
                    OldPrice = oldProcessed ?? 0m,
                    NewPrice = processed,
                    CostPriceSnapshot = costSnapshot,
                    IsBelowCost = costSnapshot.HasValue && processed < costSnapshot.Value,
                    ChangedByUserId = _tenantContext.CurrentUserId != Guid.Empty
                        ? _tenantContext.CurrentUserId.ToString()
                        : "SYSTEM",
                    ChangedByUserName = !string.IsNullOrEmpty(_tenantContext.CurrentRole)
                        ? $"{_tenantContext.CurrentRole} ({branch.Name})"
                        : branch.Name,
                    Reason = "Điều chỉnh giá đóng khay (đ/kg) từ chi nhánh",
                    CreatedAt = DateTime.UtcNow
                }, cancellationToken);
            }
        }

        catalog.UpdatedAt = DateTime.UtcNow;

        var effectiveRetail = catalog.SellingPriceOverride ?? product.SellingPrice;

        if (oldEffective != effectiveRetail)
        {
            var costSnapshot = product.CostPrice;
            var isBelowCost = costSnapshot.HasValue && effectiveRetail < costSnapshot.Value;
            var history = new ProductPriceHistory
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.CurrentTenantId,
                ProductId = product.Id,
                ProductUnitId = productForUnits.Units?.FirstOrDefault(u => u.IsBaseUnit && !u.IsDeleted)?.Id,
                BranchId = branchId,
                PriceScope = "BRANCH",
                PriceType = "SELLING_PRICE",
                OldPrice = oldEffective,
                NewPrice = effectiveRetail,
                CostPriceSnapshot = costSnapshot,
                IsBelowCost = isBelowCost,
                ManagerApprovedBy = null,
                ManagerApprovalReason = null,
                ChangedByUserId = _tenantContext.CurrentUserId != Guid.Empty ? _tenantContext.CurrentUserId.ToString() : "SYSTEM",
                ChangedByUserName = !string.IsNullOrEmpty(_tenantContext.CurrentRole) ? $"{_tenantContext.CurrentRole} ({branch.Name})" : branch.Name,
                Reason = isBelowCost ? "Điều chỉnh giá chi nhánh (Dưới giá vốn)" : "Điều chỉnh giá chi nhánh",
                CreatedAt = DateTime.UtcNow
            };
            if (_priceHistoryRepository != null)
            {
                await _priceHistoryRepository.AddAsync(history, cancellationToken);
            }
        }

        if (_priceHistoryRepository != null && command.UnitPrices is { Count: > 0 })
        {
            foreach (var row in command.UnitPrices.Where(x => x.UnitId.HasValue && x.UnitId.Value != Guid.Empty && !x.IsBaseUnit))
            {
                var unit = productForUnits.Units?.FirstOrDefault(u => !u.IsDeleted && u.Id == row.UnitId!.Value);
                if (unit == null || !oldUnitPrices.TryGetValue(unit.Id, out var oldUnitPrice) || oldUnitPrice == row.Price)
                    continue;

                var unitCost = product.CostPrice.HasValue ? product.CostPrice.Value * unit.ConversionRate : (decimal?)null;
                await _priceHistoryRepository.AddAsync(new ProductPriceHistory
                {
                    TenantId = _tenantContext.CurrentTenantId,
                    ProductId = product.Id,
                    BranchId = branchId,
                    ProductUnitId = unit.Id,
                    PriceScope = "BRANCH",
                    PriceType = "SELLING_PRICE",
                    OldPrice = oldUnitPrice,
                    NewPrice = row.Price,
                    CostPriceSnapshot = unitCost,
                    IsBelowCost = unitCost.HasValue && row.Price < unitCost.Value,
                    ChangedByUserId = _tenantContext.CurrentUserId != Guid.Empty ? _tenantContext.CurrentUserId.ToString() : "SYSTEM",
                    ChangedByUserName = !string.IsNullOrEmpty(_tenantContext.CurrentRole) ? $"{_tenantContext.CurrentRole} ({branch.Name})" : branch.Name,
                    Reason = $"Cập nhật giá đơn vị {unit.UnitName} tại chi nhánh"
                }, cancellationToken);
            }
        }

        // Lấy snapshot để dựng DTO. Không update các entity BranchStock đang tracked:
        // RowVersion của chúng thuộc luồng biến động tồn kho và có thể đổi do bán/nhập
        // hàng song song với thao tác sửa giá.
        var stocks = await _repository.GetBranchStocksByProductAsync(branchId, productId, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _repository.SyncBranchStockCatalogAsync(
            branchId,
            productId,
            effectiveRetail,
            catalog.Status,
            catalog.ShelfLocation,
            cancellationToken);

        return await BuildDtoAsync(branch, product, catalog, stocks, cancellationToken);
    }

    public async Task<BranchProductDto> ToggleStatusAsync(
        Guid branchId,
        Guid productId,
        string status,
        CancellationToken cancellationToken = default)
    {
        var branch = await _repository.GetBranchByIdAsync(branchId, cancellationToken);
        if (branch == null || branch.IsDeleted)
        {
            throw new NotFoundException("Chi nhánh", branchId);
        }

        var product = await _repository.GetProductByIdAsync(productId, cancellationToken);
        if (product == null || product.IsDeleted)
        {
            throw new NotFoundException("Sản phẩm", productId);
        }

        var catalog = await _repository.GetBranchProductAsync(branchId, productId, cancellationToken);
        if (catalog == null)
        {
            throw new NotFoundException($"Sản phẩm '{product.Name}' chưa được gán vào chi nhánh '{branch.Name}'.");
        }

        if (string.IsNullOrWhiteSpace(status)
            || (!string.Equals(status.Trim(), "Active", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(status.Trim(), "Inactive", StringComparison.OrdinalIgnoreCase)))
        {
            throw new BusinessException("VALIDATION_ERROR", "Trạng thái kinh doanh chỉ có thể là 'Active' hoặc 'Inactive'.");
        }

        var normalizedStatus = string.Equals(status.Trim(), "Active", StringComparison.OrdinalIgnoreCase) ? "Active" : "Inactive";
        if (normalizedStatus == "Active"
            && product.Status != ProductStatus.Active)
        {
            throw new BusinessException(
                "PRODUCT_INACTIVE",
                "Không thể kích hoạt sản phẩm tại chi nhánh khi hàng hóa đang ngưng kinh doanh.");
        }

        catalog.Status = normalizedStatus;
        catalog.UpdatedAt = DateTime.UtcNow;

        var stocks = await _repository.GetBranchStocksByProductAsync(branchId, productId, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _repository.SyncBranchStockCatalogAsync(
            branchId,
            productId,
            catalog.SellingPriceOverride ?? product.SellingPrice,
            normalizedStatus,
            catalog.ShelfLocation,
            cancellationToken);

        return await BuildDtoAsync(branch, product, catalog, stocks, cancellationToken);
    }

    public async Task RemoveProductAsync(
        Guid branchId,
        Guid productId,
        CancellationToken cancellationToken = default)
    {
        var branch = await _repository.GetBranchByIdAsync(branchId, cancellationToken);
        if (branch == null || branch.IsDeleted)
        {
            throw new NotFoundException("Chi nhánh", branchId);
        }

        var product = await _repository.GetProductByIdAsync(productId, cancellationToken);
        if (product == null || product.IsDeleted)
        {
            throw new NotFoundException("Sản phẩm", productId);
        }

        var catalog = await _repository.GetBranchProductAsync(branchId, productId, cancellationToken);
        var stocks = await _repository.GetBranchStocksByProductAsync(branchId, productId, cancellationToken);

        if (catalog == null && !stocks.Any())
        {
            return;
        }

        var totalCurrentStock = stocks.Sum(s => s.QuantityOnHand);
        if (totalCurrentStock > 0)
        {
            throw new BusinessException(
                "CANNOT_REMOVE_PRODUCT_WITH_STOCK",
                $"Không thể gỡ sản phẩm '{product.Name}' khỏi chi nhánh '{branch.Name}' khi tồn kho thực tế vẫn còn ({totalCurrentStock} {product.BaseUnit}). Vui lòng điều chuyển hoặc xuất kho hết trước.");
        }

        var totalReserved = stocks.Sum(s => s.QuantityReserved);
        if (totalReserved > 0)
        {
            throw new BusinessException(
                "CANNOT_REMOVE_PRODUCT_WITH_RESERVED_STOCK",
                $"Không thể gỡ sản phẩm '{product.Name}' khỏi chi nhánh '{branch.Name}' khi đang có {totalReserved} {product.BaseUnit} tạm giữ trong các đơn hàng chờ xuất.");
        }

        if (catalog != null)
        {
            _repository.RemoveBranchProduct(catalog);
        }

        if (stocks.Any())
        {
            _repository.RemoveBranchStocks(stocks);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task<BranchProductDto> BuildDtoAsync(
        Domain.Entities.SaaS.Branch branch,
        Product product,
        BranchProduct catalog,
        IReadOnlyList<BranchStock> stocks,
        CancellationToken cancellationToken)
    {
        var uniqueLotSupplierIds = stocks
            .Where(s => s.SupplierId.HasValue)
            .Select(s => s.SupplierId!.Value)
            .Distinct()
            .ToList();
        Guid? displaySupplierId = uniqueLotSupplierIds.Count == 1 ? uniqueLotSupplierIds[0] : null;
        var activeSupplierIds = await _repository.GetActiveSupplierIdsForBranchAsync(branch.Id, cancellationToken);
        var allowedSupplierIds = catalog.GetAllowedSupplierIds();
        var eligibleSupplierIds = allowedSupplierIds != null
            ? activeSupplierIds.Where(id => allowedSupplierIds.Contains(id)).ToList()
            : activeSupplierIds.ToList();
        SupplierProduct? sp = null;
        var hasSource = false;
        if (displaySupplierId.HasValue
            && displaySupplierId.Value != Guid.Empty
            && (allowedSupplierIds == null || allowedSupplierIds.Contains(displaySupplierId.Value)))
        {
            sp = await _repository.GetSupplierProductAsync(product.Id, displaySupplierId, cancellationToken);
            hasSource = sp != null && sp.Status == SupplierProductStatus.Active;
        }
        else
        {
            foreach (var sid in eligibleSupplierIds)
            {
                var candidate = await _repository.GetSupplierProductAsync(product.Id, sid, cancellationToken);
                if (candidate == null || candidate.Status != SupplierProductStatus.Active)
                {
                    continue;
                }

                hasSource = true;
                sp ??= candidate;
            }
        }

        var purchaseRate = ResolvePurchaseToStockRate(product, sp);
        var baseUnitFallbackCost = sp?.AgreedWholesalePrice is > 0m
            ? Math.Round(sp.AgreedWholesalePrice.Value / purchaseRate, 2)
            : (product.CostPrice ?? 0m);
        var costPrice = sp?.AgreedWholesalePrice is > 0m
            ? sp.AgreedWholesalePrice.Value
            : Math.Round((product.CostPrice ?? 0m) * purchaseRate, 2);
        var moq = sp != null && sp.MinimumOrderQuantity > 0
            ? (int)sp.MinimumOrderQuantity
            : 0;

        var effectiveRetail = catalog.SellingPriceOverride ?? product.SellingPrice;
        var stockUnit = string.IsNullOrWhiteSpace(product.BaseUnit) ? "Cái" : product.BaseUnit.Trim();
        var purchaseUnitName = product.Units?.FirstOrDefault(u => !u.IsDeleted && u.IsPurchaseUnit)?.UnitName?.Trim()
            ?? (!string.IsNullOrWhiteSpace(sp?.DeliveryUoM) ? sp!.DeliveryUoM.Trim() : stockUnit);
        var sellable = stocks.Sum(s => BranchStockAvailability.GetSellableAvailable(s));
        var physicalQty = stocks.Sum(s => s.QuantityOnHand);
        var stockAvgCost = physicalQty > 0m
            ? Math.Round(stocks.Sum(s => s.QuantityOnHand * s.LandedCostPerUnit) / physicalQty, 2)
            : 0m;
        var averageCost = stockAvgCost > 0m ? stockAvgCost : baseUnitFallbackCost;

        return new BranchProductDto
        {
            Id = catalog.Id,
            BranchId = branch.Id,
            BranchCode = branch.BranchCode,
            BranchName = branch.Name,
            ProductId = product.Id,
            Name = product.Name,
            Sku = product.ProductCode,
            Barcode = product.Barcode ?? string.Empty,
            Category = product.Category?.Name ?? "Khác",
            Unit = purchaseUnitName,
            StockUnit = stockUnit,
            PurchaseToStockRate = purchaseRate,
            RetailPrice = effectiveRetail,
            CostPrice = costPrice,
            AverageCost = averageCost,
            CurrentStock = sellable,
            MinStockLevel = product.MinStock,
            SupplierId = displaySupplierId ?? sp?.SupplierId,
            HasActiveSupplierSource = hasSource,
            LostSupplierSource = !hasSource,
            PrimarySupplierCode = stocks.FirstOrDefault()?.Supplier?.Code ?? sp?.Supplier?.Code ?? string.Empty,
            PrimarySupplierName = stocks.FirstOrDefault()?.Supplier?.Name ?? sp?.Supplier?.Name ?? string.Empty,
            MinOrderQuantity = moq,
            Status = catalog.Status,
            ShelfLocation = catalog.ShelfLocation,
            AssignedDate = catalog.CreatedAt,
            AllowRepack = product.AllowRepack,
            ProcessedSellingPricePerKg = product.ProcessedSellingPricePerKg
        };
    }

    private static decimal ResolvePurchaseToStockRate(Product product, SupplierProduct? sp)
    {
        var activeUnits = product.Units?.Where(u => !u.IsDeleted).ToList();
        if (activeUnits is { Count: > 0 })
        {
            if (!string.IsNullOrWhiteSpace(sp?.DeliveryUoM))
            {
                var matched = activeUnits.FirstOrDefault(u =>
                    string.Equals(u.UnitName?.Trim(), sp!.DeliveryUoM.Trim(), StringComparison.OrdinalIgnoreCase));
                if (matched != null && matched.ConversionRate > 0m)
                {
                    return matched.ConversionRate;
                }
            }

            var purchaseUnit = activeUnits.FirstOrDefault(u => u.IsPurchaseUnit);
            if (purchaseUnit != null && purchaseUnit.ConversionRate > 0m)
            {
                return purchaseUnit.ConversionRate;
            }
        }

        return sp != null && sp.ConversionRate > 0m ? sp.ConversionRate : 1m;
    }

    /// <summary>
    /// Upsert override từng ĐVT: khác Master → lưu; trùng Master → xóa override.
    /// </summary>
    private async Task ApplyUnitPriceOverridesAsync(
        BranchProduct catalog,
        Product product,
        List<BranchProductUnitPriceDto>? unitPrices,
        CancellationToken cancellationToken)
    {
        if (unitPrices is not { Count: > 0 })
        {
            return;
        }

        var masterById = (product.Units ?? [])
            .Where(u => !u.IsDeleted)
            .ToDictionary(u => u.Id, u => u);

        var existing = catalog.UnitPriceOverrides?
            .Where(o => !o.IsDeleted)
            .ToDictionary(o => o.ProductUnitId)
            ?? new Dictionary<Guid, BranchProductUnitPrice>();

        foreach (var row in unitPrices)
        {
            if (row.Price <= 0 || row.Price > 1_000_000_000m)
            {
                throw new BusinessException(
                    "VALIDATION_ERROR",
                    $"Giá bán theo đơn vị '{row.UnitName}' phải lớn hơn 0 và không vượt quá 1.000.000.000 VNĐ.");
            }

            if (!row.UnitId.HasValue || row.UnitId.Value == Guid.Empty)
            {
                continue;
            }

            var unitId = row.UnitId.Value;
            if (!masterById.TryGetValue(unitId, out var masterUnit))
            {
                continue;
            }

            var matchesMaster = row.Price == masterUnit.Price;
            existing.TryGetValue(unitId, out var current);

            if (matchesMaster)
            {
                if (current != null)
                {
                    current.IsDeleted = true;
                    current.UpdatedAt = DateTime.UtcNow;
                }

                continue;
            }

            if (current != null)
            {
                current.SellingPriceOverride = row.Price;
                current.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                await _repository.AddUnitPriceOverrideAsync(new BranchProductUnitPrice
                {
                    TenantId = catalog.TenantId,
                    BranchProductId = catalog.Id,
                    ProductUnitId = unitId,
                    SellingPriceOverride = row.Price
                }, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Giá ĐVT cơ sở: ưu tiên dòng base trong UnitPrices, không thì RetailPrice.
    /// </summary>
    private static decimal ResolveBranchRetailPrice(UpdateBranchProductCommand command, decimal masterSellingPrice)
    {
        if (command.UnitPrices is { Count: > 0 })
        {
            var baseRow = command.UnitPrices.FirstOrDefault(u => u.IsBaseUnit)
                ?? command.UnitPrices.OrderBy(u => u.ConversionRate <= 0 ? 1m : u.ConversionRate).First();
            if (baseRow.Price > 0)
            {
                return baseRow.Price;
            }
        }

        if (command.RetailPrice > 0)
        {
            return command.RetailPrice;
        }

        return masterSellingPrice > 0 ? masterSellingPrice : 0m;
    }
}
