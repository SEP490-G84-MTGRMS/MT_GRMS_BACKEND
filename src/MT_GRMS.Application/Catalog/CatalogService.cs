using MT_GRMS.Application.Catalog.Dtos;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Entities.Catalog;

namespace MT_GRMS.Application.Catalog;

/// <summary>
/// Service tầng Application quản lý danh mục sản phẩm (Catalog)
/// </summary>
public class CatalogService : ICatalogService
{
    private readonly IRepository<Product> _productRepository;
    private readonly IUnitOfWork _unitOfWork;

    public CatalogService(IRepository<Product> productRepository, IUnitOfWork unitOfWork)
    {
        _productRepository = productRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<ProductResponse> CreateProductAsync(ProductCreateRequest request, CancellationToken cancellationToken = default)
    {
        var product = new Product
        {
            Id = Guid.NewGuid(),
            SKU = request.SKU,
            Barcode = request.Barcode,
            ProductName = request.ProductName,
            CategoryId = request.CategoryId ?? Guid.Empty,
            BasePrice = request.BasePrice,
            CostPrice = request.CostPrice,
            UnitName = request.UnitName,
            HasBatches = request.HasBatches,
            IsActive = true,
            TaxCategory = "STANDARD_1PCT"
        };

        await _productRepository.AddAsync(product, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new ProductResponse(
            product.Id,
            product.SKU,
            product.Barcode,
            product.ProductName,
            product.CategoryId,
            null,
            product.BasePrice,
            product.CostPrice ?? 0m,
            product.UnitName,
            product.HasBatches,
            product.IsActive,
            product.TaxCategory
        );
    }

    public async Task<ProductResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var p = await _productRepository.GetByIdAsync(id, cancellationToken);
        if (p == null) return null;

        return new ProductResponse(
            p.Id,
            p.SKU,
            p.Barcode,
            p.ProductName,
            p.CategoryId,
            p.Category?.CategoryName,
            p.BasePrice,
            p.CostPrice ?? 0m,
            p.UnitName,
            p.HasBatches,
            p.IsActive,
            p.TaxCategory
        );
    }

    public async Task<IReadOnlyList<ProductResponse>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var products = await _productRepository.GetAllAsync(cancellationToken);
        return products.Select(p => new ProductResponse(
            p.Id,
            p.SKU,
            p.Barcode,
            p.ProductName,
            p.CategoryId,
            p.Category?.CategoryName,
            p.BasePrice,
            p.CostPrice ?? 0m,
            p.UnitName,
            p.HasBatches,
            p.IsActive,
            p.TaxCategory
        )).ToList();
    }
}
