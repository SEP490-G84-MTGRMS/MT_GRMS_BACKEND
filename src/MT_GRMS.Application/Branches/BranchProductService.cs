using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MT_GRMS.Application.Branches.Dtos;
using MT_GRMS.Application.Branches.Validators;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Suppliers;
using MT_GRMS.Domain.Entities.Inventory;
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
            var existingStocks = await _repository.GetBranchStocksByProductAsync(branchId, productId, cancellationToken);
            if (existingStocks.Any())
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

            var newStock = new BranchStock
            {
                TenantId = _tenantContext.CurrentTenantId,
                BranchId = branchId,
                ProductId = productId,
                SupplierId = eligible.SupplierId,
                QuantityOnHand = 0.000m,
                QuantityReserved = 0.000m,
                LandedCostPerUnit = effectiveCost,
                RetailPrice = product.SellingPrice,
                Status = "Active",
                ShelfLocation = command.ShelfLocation ?? "Kệ hàng",
                ZoneArea = "Khu vực trưng bày"
            };

            await _repository.AddBranchStockAsync(newStock, cancellationToken);

            assignedList.Add(new BranchProductDto
            {
                Id = newStock.Id,
                BranchId = branch.Id,
                BranchCode = branch.BranchCode,
                BranchName = branch.Name,
                ProductId = product.Id,
                Name = product.Name,
                Sku = product.ProductCode,
                Barcode = product.Barcode,
                Category = product.Category?.Name ?? "Khác",
                Unit = product.BaseUnit,
                RetailPrice = newStock.RetailPrice ?? product.SellingPrice,
                CostPrice = effectiveCost,
                CurrentStock = 0,
                MinStockLevel = product.MinStock,
                SupplierId = eligible.SupplierId,
                PrimarySupplierCode = eligible.SupplierCode,
                PrimarySupplierName = eligible.SupplierName,
                MinOrderQuantity = moq,
                Status = newStock.Status,
                ShelfLocation = newStock.ShelfLocation,
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

        var stocks = await _repository.GetBranchStocksByProductAsync(branchId, productId, cancellationToken);
        if (!stocks.Any())
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

        // Chỉ cập nhật thông tin riêng của chi nhánh. Không ghi MOQ / giá nhập sang SupplierProducts.
        foreach (var stock in stocks)
        {
            stock.RetailPrice = command.RetailPrice;
            if (!string.IsNullOrWhiteSpace(command.Status))
            {
                stock.Status = string.Equals(command.Status, "Active", StringComparison.OrdinalIgnoreCase) ? "Active" : "Inactive";
            }
            if (!string.IsNullOrWhiteSpace(command.ShelfLocation))
            {
                stock.ShelfLocation = command.ShelfLocation.Trim();
            }
            stock.UpdatedAt = DateTime.UtcNow;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var firstStock = stocks.First();
        var uniqueLotSupplierIds = stocks
            .Where(s => s.SupplierId.HasValue)
            .Select(s => s.SupplierId!.Value)
            .Distinct()
            .ToList();
        Guid? displaySupplierId = uniqueLotSupplierIds.Count == 1 ? uniqueLotSupplierIds[0] : null;
        var spUpdated = await _repository.GetSupplierProductAsync(productId, displaySupplierId, cancellationToken);
        var costPrice = SupplierProductPricing.GetEffectivePurchasePriceOrZero(
            spUpdated?.AgreedWholesalePrice ?? 0m,
            product.CostPrice);
        var moq = spUpdated != null && spUpdated.MinimumOrderQuantity > 0
            ? (int)spUpdated.MinimumOrderQuantity
            : 0;

        var totalStock = stocks.Sum(s => s.QuantityOnHand);

        return new BranchProductDto
        {
            Id = firstStock.Id,
            BranchId = branch.Id,
            BranchCode = branch.BranchCode,
            BranchName = branch.Name,
            ProductId = product.Id,
            Name = product.Name,
            Sku = product.ProductCode,
            Barcode = product.Barcode,
            Category = product.Category?.Name ?? "Khác",
            Unit = product.BaseUnit,
            RetailPrice = firstStock.RetailPrice ?? product.SellingPrice,
            CostPrice = costPrice,
            CurrentStock = totalStock,
            MinStockLevel = product.MinStock,
            SupplierId = firstStock.SupplierId ?? spUpdated?.SupplierId,
            PrimarySupplierCode = firstStock.Supplier?.Code ?? spUpdated?.Supplier?.Code ?? string.Empty,
            PrimarySupplierName = firstStock.Supplier?.Name ?? spUpdated?.Supplier?.Name ?? string.Empty,
            MinOrderQuantity = moq,
            Status = firstStock.Status,
            ShelfLocation = firstStock.ShelfLocation,
            AssignedDate = firstStock.CreatedAt
        };
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

        var stocks = await _repository.GetBranchStocksByProductAsync(branchId, productId, cancellationToken);
        if (!stocks.Any())
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

        foreach (var stock in stocks)
        {
            stock.Status = normalizedStatus;
            stock.UpdatedAt = DateTime.UtcNow;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var firstStock = stocks.First();
        var uniqueLotSupplierIds = stocks
            .Where(s => s.SupplierId.HasValue)
            .Select(s => s.SupplierId!.Value)
            .Distinct()
            .ToList();
        Guid? displaySupplierId = uniqueLotSupplierIds.Count == 1 ? uniqueLotSupplierIds[0] : null;
        var sp = await _repository.GetSupplierProductAsync(productId, displaySupplierId, cancellationToken);
        var costPrice = SupplierProductPricing.GetEffectivePurchasePriceOrZero(
            sp?.AgreedWholesalePrice ?? 0m,
            product.CostPrice);
        var moq = sp != null && sp.MinimumOrderQuantity > 0
            ? (int)sp.MinimumOrderQuantity
            : 0;

        return new BranchProductDto
        {
            Id = firstStock.Id,
            BranchId = branch.Id,
            BranchCode = branch.BranchCode,
            BranchName = branch.Name,
            ProductId = product.Id,
            Name = product.Name,
            Sku = product.ProductCode,
            Barcode = product.Barcode,
            Category = product.Category?.Name ?? "Khác",
            Unit = product.BaseUnit,
            RetailPrice = firstStock.RetailPrice ?? product.SellingPrice,
            CostPrice = costPrice,
            CurrentStock = stocks.Sum(s => s.QuantityOnHand),
            MinStockLevel = product.MinStock,
            SupplierId = firstStock.SupplierId ?? sp?.SupplierId,
            PrimarySupplierCode = firstStock.Supplier?.Code ?? sp?.Supplier?.Code ?? string.Empty,
            PrimarySupplierName = firstStock.Supplier?.Name ?? sp?.Supplier?.Name ?? string.Empty,
            MinOrderQuantity = moq,
            Status = firstStock.Status,
            ShelfLocation = firstStock.ShelfLocation,
            AssignedDate = firstStock.CreatedAt
        };
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

        var stocks = await _repository.GetBranchStocksByProductAsync(branchId, productId, cancellationToken);
        if (!stocks.Any())
        {
            return; // Đã không còn trong chi nhánh
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

        _repository.RemoveBranchStocks(stocks);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
