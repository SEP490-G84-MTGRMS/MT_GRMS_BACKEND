using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.GoodsReceipts.Dtos;

namespace MT_GRMS.Application.GoodsReceipts;

public interface IGoodsReceiptService
{
    Task<PagedResult<GoodsReceiptDto>> GetGoodsReceiptsAsync(GoodsReceiptFilterQuery query, CancellationToken cancellationToken = default);
    Task<GoodsReceiptDto> GetGoodsReceiptByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<GoodsReceiptDto> CreateGoodsReceiptAsync(CreateGoodsReceiptCommand command, CancellationToken cancellationToken = default);
    Task<GoodsReceiptDto> CompleteGoodsReceiptAsync(Guid id, CompleteGoodsReceiptCommand command, CancellationToken cancellationToken = default);
    Task<GoodsReceiptDto> CancelGoodsReceiptAsync(Guid id, CancelGoodsReceiptCommand command, CancellationToken cancellationToken = default);
    Task<List<PurchaseOrderLookupDto>> GetPoLookupAsync(Guid? branchId, CancellationToken cancellationToken = default);
    Task<PoReceiptDetailsDto> GetPoReceiptDetailsAsync(Guid poId, CancellationToken cancellationToken = default);
}
