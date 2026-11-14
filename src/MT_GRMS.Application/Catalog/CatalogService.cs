using System.Text.RegularExpressions;
using MT_GRMS.Application.Catalog.Dtos;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Domain.Exceptions;

namespace MT_GRMS.Application.Catalog;

public class CatalogService : ICatalogService
{
    private static readonly Regex ProductCodeRegex = new(@"^[A-Z0-9_-]{1,50}$", RegexOptions.Compiled);
    private const decimal MaxAllowedPrice = 1_000_000_000m;
    private readonly IProductRepository _productRepository;
    private readonly ICategoryRepository _categoryRepository;
    private readonly IProductPriceHistoryRepository? _priceHistoryRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITenantContext _tenantContext;

    public CatalogService(
        IProductRepository productRepository,
        ICategoryRepository categoryRepository,
        IUnitOfWork unitOfWork,
        ITenantContext tenantContext)
        : this(productRepository, categoryRepository, null, unitOfWork, tenantContext)
    {
    }

    public CatalogService(
        IProductRepository productRepository,
        ICategoryRepository categoryRepository,
        IProductPriceHistoryRepository? priceHistoryRepository,
        IUnitOfWork unitOfWork,
        ITenantContext tenantContext)
    {
        _productRepository = productRepository;
        _categoryRepository = categoryRepository;
        _priceHistoryRepository = priceHistoryRepository;
        _unitOfWork = unitOfWork;
        _tenantContext = tenantContext;
    }

    private static ProductResponse MapToResponse(Product p, bool canViewCostPrice = true, bool hasOrders = false)
    {
        var imageUrls = p.ImageUrls != null && p.ImageUrls.Any() ? p.ImageUrls : new List<string>();
        var avatar = imageUrls.FirstOrDefault();

        var unitPrices = new List<ProductUnitResponse>
        {
            new()
            {
                Id = Guid.Empty,
                ProductId = p.Id,
                UnitName = p.BaseUnit,
                ConversionRate = 1.00m,
                Price = p.SellingPrice,
                Barcode = p.Barcode,
                IsBaseUnit = true,
                CreatedAt = p.CreatedAt,
                UpdatedAt = p.UpdatedAt
            }
        };

        if (p.Units != null && p.Units.Any())
        {
            unitPrices.AddRange(p.Units.Where(u => !u.IsDeleted).Select(u => new ProductUnitResponse
            {
                Id = u.Id,
                ProductId = u.ProductId,
                UnitName = u.UnitName,
                ConversionRate = u.ConversionRate,
                Price = u.Price,
                Barcode = u.Barcode,
                IsBaseUnit = false,
                CreatedAt = u.CreatedAt,
                UpdatedAt = u.UpdatedAt
            }));
        }

        var activeUnits = p.Units?.Where(u => !u.IsDeleted).ToList() ?? new List<ProductUnit>();
        var purchaseUnitEntity = activeUnits.FirstOrDefault(u => u.IsPurchaseUnit);
        var baseUnitEntity = activeUnits.FirstOrDefault(u => u.IsBaseUnit);
        var isBaseUnitPurchaseUnit = purchaseUnitEntity != null
            ? purchaseUnitEntity.IsBaseUnit
            : (baseUnitEntity?.IsPurchaseUnit ?? !activeUnits.Any(u => !u.IsBaseUnit && u.IsPurchaseUnit));
        var purchaseUnitName = purchaseUnitEntity?.UnitName ?? p.BaseUnit;

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
            p.EligibleForVatReduction,
            isBaseUnitPurchaseUnit,
            purchaseUnitName,
            unitPrices,
            p.AllowRepack,
            p.SellRawByWeight
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

        var trimmedName = request.Name.Trim();
        if (trimmedName.Length > 200)
        {
            throw new ArgumentException("Tên sản phẩm không được vượt quá 200 ký tự.");
        }

        var trimmedBaseUnit = string.IsNullOrWhiteSpace(request.BaseUnit) ? "Cái" : request.BaseUnit.Trim();
        if (trimmedBaseUnit.Length > 30)
        {
            throw new ArgumentException("Tên đơn vị cơ bản không được vượt quá 30 ký tự.");
        }

        if (request.SellingPrice < 0 || request.SellingPrice > MaxAllowedPrice)
        {
            throw new ArgumentException("Giá bán không được nhỏ hơn 0 hoặc vượt quá 1.000.000.000 đ.");
        }

        if (request.CostPrice.HasValue && (request.CostPrice.Value < 0 || request.CostPrice.Value > MaxAllowedPrice))
        {
            throw new ArgumentException("Giá vốn không được nhỏ hơn 0 hoặc vượt quá 1.000.000.000 đ.");
        }

        if (request.MinStock < 0 || request.MaxStock < request.MinStock)
        {
            throw new ArgumentException("Định mức tồn kho không hợp lệ (Tồn tối đa phải lớn hơn hoặc bằng tồn tối thiểu).");
        }

        var trimmedDescription = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        if (trimmedDescription != null && trimmedDescription.Length > 1000)
        {
            throw new ArgumentException("Mô tả sản phẩm không được vượt quá 1000 ký tự.");
        }

        var trimmedInternalNote = string.IsNullOrWhiteSpace(request.InternalNote) ? null : request.InternalNote.Trim();
        if (trimmedInternalNote != null && trimmedInternalNote.Length > 500)
        {
            throw new ArgumentException("Ghi chú nội bộ không được vượt quá 500 ký tự.");
        }

        if (request.CategoryId == Guid.Empty)
        {
            throw new ArgumentException("Vui lòng chọn nhóm hàng hợp lệ.");
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
            if (code.Length > 50 || !ProductCodeRegex.IsMatch(code))
            {
                throw new ArgumentException("Mã hàng không được vượt quá 50 ký tự và chỉ gồm chữ cái không dấu, số, gạch ngang/dưới.");
            }
            if (await _productRepository.ExistsByCodeAsync(code, null, cancellationToken))
            {
                throw new BusinessException("PRODUCT_CODE_EXISTS", $"Mã sản phẩm '{code}' đã tồn tại trong hệ thống.");
            }
        }

        string? barcode = string.IsNullOrWhiteSpace(request.Barcode) ? null : request.Barcode.Trim();
        if (!string.IsNullOrWhiteSpace(barcode))
        {
            if (barcode.Length > 50 || barcode.Any(char.IsWhiteSpace))
            {
                throw new ArgumentException("Mã vạch không hợp lệ (tối đa 50 ký tự, không chứa khoảng trắng).");
            }
            if (await _productRepository.ExistsByBarcodeAsync(barcode, null, cancellationToken))
            {
                throw new BusinessException("PRODUCT_BARCODE_EXISTS", $"Mã vạch '{barcode}' đã được sử dụng bởi sản phẩm khác.");
            }
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

        if (imageUrls.Any(u => u.Length > 500))
        {
            throw new ArgumentException("Đường dẫn ảnh hàng hóa không được vượt quá 500 ký tự.");
        }

        var productStatus = !string.IsNullOrWhiteSpace(request.Status) && Enum.TryParse<ProductStatus>(request.Status, true, out var parsedStatus)
            ? parsedStatus
            : ProductStatus.Active;

        var product = new Product
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantContext.CurrentTenantId,
            Name = trimmedName,
            CategoryId = request.CategoryId,
            Category = category,
            ProductCode = code,
            Barcode = barcode,
            BaseUnit = trimmedBaseUnit,
            CostPrice = request.CostPrice,
            SellingPrice = request.SellingPrice,
            MinStock = request.MinStock,
            MaxStock = request.MaxStock,
            VatRate = request.VatRate,
            ProductType = string.IsNullOrWhiteSpace(request.ProductType) ? "STANDARD" : request.ProductType,
            Status = productStatus,
            ImageUrls = imageUrls,
            Description = trimmedDescription,
            InternalNote = trimmedInternalNote,
            HasBatches = request.HasBatches || request.AllowRepack,
            AllowRepack = request.AllowRepack,
            SellRawByWeight = request.SellRawByWeight,
            IsFavorite = request.IsFavorite,
            IsActive = true,
            TaxCategory = "STANDARD_1PCT",
            EligibleForVatReduction = request.EligibleForVatReduction
        };

        product.Units.Add(new ProductUnit(
            product.Id,
            product.BaseUnit,
            1.00m,
            product.SellingPrice,
            null,
            isBaseUnit: true,
            isPurchaseUnit: request.IsBaseUnitPurchaseUnit)
        {
            TenantId = _tenantContext.CurrentTenantId
        });

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

        var trimmedName = request.Name.Trim();
        if (trimmedName.Length > 200)
        {
            throw new ArgumentException("Tên sản phẩm không được vượt quá 200 ký tự.");
        }

        if (!string.IsNullOrWhiteSpace(request.BaseUnit) && request.BaseUnit.Trim().Length > 30)
        {
            throw new ArgumentException("Tên đơn vị cơ bản không được vượt quá 30 ký tự.");
        }

        if (request.SellingPrice < 0 || request.SellingPrice > MaxAllowedPrice)
        {
            throw new ArgumentException("Giá bán không được nhỏ hơn 0 hoặc vượt quá 1.000.000.000 đ.");
        }

        if (request.CostPrice.HasValue && (request.CostPrice.Value < 0 || request.CostPrice.Value > MaxAllowedPrice))
        {
            throw new ArgumentException("Giá vốn không được nhỏ hơn 0 hoặc vượt quá 1.000.000.000 đ.");
        }

        if (request.MinStock < 0 || request.MaxStock < request.MinStock)
        {
            throw new ArgumentException("Định mức tồn kho không hợp lệ (Tồn tối đa phải lớn hơn hoặc bằng tồn tối thiểu).");
        }

        var trimmedDescription = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        if (trimmedDescription != null && trimmedDescription.Length > 1000)
        {
            throw new ArgumentException("Mô tả sản phẩm không được vượt quá 1000 ký tự.");
        }

        var trimmedInternalNote = string.IsNullOrWhiteSpace(request.InternalNote) ? null : request.InternalNote.Trim();
        if (trimmedInternalNote != null && trimmedInternalNote.Length > 500)
        {
            throw new ArgumentException("Ghi chú nội bộ không được vượt quá 500 ký tự.");
        }

        var product = await _productRepository.GetByIdWithCategoryAsync(id, cancellationToken);
        if (product == null)
        {
            throw new NotFoundException("Sản phẩm", id);
        }

        if (request.CategoryId == Guid.Empty)
        {
            throw new ArgumentException("Vui lòng chọn nhóm hàng hợp lệ.");
        }

        var category = await _categoryRepository.GetByIdAsync(request.CategoryId, cancellationToken);
        if (category == null)
        {
            throw new BusinessException("CATEGORY_NOT_FOUND", "Nhóm hàng không tồn tại trong hệ thống.");
        }

        if (!string.IsNullOrWhiteSpace(request.Barcode) && request.Barcode.Trim() != product.Barcode)
        {
            var trimmedBarcode = request.Barcode.Trim();
            if (trimmedBarcode.Length > 50 || trimmedBarcode.Any(char.IsWhiteSpace))
            {
                throw new ArgumentException("Mã vạch không hợp lệ (tối đa 50 ký tự, không chứa khoảng trắng).");
            }
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

        var oldPrice = product.SellingPrice;
        var newPrice = request.SellingPrice;
        if (oldPrice != newPrice)
        {
            var costSnapshot = request.CostPrice ?? product.CostPrice;
            var isBelowCost = costSnapshot.HasValue && newPrice < costSnapshot.Value;
            var history = new ProductPriceHistory
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.CurrentTenantId,
                ProductId = product.Id,
                BranchId = null,
                PriceScope = "MASTER",
                PriceType = "SELLING_PRICE",
                OldPrice = oldPrice,
                NewPrice = newPrice,
                CostPriceSnapshot = costSnapshot,
                IsBelowCost = isBelowCost,
                ManagerApprovedBy = isBelowCost ? (_tenantContext.CurrentUserId != Guid.Empty ? _tenantContext.CurrentUserId.ToString() : "STORE_OWNER") : null,
                ManagerApprovalReason = isBelowCost ? "Xác nhận bán dưới giá vốn" : null,
                ChangedByUserId = _tenantContext.CurrentUserId != Guid.Empty ? _tenantContext.CurrentUserId.ToString() : "SYSTEM",
                ChangedByUserName = !string.IsNullOrEmpty(_tenantContext.CurrentRole) ? _tenantContext.CurrentRole : "Quản lý",
                Reason = isBelowCost ? "Điều chỉnh giá bán (Dưới giá vốn)" : "Điều chỉnh giá bán",
                CreatedAt = DateTime.UtcNow
            };
            if (_priceHistoryRepository != null)
            {
                await _priceHistoryRepository.AddAsync(history, cancellationToken);
            }
        }

        product.Name = trimmedName;
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
        product.Description = trimmedDescription;
        product.InternalNote = trimmedInternalNote;
        product.HasBatches = request.HasBatches || request.AllowRepack;
        product.AllowRepack = request.AllowRepack;
        product.SellRawByWeight = request.SellRawByWeight;
        product.IsFavorite = request.IsFavorite;
        product.TaxCategory = request.TaxCategory ?? product.TaxCategory ?? "STANDARD_1PCT";
        product.EligibleForVatReduction = request.EligibleForVatReduction;
        product.UpdatedAt = DateTime.UtcNow;

        if (product.Units != null && product.Units.Any())
        {
            var baseUnitEntity = product.Units.FirstOrDefault(u => !u.IsDeleted && u.IsBaseUnit);
            if (baseUnitEntity != null)
            {
                baseUnitEntity.UnitName = product.BaseUnit;
                baseUnitEntity.Price = product.SellingPrice;
                baseUnitEntity.ConversionRate = 1.00m;
                if (request.IsBaseUnitPurchaseUnit.HasValue)
                {
                    baseUnitEntity.IsPurchaseUnit = request.IsBaseUnitPurchaseUnit.Value;
                    if (request.IsBaseUnitPurchaseUnit.Value)
                    {
                        foreach (var otherUnit in product.Units.Where(u => !u.IsDeleted && u.Id != baseUnitEntity.Id && u.IsPurchaseUnit))
                        {
                            otherUnit.IsPurchaseUnit = false;
                        }
                    }
                }
            }
            else if (request.IsBaseUnitPurchaseUnit.HasValue)
            {
                product.Units.Add(new ProductUnit(
                    product.Id,
                    product.BaseUnit,
                    1.00m,
                    product.SellingPrice,
                    null,
                    isBaseUnit: true,
                    isPurchaseUnit: request.IsBaseUnitPurchaseUnit.Value)
                {
                    TenantId = _tenantContext.CurrentTenantId
                });
                if (request.IsBaseUnitPurchaseUnit.Value)
                {
                    foreach (var otherUnit in product.Units.Where(u => !u.IsDeleted && !u.IsBaseUnit && u.IsPurchaseUnit))
                    {
                        otherUnit.IsPurchaseUnit = false;
                    }
                }
            }
        }
        else if (request.IsBaseUnitPurchaseUnit.HasValue && product.Units != null)
        {
            product.Units.Add(new ProductUnit(
                product.Id,
                product.BaseUnit,
                1.00m,
                product.SellingPrice,
                null,
                isBaseUnit: true,
                isPurchaseUnit: request.IsBaseUnitPurchaseUnit.Value)
            {
                TenantId = _tenantContext.CurrentTenantId
            });
        }

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

    public async Task<IReadOnlyList<ProductPriceHistoryDto>> GetPriceHistoryAsync(Guid productId, Guid? branchId = null, CancellationToken cancellationToken = default)
    {
        if (_priceHistoryRepository == null) return Array.Empty<ProductPriceHistoryDto>();
        var list = await _priceHistoryRepository.GetByProductIdAsync(productId, branchId, cancellationToken);
        return list.Select(h => new ProductPriceHistoryDto(
            h.Id,
            h.ProductId,
            h.BranchId,
            h.Branch?.Name,
            h.PriceScope,
            h.PriceType,
            h.OldPrice,
            h.NewPrice,
            h.CostPriceSnapshot,
            h.IsBelowCost,
            h.ManagerApprovedBy,
            h.ManagerApprovalReason,
            h.ChangedByUserId,
            h.ChangedByUserName,
            h.Reason,
            h.CreatedAt
        )).ToList();
    }

    private bool CanViewCostPrice()
    {
        var role = _tenantContext.CurrentRole;
        return role is "SUPER_ADMIN" or "STORE_OWNER" or "BRANCH_MANAGER" or "StoreOwner" or "Manager";
    }
}
