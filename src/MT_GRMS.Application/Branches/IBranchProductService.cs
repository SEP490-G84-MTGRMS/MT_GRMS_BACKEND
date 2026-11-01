using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MT_GRMS.Application.Branches.Dtos;
using MT_GRMS.Application.Common;

namespace MT_GRMS.Application.Branches;

public interface IBranchProductService
{
    /// <summary>
    /// Lấy danh sách sản phẩm thuộc chi nhánh theo bộ lọc (từ khóa, ngành hàng, cảnh báo tồn, trạng thái) và phân trang
    /// </summary>
    Task<PagedResult<BranchProductDto>> GetBranchProductsAsync(
        Guid branchId,
        BranchProductQueryFilter filter,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy danh sách chỉ số tổng quan của toàn bộ các chi nhánh (Tổng SKU, Tổng SOH, Số cảnh báo thiếu hàng)
    /// </summary>
    Task<IReadOnlyList<BranchSummaryDto>> GetBranchSummariesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gán một hoặc nhiều sản phẩm từ Master Catalog vào một chi nhánh
    /// </summary>
    Task<IReadOnlyList<BranchProductDto>> AssignProductsAsync(
        Guid branchId,
        AssignBranchProductsCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cập nhật giá bán lẻ niêm yết, định mức tồn kho và vị trí kệ hàng tại chi nhánh
    /// </summary>
    Task<BranchProductDto> UpdateProductAsync(
        Guid branchId,
        Guid productId,
        UpdateBranchProductCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Chuyển đổi trạng thái Active / Inactive của sản phẩm tại chi nhánh
    /// </summary>
    Task<BranchProductDto> ToggleStatusAsync(
        Guid branchId,
        Guid productId,
        string status,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gỡ sản phẩm khỏi danh mục kinh doanh của chi nhánh (chặn nếu tồn kho SOH > 0)
    /// </summary>
    Task RemoveProductAsync(
        Guid branchId,
        Guid productId,
        CancellationToken cancellationToken = default);
}
