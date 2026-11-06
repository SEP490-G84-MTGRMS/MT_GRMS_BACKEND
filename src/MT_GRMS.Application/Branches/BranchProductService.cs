using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MT_GRMS.Application.Branches.Dtos;
using MT_GRMS.Application.Branches.Validators;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Suppliers;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Domain.Exceptions;

namespace MT_GRMS.Application.Branches;

public class BranchProductService : IBranchProductService
{
    private readonly IBranchProductRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITenantContext _tenantContext;

    public BranchProductService(
        IBranchProductRepository repository,
        IUnitOfWork unitOfWork,
        ITenantContext tenantContext)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _tenantContext = tenantContext;
    }

    public async Task<PagedResult<BranchProductDto>> GetBranchProductsAsync(
        Guid branchId,
        BranchProductQueryFilter filter,
        CancellationToken cancellationToken = default)
    {
        if (branchId != Guid.Empty)
        {
            var branch = await _repository.GetBranchByIdAsync(branchId, cancellationToken);
            if (branch == null || branch.IsDeleted)
            {
                throw new NotFoundException("Chi nhánh", branchId);
            }
        }

        var pageIndex = Math.Max(1, filter.PageIndex);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);

        var (items, totalCount) = await _repository.GetPagedBranchProductsAsync(
            branchId,
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
        return await _repository.GetBranchSummariesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AssignableBranchProductDto>> GetAssignableProductsAsync(
        Guid branchId,
        CancellationToken cancellationToken = default)
    {
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
        var assignableByProduct = assignable
            .GroupBy(x => x.ProductId)
            .ToDictionary(g => g.Key, g => g.First());

        var assignedList = new List<BranchProductDto>();

        foreach (var productId in command.ProductIds.Distinct())
        {
            var existing = await _repository.GetBranchProductAsync(branchId, productId, cancellationToken);
            if (existing != null)
            {
                continue;
            }

            if (!assignableByProduct.TryGetValue(productId, out var eligible))
            {
                var productName = (await _repository.GetProductByIdAsync(productId, cancellationToken))?.Name
                    ?? productId.ToString();
                throw new BusinessException(
                    "PRODUCT_NOT_SUPPLIED_TO_BRANCH",
                    $"Sản phẩm '{productName}' không thuộc nhà cung cấp đang cung cấp cho chi nhánh này.");
            }

            if (eligible.IsAlreadyAssigned)
            {
                continue;
            }

            var product = await _repository.GetProductByIdAsync(productId, cancellationToken);
            if (product == null || product.IsDeleted)
            {
                continue;
            }

            if (!string.Equals(product.Status, "Active", StringComparison.OrdinalIgnoreCase))
            {
                throw new BusinessException(
                    "PRODUCT_INACTIVE",
                    $"Không thể gán sản phẩm '{product.Name}' đang ngưng kinh doanh vào chi nhánh.");
            }

            var sp = await _repository.GetSupplierProductAsync(productId, eligible.SupplierId, cancellationToken);
            var effectiveCost = SupplierProductPricing.GetEffectivePurchasePriceOrZero(
                sp?.AgreedWholesalePrice ?? 0m,
                product.CostPrice);
            var moq = sp != null && sp.MinimumOrderQuantity > 0 ? (int)sp.MinimumOrderQuantity : eligible.Moq;

            // Không copy cứng Master → SellingPriceOverride = null (hiệu lực = Master).
            // Chỉ ghi override khi client gửi DefaultRetailPrice > 0 và khác Master.
            decimal? sellingOverride = null;
            if (command.DefaultRetailPrice is > 0 && command.DefaultRetailPrice.Value != product.SellingPrice)
            {
                sellingOverride = command.DefaultRetailPrice.Value;
            }

            var effectiveRetail = sellingOverride ?? product.SellingPrice;

            var catalog = new BranchProduct
            {
                TenantId = _tenantContext.CurrentTenantId,
                BranchId = branchId,
                ProductId = productId,
                SellingPriceOverride = sellingOverride,
                Status = "Active",
                ShelfLocation = command.ShelfLocation ?? "Kệ hàng"
            };
            await _repository.AddBranchProductAsync(catalog, cancellationToken);

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
                    LandedCostPerUnit = effectiveCost,
                    RetailPrice = effectiveRetail,
                    Status = "Active",
                    ShelfLocation = catalog.ShelfLocation,
                    ZoneArea = "Khu vực trưng bày"
                }, cancellationToken);
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
                Barcode = product.Barcode,
                Category = product.Category?.Name ?? "Khác",
                Unit = product.BaseUnit,
                RetailPrice = effectiveRetail,
                CostPrice = effectiveCost,
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
            && !string.Equals(product.Status, "Active", StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessException(
                "PRODUCT_INACTIVE",
                "Không thể kích hoạt sản phẩm tại chi nhánh khi hàng hóa đang ngưng kinh doanh.");
        }

        var productForUnits = catalog.Product ?? product;
        ApplyUnitPriceOverrides(catalog, productForUnits, command.UnitPrices);

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

        catalog.UpdatedAt = DateTime.UtcNow;

        var effectiveRetail = catalog.SellingPriceOverride ?? product.SellingPrice;

        // Đồng bộ RetailPrice trên các lô tồn (nếu có) để POS đọc nhất quán.
        var stocks = await _repository.GetBranchStocksByProductAsync(branchId, productId, cancellationToken);
        foreach (var stock in stocks)
        {
            stock.RetailPrice = effectiveRetail;
            stock.Status = catalog.Status;
            if (!string.IsNullOrWhiteSpace(catalog.ShelfLocation))
            {
                stock.ShelfLocation = catalog.ShelfLocation;
            }
            stock.UpdatedAt = DateTime.UtcNow;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

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

        var normalizedStatus = string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase) ? "Active" : "Inactive";
        if (normalizedStatus == "Active"
            && !string.Equals(product.Status, "Active", StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessException(
                "PRODUCT_INACTIVE",
                "Không thể kích hoạt sản phẩm tại chi nhánh khi hàng hóa đang ngưng kinh doanh.");
        }

        catalog.Status = normalizedStatus;
        catalog.UpdatedAt = DateTime.UtcNow;

        var stocks = await _repository.GetBranchStocksByProductAsync(branchId, productId, cancellationToken);
        foreach (var stock in stocks)
        {
            stock.Status = normalizedStatus;
            stock.UpdatedAt = DateTime.UtcNow;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

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
        var sp = await _repository.GetSupplierProductAsync(product.Id, displaySupplierId, cancellationToken);
        var costPrice = SupplierProductPricing.GetEffectivePurchasePriceOrZero(
            sp?.AgreedWholesalePrice ?? 0m,
            product.CostPrice);
        var moq = sp != null && sp.MinimumOrderQuantity > 0
            ? (int)sp.MinimumOrderQuantity
            : 0;

        var effectiveRetail = catalog.SellingPriceOverride ?? product.SellingPrice;

        return new BranchProductDto
        {
            Id = catalog.Id,
            BranchId = branch.Id,
            BranchCode = branch.BranchCode,
            BranchName = branch.Name,
            ProductId = product.Id,
            Name = product.Name,
            Sku = product.ProductCode,
            Barcode = product.Barcode,
            Category = product.Category?.Name ?? "Khác",
            Unit = product.BaseUnit,
            RetailPrice = effectiveRetail,
            CostPrice = costPrice,
            CurrentStock = stocks.Sum(s => s.QuantityOnHand),
            MinStockLevel = product.MinStock,
            SupplierId = displaySupplierId ?? sp?.SupplierId,
            PrimarySupplierCode = stocks.FirstOrDefault()?.Supplier?.Code ?? sp?.Supplier?.Code ?? string.Empty,
            PrimarySupplierName = stocks.FirstOrDefault()?.Supplier?.Name ?? sp?.Supplier?.Name ?? string.Empty,
            MinOrderQuantity = moq,
            Status = catalog.Status,
            ShelfLocation = catalog.ShelfLocation,
            AssignedDate = catalog.CreatedAt
        };
    }

    /// <summary>
    /// Upsert override từng ĐVT: khác Master → lưu; trùng Master → xóa override.
    /// </summary>
    private void ApplyUnitPriceOverrides(
        BranchProduct catalog,
        Product product,
        List<BranchProductUnitPriceDto>? unitPrices)
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
            if (!row.UnitId.HasValue || row.UnitId.Value == Guid.Empty || row.Price < 0)
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
                catalog.UnitPriceOverrides.Add(new BranchProductUnitPrice
                {
                    TenantId = catalog.TenantId,
                    BranchProductId = catalog.Id,
                    ProductUnitId = unitId,
                    SellingPriceOverride = row.Price
                });
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
