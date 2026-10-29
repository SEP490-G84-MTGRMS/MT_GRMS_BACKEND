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
            filter.SearchTerm,
            filter.Category,
            filter.Status,
            filter.StockAlertFilter,
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

                await _repository.AddBranchStockAsync(newStock, cancellationToken);

                assignedList.Add(new BranchProductDto
                {
                    Id = newStock.Id,
                    BranchId = branch.Id,
                    BranchName = branch.Name,
                    ProductId = product.Id,
                    Name = product.Name,
                    Sku = product.ProductCode,
                    Barcode = product.Barcode,
                    Category = product.Category?.Name ?? "Khác",
                    Unit = product.BaseUnit,
                    RetailPrice = product.SellingPrice,
                    CostPrice = product.CostPrice ?? 0m,
                    CurrentStock = 0,
                    MinStockLevel = product.MinStock,
                    PrimarySupplierName = "Nông Sản Tươi Sạch Khải Anh",
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

        // Cập nhật giá bán lẻ niêm yết và mức tồn tối thiểu
        product.SellingPrice = command.RetailPrice;
        product.MinStock = command.MinStockLevel;

        if (!string.IsNullOrWhiteSpace(command.ShelfLocation))
        {
            foreach (var stock in stocks)
            {
                stock.ShelfLocation = command.ShelfLocation.Trim();
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var totalStock = stocks.Sum(s => s.QuantityOnHand);

        return new BranchProductDto
        {
            Id = stocks.First().Id,
            BranchId = branch.Id,
            BranchName = branch.Name,
            ProductId = product.Id,
            Name = product.Name,
            Sku = product.ProductCode,
            Barcode = product.Barcode,
            Category = product.Category?.Name ?? "Khác",
            Unit = product.BaseUnit,
            RetailPrice = product.SellingPrice,
            CostPrice = product.CostPrice ?? 0m,
            CurrentStock = totalStock,
            MinStockLevel = product.MinStock,
            PrimarySupplierName = "Nông Sản Tươi Sạch Khải Anh",
            Status = product.Status,
            ShelfLocation = stocks.First().ShelfLocation,
            AssignedDate = DateTime.UtcNow
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
        product.Status = normalizedStatus;

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new BranchProductDto
        {
            Id = stocks.First().Id,
            BranchId = branch.Id,
            BranchName = branch.Name,
            ProductId = product.Id,
            Name = product.Name,
            Sku = product.ProductCode,
            Barcode = product.Barcode,
            Category = product.Category?.Name ?? "Khác",
            Unit = product.BaseUnit,
            RetailPrice = product.SellingPrice,
            CostPrice = product.CostPrice ?? 0m,
            CurrentStock = stocks.Sum(s => s.QuantityOnHand),
            MinStockLevel = product.MinStock,
            PrimarySupplierName = "Nông Sản Tươi Sạch Khải Anh",
            Status = product.Status,
            ShelfLocation = stocks.First().ShelfLocation,
            AssignedDate = DateTime.UtcNow
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

        _repository.RemoveBranchStocks(stocks);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
