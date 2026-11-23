using MT_GRMS.Application.Admin.Validators;
using MT_GRMS.Application.Admin.Dtos;
using MT_GRMS.Application.Admin.Repositories;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Exceptions;

namespace MT_GRMS.Application.Admin.Services;

public class MasterCatalogService : IMasterCatalogService
{
    private readonly IMasterProductRepository _masterRepo;
    private readonly IUnitOfWork _unitOfWork;

    public MasterCatalogService(IMasterProductRepository masterRepo, IUnitOfWork unitOfWork)
    {
        _masterRepo = masterRepo;
        _unitOfWork = unitOfWork;
    }

    public async Task<PagedResult<MasterProductDto>> GetPagedListAsync(
        MasterProductFilterRequest filter,
        CancellationToken cancellationToken = default)
    {
        var (items, totalCount) = await _masterRepo.GetPagedListAsync(filter, cancellationToken);

        var dtos = items.Select(MapToDto).ToList();
        return new PagedResult<MasterProductDto>
        {
            Items = dtos,
            TotalItems = totalCount,
            Page = filter.PageIndex,
            PageSize = filter.PageSize
        };
    }

    public async Task<MasterProductDetailDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _masterRepo.GetByIdWithUnitsAsync(id, cancellationToken);
        return entity == null ? null : MapToDetailDto(entity);
    }

    public async Task<MasterCatalogKpiDto> GetKpisAsync(CancellationToken cancellationToken = default)
    {
        return await _masterRepo.GetKpisAsync(cancellationToken);
    }

    public async Task<MasterProductDetailDto> CreateAsync(
        CreateMasterProductRequest request,
        CancellationToken cancellationToken = default)
    {
        MasterProductValidator.ValidateCreate(request);
        var barcode = request.Barcode.Trim();

        if (await _masterRepo.ExistsByBarcodeAsync(barcode, null, cancellationToken))
        {
            throw new BusinessException("BARCODE_EXISTS", $"Mã vạch (Barcode) '{barcode}' đã tồn tại trong Thư viện hàng mẫu.");
        }

        var product = new MasterProduct
        {
            Barcode = barcode,
            Name = request.Name.Trim(),
            Brand = request.Brand.Trim(),
            CategoryName = request.CategoryName.Trim(),
            BaseUnit = request.BaseUnit.Trim(),
            SuggestedRetailPrice = request.SuggestedRetailPrice,
            VatRate = request.VatRate,
            TaxCategory = string.IsNullOrWhiteSpace(request.TaxCategory) ? "STANDARD_1PCT" : request.TaxCategory.Trim(),
            ImageUrl = request.ImageUrl?.Trim(),
            Description = request.Description?.Trim(),
            IsActive = true,
            UsageCount = 0
        };

        if (request.Units != null && request.Units.Count > 0)
        {
            foreach (var u in request.Units)
            {
                product.Units.Add(new MasterProductUnit
                {
                    MasterProductId = product.Id,
                    UnitName = u.UnitName.Trim(),
                    ConversionRate = u.ConversionRate,
                    Barcode = string.IsNullOrWhiteSpace(u.Barcode) ? null : u.Barcode.Trim(),
                    IsPurchaseUnit = u.IsPurchaseUnit
                });
            }
        }

        await _masterRepo.AddAsync(product, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return MapToDetailDto(product);
    }

    public async Task<MasterProductDetailDto> UpdateAsync(
        Guid id,
        UpdateMasterProductRequest request,
        CancellationToken cancellationToken = default)
    {
        var product = await _masterRepo.GetByIdWithUnitsAsync(id, cancellationToken)
            ?? throw new NotFoundException($"Không tìm thấy sản phẩm mẫu với ID: {id}");

        MasterProductValidator.EnsureValid(request);
        product.Name = request.Name.Trim();
        product.Brand = request.Brand.Trim();
        product.CategoryName = request.CategoryName.Trim();
        product.BaseUnit = request.BaseUnit.Trim();
        product.SuggestedRetailPrice = request.SuggestedRetailPrice;
        product.VatRate = request.VatRate;
        product.TaxCategory = string.IsNullOrWhiteSpace(request.TaxCategory) ? "STANDARD_1PCT" : request.TaxCategory.Trim();
        product.ImageUrl = request.ImageUrl?.Trim();
        product.Description = request.Description?.Trim();
        product.IsActive = request.IsActive;

        // Cập nhật units
        product.Units.Clear();
        if (request.Units != null && request.Units.Count > 0)
        {
            foreach (var u in request.Units)
            {
                product.Units.Add(new MasterProductUnit
                {
                    MasterProductId = product.Id,
                    UnitName = u.UnitName.Trim(),
                    ConversionRate = u.ConversionRate,
                    Barcode = string.IsNullOrWhiteSpace(u.Barcode) ? null : u.Barcode.Trim(),
                    IsPurchaseUnit = u.IsPurchaseUnit
                });
            }
        }

        _masterRepo.Update(product);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return MapToDetailDto(product);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var product = await _masterRepo.GetByIdWithUnitsAsync(id, cancellationToken);
        if (product == null) return false;

        product.IsDeleted = true;
        _masterRepo.Update(product);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<MasterProductSuggestionDto?> LookupByBarcodeAsync(string barcode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(barcode)) return null;

        var product = await _masterRepo.GetByBarcodeWithUnitsAsync(barcode, cancellationToken);
        if (product == null) return null;

        return new MasterProductSuggestionDto(
            MasterProductId: product.Id,
            Barcode: product.Barcode,
            Name: product.Name,
            Brand: product.Brand,
            CategoryName: product.CategoryName,
            BaseUnit: product.BaseUnit,
            SuggestedRetailPrice: product.SuggestedRetailPrice,
            VatRate: product.VatRate,
            TaxCategory: product.TaxCategory,
            ImageUrl: product.ImageUrl,
            Units: product.Units.Select(u => new MasterProductUnitDto(
                Id: u.Id,
                UnitName: u.UnitName,
                ConversionRate: u.ConversionRate,
                Barcode: u.Barcode,
                IsPurchaseUnit: u.IsPurchaseUnit
            )).ToList()
        );
    }

    public async Task<IReadOnlyList<string>> GetBrandsAsync(CancellationToken cancellationToken = default)
    {
        return await _masterRepo.GetAllBrandsAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetCategoriesAsync(CancellationToken cancellationToken = default)
    {
        return await _masterRepo.GetAllCategoriesAsync(cancellationToken);
    }

    private static MasterProductDto MapToDto(MasterProduct p)
    {
        return new MasterProductDto(
            Id: p.Id,
            Barcode: p.Barcode,
            Description: p.Description,
            Name: p.Name,
            Brand: p.Brand,
            CategoryName: p.CategoryName,
            BaseUnit: p.BaseUnit,
            SuggestedRetailPrice: p.SuggestedRetailPrice,
            VatRate: p.VatRate,
            TaxCategory: p.TaxCategory,
            ImageUrl: p.ImageUrl,
            UsageCount: p.UsageCount,
            IsActive: p.IsActive,
            Units: p.Units.Select(u => new MasterProductUnitDto(
                Id: u.Id,
                UnitName: u.UnitName,
                ConversionRate: u.ConversionRate,
                Barcode: u.Barcode,
                IsPurchaseUnit: u.IsPurchaseUnit
            )).ToList()
        );
    }

    private static MasterProductDetailDto MapToDetailDto(MasterProduct p)
    {
        return new MasterProductDetailDto(
            Id: p.Id,
            Barcode: p.Barcode,
            Name: p.Name,
            Brand: p.Brand,
            CategoryName: p.CategoryName,
            BaseUnit: p.BaseUnit,
            SuggestedRetailPrice: p.SuggestedRetailPrice,
            VatRate: p.VatRate,
            TaxCategory: p.TaxCategory,
            ImageUrl: p.ImageUrl,
            Description: p.Description,
            UsageCount: p.UsageCount,
            IsActive: p.IsActive,
            CreatedAt: p.CreatedAt,
            Units: p.Units.Select(u => new MasterProductUnitDto(
                Id: u.Id,
                UnitName: u.UnitName,
                ConversionRate: u.ConversionRate,
                Barcode: u.Barcode,
                IsPurchaseUnit: u.IsPurchaseUnit
            )).ToList()
        );
    }
}
