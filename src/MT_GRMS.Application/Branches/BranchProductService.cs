using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MT_GRMS.Application.Branches.Dtos;
using MT_GRMS.Application.Branches.Validators;
using MT_GRMS.Application.Common;
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

        var assignedList = new List<BranchProductDto>();

        foreach (var productId in command.ProductIds.Distinct())
        {
            var product = await _repository.GetProductByIdAsync(productId, cancellationToken);
            if (product == null || product.IsDeleted)
            {
                continue;
            }

            var existingStocks = await _repository.GetBranchStocksByProductAsync(branchId, productId, cancellationToken);
            if (!existingStocks.Any())
            {
                var newStock = new BranchStock
                {
                    TenantId = _tenantContext.CurrentTenantId,
                    BranchId = branchId,
                    ProductId = productId,
                    QuantityOnHand = 0.000m,
                    QuantityReserved = 0.000m,
                    LandedCostPerUnit = product.CostPrice ?? 0m,
                    ShelfLocation = command.ShelfLocation ?? "Kệ hàng",
                    ZoneArea = "Khu vực trưng bày"
                };

                if (command.DefaultRetailPrice.HasValue && command.DefaultRetailPrice.Value > 0)
                {
                    product.SellingPrice = command.DefaultRetailPrice.Value;
                }

                if (command.DefaultMinStock.HasValue && command.DefaultMinStock.Value >= 0)
                {
                    product.MinStock = command.DefaultMinStock.Value;
                }

                var sp = await _repository.GetSupplierProductAsync(productId, null, cancellationToken);
                var moq = sp != null && sp.MinimumOrderQuantity > 0 ? (int)sp.MinimumOrderQuantity : 1;
                if (command.DefaultMinOrderQuantity.HasValue && command.DefaultMinOrderQuantity.Value >= 0)
                {
                    moq = command.DefaultMinOrderQuantity.Value;
                    if (sp != null)
                    {
                        sp.MinimumOrderQuantity = command.DefaultMinOrderQuantity.Value;
                        sp.UpdatedAt = DateTime.UtcNow;
                    }
                }

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
                    RetailPrice = product.SellingPrice,
                    CostPrice = sp?.AgreedWholesalePrice ?? product.CostPrice ?? 0m,
                    CurrentStock = 0,
                    MinStockLevel = product.MinStock,
                    PrimarySupplierCode = sp?.Supplier?.Code ?? string.Empty,
                    PrimarySupplierName = sp?.Supplier?.Name ?? string.Empty,
                    MinOrderQuantity = moq,
                    Status = product.Status,
                    ShelfLocation = newStock.ShelfLocation,
                    AssignedDate = DateTime.UtcNow
                });
            }
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

        // Cập nhật giá bán lẻ niêm yết và trạng thái trên từng bản ghi kho của chi nhánh
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

        if (command.MinStockLevel >= 0)
        {
            product.MinStock = command.MinStockLevel;
        }

        // Cập nhật MOQ trên SupplierProduct tương ứng của nhà cung cấp đang thao tác
        if (command.MinOrderQuantity.HasValue && command.MinOrderQuantity.Value > 0)
        {
            var targetSupplierId = command.SupplierId ?? stocks.FirstOrDefault(s => s.SupplierId.HasValue)?.SupplierId;
            var sp = await _repository.GetSupplierProductAsync(productId, targetSupplierId, cancellationToken);
            if (sp != null)
            {
                sp.MinimumOrderQuantity = command.MinOrderQuantity.Value;
                sp.UpdatedAt = DateTime.UtcNow;
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var firstStock = stocks.First();
        var spUpdated = await _repository.GetSupplierProductAsync(productId, firstStock.SupplierId, cancellationToken);
        var costPrice = (spUpdated != null && spUpdated.AgreedWholesalePrice > 0)
            ? spUpdated.AgreedWholesalePrice
            : (product.CostPrice ?? 0m);
        var moq = (spUpdated != null && spUpdated.MinimumOrderQuantity > 0)
            ? (int)spUpdated.MinimumOrderQuantity
            : (command.MinOrderQuantity ?? 1);

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
        foreach (var stock in stocks)
        {
            stock.Status = normalizedStatus;
            stock.UpdatedAt = DateTime.UtcNow;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var firstStock = stocks.First();
        var sp = await _repository.GetSupplierProductAsync(productId, firstStock.SupplierId, cancellationToken);
        var costPrice = (sp != null && sp.AgreedWholesalePrice > 0)
            ? sp.AgreedWholesalePrice
            : (product.CostPrice ?? 0m);
        var moq = (sp != null && sp.MinimumOrderQuantity > 0)
            ? (int)sp.MinimumOrderQuantity
            : 10;

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
