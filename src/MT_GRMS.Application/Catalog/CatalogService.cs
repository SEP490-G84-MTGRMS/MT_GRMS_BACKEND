using MT_GRMS.Application.Catalog.Dtos;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Entities.Catalog;

namespace MT_GRMS.Application.Catalog;

/// <summary>
/// KHUNG MẪU RỖNG CHO SERVICE TẦNG APPLICATION:
/// - Tầng Service là nơi duy nhất (cùng DbContext) được phép trỏ tới Entity (Product).
/// - Các hàm chỉ nhận Input là DTO và trả Output là DTO (tuyệt đối không trả Entity ra ngoài).
/// - Nhóm tự viết business logic chi tiết vào các hàm bên dưới.
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

    public Task<ProductResponse> CreateProductAsync(ProductCreateRequest request, CancellationToken cancellationToken = default)
    {
        // TODO: Nhóm tự viết logic ở đây (Map DTO -> Entity -> AddAsync -> SaveChangesAsync -> Map sang Response DTO)
        throw new NotImplementedException();
    }

    public Task<ProductResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // TODO: Nhóm tự viết logic ở đây (Query Entity -> Map sang Response DTO)
        throw new NotImplementedException();
    }

    public Task<IReadOnlyList<ProductResponse>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        // TODO: Nhóm tự viết logic ở đây (Query danh sách Entity -> Map sang List Response DTO)
        throw new NotImplementedException();
    }
}
