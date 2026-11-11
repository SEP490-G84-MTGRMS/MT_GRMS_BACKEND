using System.Globalization;
using System.Text;
using MT_GRMS.Application.Catalog.Dtos;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Entities.Catalog;

namespace MT_GRMS.Application.Catalog;

public class ProductUnitService : IProductUnitService
{
    private readonly IProductUnitRepository _unitRepository;
    private readonly IProductRepository _productRepository;
    private readonly IUnitOfWork _unitOfWork;

    public ProductUnitService(
        IProductUnitRepository unitRepository,
        IProductRepository productRepository,
        IUnitOfWork unitOfWork)
    {
        _unitRepository = unitRepository;
        _productRepository = productRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<IReadOnlyList<ProductUnitResponse>> GetUnitsByProductIdAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        var product = await _productRepository.GetByIdAsync(productId, cancellationToken);
        if (product == null)
        {
            throw new KeyNotFoundException("Không tìm thấy sản phẩm.");
        }

        var units = await _unitRepository.GetByProductIdAsync(productId, cancellationToken);

        return units.Select(MapToResponse).ToList();
    }

    public async Task<ProductUnitResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var unit = await _unitRepository.GetByIdAsync(id, cancellationToken);
        return unit == null ? null : MapToResponse(unit);
    }

    public async Task<ProductUnitResponse> CreateAsync(Guid productId, ProductUnitCreateRequest request, CancellationToken cancellationToken = default)
    {
        ValidateRequest(request.UnitName, request.ConversionRate, request.Price, request.Barcode);

        var product = await _productRepository.GetByIdAsync(productId, cancellationToken);
        if (product == null)
        {
            throw new KeyNotFoundException("Không tìm thấy sản phẩm.");
        }

        var nameExists = await _unitRepository.ExistsByNameAsync(productId, request.UnitName.Trim(), null, cancellationToken);
        if (nameExists)
        {
            throw new InvalidOperationException("Đơn vị tính này đã tồn tại cho sản phẩm.");
        }

        if (!string.IsNullOrWhiteSpace(request.Barcode))
        {
            var trimmedBarcode = request.Barcode.Trim();
            var barcodeExists = await _unitRepository.ExistsByBarcodeAsync(trimmedBarcode, null, cancellationToken);
            if (barcodeExists)
            {
                throw new InvalidOperationException($"Mã vạch '{trimmedBarcode}' đã tồn tại trong hệ thống.");
            }
        }

        // Xử lý đơn vị cơ bản
        if (request.IsBaseUnit)
        {
            var oldBaseUnit = await _unitRepository.GetBaseUnitByProductIdAsync(productId, cancellationToken);
            if (oldBaseUnit != null)
            {
                oldBaseUnit.IsBaseUnit = false;
                _unitRepository.Update(oldBaseUnit);
            }

            // Đồng bộ tên đơn vị cơ bản vào bảng Product
            product.BaseUnit = request.UnitName.Trim();
            _productRepository.Update(product);
        }

        // Xử lý đơn vị nhập hàng (mỗi sản phẩm chỉ có tối đa 1 đơn vị được đánh dấu là đơn vị nhập hàng)
        if (request.IsPurchaseUnit)
        {
            var existingUnits = await _unitRepository.GetByProductIdAsync(productId, cancellationToken);
            if (existingUnits != null)
            {
                foreach (var existingUnit in existingUnits.Where(u => u.IsPurchaseUnit))
                {
                    existingUnit.IsPurchaseUnit = false;
                    _unitRepository.Update(existingUnit);
                }
            }
        }

        var unit = new ProductUnit
        {
            ProductId = productId,
            UnitName = request.UnitName.Trim(),
            ConversionRate = request.IsBaseUnit ? 1.00m : request.ConversionRate,
            Price = request.Price,
            Barcode = string.IsNullOrWhiteSpace(request.Barcode) ? null : request.Barcode.Trim(),
            IsBaseUnit = request.IsBaseUnit,
            IsPurchaseUnit = request.IsPurchaseUnit
        };

        await _unitRepository.AddAsync(unit, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return MapToResponse(unit);
    }

    public async Task<ProductUnitResponse> UpdateAsync(Guid id, ProductUnitUpdateRequest request, CancellationToken cancellationToken = default)
    {
        ValidateRequest(request.UnitName, request.ConversionRate, request.Price, request.Barcode);

        var unit = await _unitRepository.GetByIdAsync(id, cancellationToken);
        if (unit == null)
        {
            throw new KeyNotFoundException("Không tìm thấy đơn vị tính.");
        }

        var nameExists = await _unitRepository.ExistsByNameAsync(unit.ProductId, request.UnitName.Trim(), id, cancellationToken);
        if (nameExists)
        {
            throw new InvalidOperationException("Đơn vị tính này đã tồn tại cho sản phẩm.");
        }

        if (!string.IsNullOrWhiteSpace(request.Barcode))
        {
            var trimmedBarcode = request.Barcode.Trim();
            var barcodeExists = await _unitRepository.ExistsByBarcodeAsync(trimmedBarcode, id, cancellationToken);
            if (barcodeExists)
            {
                throw new InvalidOperationException($"Mã vạch '{trimmedBarcode}' đã tồn tại trong hệ thống.");
            }
        }

        // Xử lý cờ IsBaseUnit
        if (request.IsBaseUnit && !unit.IsBaseUnit)
        {
            var oldBaseUnit = await _unitRepository.GetBaseUnitByProductIdAsync(unit.ProductId, cancellationToken);
            if (oldBaseUnit != null && oldBaseUnit.Id != unit.Id)
            {
                oldBaseUnit.IsBaseUnit = false;
                _unitRepository.Update(oldBaseUnit);
            }

            var product = await _productRepository.GetByIdAsync(unit.ProductId, cancellationToken);
            if (product != null)
            {
                product.BaseUnit = request.UnitName.Trim();
                _productRepository.Update(product);
            }
        }
        else if (!request.IsBaseUnit && unit.IsBaseUnit)
        {
            var count = await _unitRepository.CountByProductIdAsync(unit.ProductId, cancellationToken);
            if (count <= 1)
            {
                throw new InvalidOperationException("Sản phẩm phải có ít nhất một đơn vị tính cơ bản.");
            }
        }

        if (request.IsPurchaseUnit && !unit.IsPurchaseUnit)
        {
            var existingUnits = await _unitRepository.GetByProductIdAsync(unit.ProductId, cancellationToken);
            if (existingUnits != null)
            {
                foreach (var existingUnit in existingUnits.Where(u => u.IsPurchaseUnit && u.Id != unit.Id))
                {
                    existingUnit.IsPurchaseUnit = false;
                    _unitRepository.Update(existingUnit);
                }
            }
        }

        unit.UnitName = request.UnitName.Trim();
        unit.ConversionRate = request.IsBaseUnit ? 1.00m : request.ConversionRate;
        unit.Price = request.Price;
        unit.Barcode = string.IsNullOrWhiteSpace(request.Barcode) ? null : request.Barcode.Trim();
        unit.IsBaseUnit = request.IsBaseUnit;
        unit.IsPurchaseUnit = request.IsPurchaseUnit;

        _unitRepository.Update(unit);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return MapToResponse(unit);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var unit = await _unitRepository.GetByIdAsync(id, cancellationToken);
        if (unit == null)
        {
            throw new KeyNotFoundException("Không tìm thấy đơn vị tính.");
        }

        if (unit.IsBaseUnit)
        {
            var count = await _unitRepository.CountByProductIdAsync(unit.ProductId, cancellationToken);
            if (count <= 1)
            {
                throw new InvalidOperationException("Không thể xóa đơn vị tính cơ bản duy nhất của sản phẩm.");
            }
        }

        _unitRepository.Delete(unit);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static void ValidateRequest(string unitName, decimal conversionRate, decimal price, string? barcode = null)
    {
        if (string.IsNullOrWhiteSpace(unitName))
        {
            throw new ArgumentException("Tên đơn vị tính không được để trống.");
        }

        if (unitName.Trim().Length > 30)
        {
            throw new ArgumentException("Tên đơn vị tính không được vượt quá 30 ký tự.");
        }

        if (conversionRate <= 0)
        {
            throw new ArgumentException("Tỷ lệ quy đổi phải lớn hơn 0.");
        }

        if (price < 0 || price > 1_000_000_000m)
        {
            throw new ArgumentException("Giá bán của đơn vị tính không được nhỏ hơn 0 hoặc vượt quá 1.000.000.000 đ.");
        }

        if (!string.IsNullOrWhiteSpace(barcode))
        {
            var trimmedBarcode = barcode.Trim();
            if (trimmedBarcode.Length > 50 || trimmedBarcode.Any(char.IsWhiteSpace))
            {
                throw new ArgumentException("Mã vạch đơn vị tính không hợp lệ (tối đa 50 ký tự, không chứa khoảng trắng).");
            }
        }
    }

    private static ProductUnitResponse MapToResponse(ProductUnit unit)
    {
        return new ProductUnitResponse
        {
            Id = unit.Id,
            ProductId = unit.ProductId,
            UnitName = unit.UnitName,
            ConversionRate = unit.ConversionRate,
            Price = unit.Price,
            Barcode = unit.Barcode,
            IsBaseUnit = unit.IsBaseUnit,
            IsPurchaseUnit = unit.IsPurchaseUnit,
            CreatedAt = unit.CreatedAt,
            UpdatedAt = unit.UpdatedAt
        };
    }
}
