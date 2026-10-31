using MT_GRMS.Application.Catalog.Dtos;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Entities.Catalog;
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

    private bool CanViewCostPrice()
    {
        var role = _tenantContext.CurrentRole;
        return role is "SUPER_ADMIN" or "STORE_OWNER" or "BRANCH_MANAGER" or "StoreOwner" or "Manager";
    }

    private static ProductResponse MapToResponse(Product p, bool canViewCostPrice)
    {
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
            p.Status,
            p.ImageUrl,
            p.Description,
            p.InternalNote,
            p.HasBatches,
            p.IsFavorite,
            p.CreatedAt,
            p.UpdatedAt
        );
    }

    public async Task<PagedResult<ProductResponse>> GetProductsPagedAsync(ProductQueryFilter filter, CancellationToken cancellationToken = default)
    {
        var (items, totalCount) = await _productRepository.GetPagedListAsync(filter, cancellationToken);
        var canSeeCost = CanViewCostPrice();

        var dtos = items.Select(p => MapToResponse(p, canSeeCost)).ToList();

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

        return MapToResponse(product, CanViewCostPrice());
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

        var product = new Product
        {
            Id = Guid.NewGuid(),
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
            Status = string.IsNullOrWhiteSpace(request.Status) ? "Active" : request.Status,
            ImageUrl = request.ImageUrl,
            Description = request.Description?.Trim(),
            InternalNote = request.InternalNote?.Trim(),
            HasBatches = request.HasBatches,
            IsFavorite = request.IsFavorite
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
        product.Status = string.IsNullOrWhiteSpace(request.Status) ? product.Status : request.Status;
        product.ImageUrl = request.ImageUrl;
        product.Description = request.Description?.Trim();
        product.InternalNote = request.InternalNote?.Trim();
        product.HasBatches = request.HasBatches;
        product.IsFavorite = request.IsFavorite;
        product.UpdatedAt = DateTime.UtcNow;

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

        _productRepository.Delete(product);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ProductResponse>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var (items, _) = await _productRepository.GetPagedListAsync(new ProductQueryFilter { PageSize = 1000 }, cancellationToken);
        var canSeeCost = CanViewCostPrice();
        return items.Select(p => MapToResponse(p, canSeeCost)).ToList();
    }
}
