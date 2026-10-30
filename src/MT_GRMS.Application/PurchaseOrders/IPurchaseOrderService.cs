using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.PurchaseOrders.Dtos;

namespace MT_GRMS.Application.PurchaseOrders;

public interface IPurchaseOrderService
{
    /// <summary>
    /// Lấy toàn bộ dữ liệu ma trận đặt hàng (Sản phẩm, tồn kho SOH, số lượng NV đề xuất & Store Owner ghi đè)
    /// </summary>
    Task<OrderMatrixResponse> GetOrderMatrixAsync(
        OrderMatrixQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lưu bản thảo kế hoạch đặt hàng ma trận (DRAFT PO)
    /// </summary>
    Task<PurchaseOrderDto> SaveMatrixDraftAsync(
        SaveMatrixDraftCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Chốt & Tách ma trận thành các đơn PO độc lập theo từng Ngày giao hàng và từng Nhà cung cấp (FE-05)
    /// </summary>
    Task<IReadOnlyList<PurchaseOrderDto>> SplitMatrixPoAsync(
        SplitMatrixPoCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy danh sách các đơn PO nhập kho theo bộ lọc và phân trang
    /// </summary>
    Task<PagedResult<PurchaseOrderDto>> GetPagedPurchaseOrdersAsync(
        PurchaseOrderFilterQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy thông tin chi tiết một đơn PO theo ID (kèm các dòng sản phẩm chi tiết)
    /// </summary>
    Task<PurchaseOrderDto> GetPurchaseOrderByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cập nhật trạng thái của đơn PO (DRAFT, PENDING_APPROVAL, APPROVED, DISPATCHED, CANCELLED)
    /// </summary>
    Task<PurchaseOrderDto> UpdatePurchaseOrderStatusAsync(
        Guid id,
        string status,
        string? reason = null,
        CancellationToken cancellationToken = default);
}
