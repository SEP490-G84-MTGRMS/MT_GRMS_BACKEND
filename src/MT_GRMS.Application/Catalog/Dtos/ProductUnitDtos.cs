namespace MT_GRMS.Application.Catalog.Dtos;

/// <summary>
/// DTO phản hồi thông tin đơn vị tính quy đổi của sản phẩm
/// </summary>
public class ProductUnitResponse
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public string UnitName { get; set; } = string.Empty;
    public decimal ConversionRate { get; set; }
    public decimal Price { get; set; }
    public string? Barcode { get; set; }
    public bool IsBaseUnit { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// DTO yêu cầu tạo mới đơn vị tính quy đổi cho sản phẩm
/// </summary>
public class ProductUnitCreateRequest
{
    public string UnitName { get; set; } = string.Empty;
    public decimal ConversionRate { get; set; } = 1.00m;
    public decimal Price { get; set; } = 0.00m;
    public string? Barcode { get; set; }
    public bool IsBaseUnit { get; set; } = false;
}

/// <summary>
/// DTO yêu cầu cập nhật thông tin đơn vị tính quy đổi
/// </summary>
public class ProductUnitUpdateRequest
{
    public string UnitName { get; set; } = string.Empty;
    public decimal ConversionRate { get; set; } = 1.00m;
    public decimal Price { get; set; } = 0.00m;
    public string? Barcode { get; set; }
    public bool IsBaseUnit { get; set; } = false;
}
