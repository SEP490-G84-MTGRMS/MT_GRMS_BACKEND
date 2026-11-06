using MT_GRMS.Application.Catalog.Dtos;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Domain.Exceptions;

namespace MT_GRMS.Application.Catalog;

public class CatalogService : ICatalogService
{
    private readonly IProductRepository _productRepository;
    private readonly ICategoryRepository _categoryRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITenantContext _tenantContext;

    public CatalogService(
        IProductRepository productRepository,
        ICategoryRepository categoryRepository,
        IUnitOfWork unitOfWork,
        ITenantContext tenantContext)
    {
        _productRepository = productRepository;
        _categoryRepository = categoryRepository;
        _unitOfWork = unitOfWork;
        _tenantContext = tenantContext;
    }

    private static ProductResponse MapToResponse(Product p, bool canViewCostPrice = true, bool hasOrders = false)
    {
        var imageUrls = p.ImageUrls != null && p.ImageUrls.Any() ? p.ImageUrls : new List<string>();
        var avatar = imageUrls.FirstOrDefault();

        return new ProductResponse(
            p.Id,
            p.ProductCode,
            p.Barcode,
            p.Name,
            p.CategoryId,
            p.Category?.Name ?? "Chưa phân loại",
            p.BaseUnit,
            p.SellingPrice,
            canViewCostPrice ? p.CostPrice : null,
            p.MinStock,
            p.MaxStock,
            p.VatRate,
            p.ProductType,
            p.Status.ToString(),
            avatar,
            p.Description,
            p.InternalNote,
            p.HasBatches,
            p.IsFavorite,
            p.CreatedAt,
            p.UpdatedAt,
            imageUrls,
            hasOrders,
            p.TaxCategory ?? "STANDARD_1PCT",
            p.EligibleForVatReduction
        );
    }

    public async Task<PagedResult<ProductResponse>> GetProductsPagedAsync(ProductQueryFilter filter, CancellationToken cancellationToken = default)
    {
        var (items, totalCount) = await _productRepository.GetPagedListAsync(filter, cancellationToken);
        var canSeeCost = CanViewCostPrice();
        var itemIds = items.Select(x => x.Id).ToList();
        var productIdsWithOrders = await _productRepository.GetProductIdsWithOrdersAsync(itemIds, cancellationToken);

        var dtos = items.Select(p => MapToResponse(p, canSeeCost, productIdsWithOrders.Contains(p.Id))).ToList();

        return new PagedResult<ProductResponse>
        {
            Items = dtos,
            Page = Math.Max(1, filter.PageIndex),
            PageSize = Math.Clamp(filter.PageSize, 1, 100),
            TotalItems = totalCount
        };
    }

    public async Task<ProductResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var product = await _productRepository.GetByIdWithCategoryAsync(id, cancellationToken);
        if (product == null) return null;

        var hasOrders = await _productRepository.HasOrdersAsync(id, cancellationToken);
        return MapToResponse(product, CanViewCostPrice(), hasOrders);
    }

    public async Task<ProductResponse> CreateProductAsync(ProductCreateRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ArgumentException("Tên sản phẩm không được để trống.");
        }

        if (request.SellingPrice < 0)
        {
            throw new ArgumentException("Giá bán không được nhỏ hơn 0.");
        }

        if (request.CostPrice.HasValue && request.CostPrice.Value < 0)
        {
            throw new ArgumentException("Giá vốn không được nhỏ hơn 0.");
        }

        if (request.MinStock < 0 || request.MaxStock < request.MinStock)
        {
            throw new ArgumentException("Định mức tồn kho không hợp lệ.");
        }

        var category = await _categoryRepository.GetByIdAsync(request.CategoryId, cancellationToken);
        if (category == null)
        {
            throw new BusinessException("CATEGORY_NOT_FOUND", "Nhóm hàng không tồn tại trong hệ thống.");
        }

        string code;
        if (string.IsNullOrWhiteSpace(request.ProductCode))
        {
            var maxCode = await _productRepository.GetMaxSequentialCodeAsync("SP", 8, cancellationToken);
            var nextNumber = 1;
            if (!string.IsNullOrWhiteSpace(maxCode) && maxCode.Length == 8 && int.TryParse(maxCode[2..], out var currentMax))
            {
                nextNumber = currentMax + 1;
            }

            code = $"SP{nextNumber:D6}";
            while (await _productRepository.ExistsByCodeAsync(code, null, cancellationToken))
            {
                nextNumber++;
                code = $"SP{nextNumber:D6}";
            }
        }
        else
        {
            code = request.ProductCode.Trim().ToUpper();
            if (await _productRepository.ExistsByCodeAsync(code, null, cancellationToken))
            {
                throw new BusinessException("PRODUCT_CODE_EXISTS", $"Mã sản phẩm '{code}' đã tồn tại trong hệ thống.");
            }
        }

        string? barcode = string.IsNullOrWhiteSpace(request.Barcode) ? null : request.Barcode.Trim();
        if (!string.IsNullOrWhiteSpace(barcode) && await _productRepository.ExistsByBarcodeAsync(barcode, null, cancellationToken))
        {
            throw new BusinessException("PRODUCT_BARCODE_EXISTS", $"Mã vạch '{barcode}' đã được sử dụng bởi sản phẩm khác.");
        }

        var imageUrls = new List<string>();
        if (request.ImageUrls != null && request.ImageUrls.Any())
        {
            imageUrls = request.ImageUrls.Where(u => !string.IsNullOrWhiteSpace(u)).Select(u => u.Trim()).ToList();
        }
        else if (!string.IsNullOrWhiteSpace(request.ImageUrl))
        {
            imageUrls.Add(request.ImageUrl.Trim());
        }

        var productStatus = !string.IsNullOrWhiteSpace(request.Status) && Enum.TryParse<ProductStatus>(request.Status, true, out var parsedStatus)
            ? parsedStatus
            : ProductStatus.Active;

        var product = new Product
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.CurrentTenantId,
            Name = request.Name.Trim(),
            CategoryId = request.CategoryId,
            Category = category,
            ProductCode = code,
            Barcode = barcode,
            BaseUnit = string.IsNullOrWhiteSpace(request.BaseUnit) ? "Cái" : request.BaseUnit.Trim(),
            CostPrice = request.CostPrice,
            SellingPrice = request.SellingPrice,
            MinStock = request.MinStock,
            MaxStock = request.MaxStock,
            VatRate = request.VatRate,
            ProductType = string.IsNullOrWhiteSpace(request.ProductType) ? "STANDARD" : request.ProductType,
            Status = productStatus,
            ImageUrls = imageUrls,
            Description = request.Description?.Trim(),
            InternalNote = request.InternalNote?.Trim(),
            HasBatches = request.HasBatches,
            IsFavorite = request.IsFavorite,
            IsActive = true,
            TaxCategory = "STANDARD_1PCT",
            EligibleForVatReduction = request.EligibleForVatReduction
        };

        await _productRepository.AddAsync(product, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return MapToResponse(product, CanViewCostPrice());
    }

    public async Task<ProductResponse> UpdateProductAsync(Guid id, ProductUpdateRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ArgumentException("Tên sản phẩm không được để trống.");
        }

        if (request.SellingPrice < 0)
        {
            throw new ArgumentException("Giá bán không được nhỏ hơn 0.");
        }

        if (request.CostPrice.HasValue && request.CostPrice.Value < 0)
        {
            throw new ArgumentException("Giá vốn không được nhỏ hơn 0.");
        }

        if (request.MinStock < 0 || request.MaxStock < request.MinStock)
        {
            throw new ArgumentException("Định mức tồn kho không hợp lệ.");
        }

        var product = await _productRepository.GetByIdWithCategoryAsync(id, cancellationToken);
        if (product == null)
        {
            throw new NotFoundException("Sản phẩm", id);
        }

        var category = await _categoryRepository.GetByIdAsync(request.CategoryId, cancellationToken);
        if (category == null)
        {
            throw new BusinessException("CATEGORY_NOT_FOUND", "Nhóm hàng không tồn tại trong hệ thống.");
        }

        if (!string.IsNullOrWhiteSpace(request.Barcode) && request.Barcode.Trim() != product.Barcode)
        {
            var trimmedBarcode = request.Barcode.Trim();
            if (await _productRepository.ExistsByBarcodeAsync(trimmedBarcode, id, cancellationToken))
            {
                throw new BusinessException("PRODUCT_BARCODE_EXISTS", $"Mã vạch '{trimmedBarcode}' đã được sử dụng bởi sản phẩm khác.");
            }
            product.Barcode = trimmedBarcode;
        }
        else if (string.IsNullOrWhiteSpace(request.Barcode))
        {
            product.Barcode = null;
        }

        product.Name = request.Name.Trim();
        product.CategoryId = request.CategoryId;
        product.Category = category;
        product.BaseUnit = string.IsNullOrWhiteSpace(request.BaseUnit) ? product.BaseUnit : request.BaseUnit.Trim();
        product.CostPrice = request.CostPrice;
        product.SellingPrice = request.SellingPrice;
        product.MinStock = request.MinStock;
        product.MaxStock = request.MaxStock;
        product.VatRate = request.VatRate;
        if (!string.IsNullOrWhiteSpace(request.Status) && Enum.TryParse<ProductStatus>(request.Status, true, out var updatedStatus))
        {
            product.Status = updatedStatus;
        }
        product.Description = request.Description?.Trim();
        product.InternalNote = request.InternalNote?.Trim();
        product.HasBatches = request.HasBatches;
        product.IsFavorite = request.IsFavorite;
        product.TaxCategory = request.TaxCategory ?? product.TaxCategory ?? "STANDARD_1PCT";
        product.EligibleForVatReduction = request.EligibleForVatReduction;
        product.UpdatedAt = DateTime.UtcNow;

        if (request.ImageUrls != null)
        {
            product.ImageUrls = request.ImageUrls.Where(u => !string.IsNullOrWhiteSpace(u)).Select(u => u.Trim()).ToList();
        }
        else if (request.ImageUrl != null)
        {
            if (string.IsNullOrWhiteSpace(request.ImageUrl))
            {
                product.ImageUrls = new List<string>();
            }
            else if (!product.ImageUrls.Contains(request.ImageUrl.Trim()))
            {
                product.ImageUrls = new List<string> { request.ImageUrl.Trim() };
            }
        }

        _productRepository.Update(product);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return MapToResponse(product, CanViewCostPrice());
    }

    public async Task DeleteProductAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var product = await _productRepository.GetByIdAsync(id, cancellationToken);
        if (product == null)
        {
            throw new NotFoundException("Sản phẩm", id);
        }

        if (await _productRepository.HasOrdersAsync(id, cancellationToken))
        {
            throw new BusinessException("PRODUCT_HAS_ORDERS", "Sản phẩm đã có đơn hàng (Order) phát sinh, không thể xóa mà chỉ được phép chuyển sang trạng thái Ngừng kinh doanh.");
        }

        await _productRepository.DeleteWithUnitsAsync(product, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<ProductResponse?> GetByBarcodeAsync(string barcode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(barcode)) return null;
        var trimmed = barcode.Trim();
        var list = await _productRepository.FindAsync(p => p.Barcode == trimmed || p.ProductCode == trimmed, cancellationToken);
        var p = list.FirstOrDefault();
        if (p == null) return null;

        return MapToResponse(p, CanViewCostPrice());
    }

    public async Task<IReadOnlyList<ProductResponse>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var (items, _) = await _productRepository.GetPagedListAsync(new ProductQueryFilter { PageSize = 1000 }, cancellationToken);
        var canSeeCost = CanViewCostPrice();
        var itemIds = items.Select(x => x.Id).ToList();
        var productIdsWithOrders = await _productRepository.GetProductIdsWithOrdersAsync(itemIds, cancellationToken);
        return items.Select(p => MapToResponse(p, canSeeCost, productIdsWithOrders.Contains(p.Id))).ToList();
    }

    private bool CanViewCostPrice()
    {
        var role = _tenantContext.CurrentRole;
        return role is "SUPER_ADMIN" or "STORE_OWNER" or "BRANCH_MANAGER" or "StoreOwner" or "Manager";
    }
}
