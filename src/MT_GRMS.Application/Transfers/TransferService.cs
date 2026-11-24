using MT_GRMS.Application.Common;
using MT_GRMS.Application.Inventory;
using MT_GRMS.Application.Notifications;
using MT_GRMS.Domain.Constants;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Application.Transfers;

public sealed class TransferService(
    IRepository<Transfer> transfers,
    IRepository<TransferDetail> details,
    IRepository<BranchStock> stocks,
    IRepository<Product> products,
    IRepository<Branch> branches,
    IRepository<TransferStatusHistory> statusHistory,
    ITenantContext tenant,
    IUnitOfWork unitOfWork,
    NotificationOutbox? notificationOutbox = null,
    IRepository<Package>? packages = null,
    IRepository<ProductUnit>? productUnits = null) : ITransferService
{
    private async Task<(decimal BaseQuantity, string NormalizedUnit)> NormalizeToBaseUnitAsync(
        Product product,
        decimal requestedQuantity,
        string requestedUnit,
        CancellationToken ct)
    {
        var trimmedUnit = requestedUnit.Trim();
        var baseUnit = string.IsNullOrWhiteSpace(product.BaseUnit) ? trimmedUnit : product.BaseUnit.Trim();
        if (string.Equals(trimmedUnit, baseUnit, StringComparison.OrdinalIgnoreCase) || productUnits == null)
        {
            return (requestedQuantity, baseUnit);
        }

        var units = await productUnits.FindAsync(
            u => u.TenantId == tenant.CurrentTenantId && u.ProductId == product.Id && !u.IsDeleted,
            ct);
        var matched = units.FirstOrDefault(u => string.Equals(u.UnitName?.Trim(), trimmedUnit, StringComparison.OrdinalIgnoreCase));
        if (matched == null || matched.ConversionRate <= 0)
        {
            throw new InvalidOperationException($"Đơn vị '{trimmedUnit}' không hợp lệ cho sản phẩm {product.Name} (đơn vị cơ sở: {baseUnit}).");
        }

        return (requestedQuantity * matched.ConversionRate, baseUnit);
    }
    private async Task<decimal> RawAvailableAsync(BranchStock stock, CancellationToken ct)
    {
        var active = packages == null ? 0m : (await packages.FindAsync(p => p.TenantId == tenant.CurrentTenantId
            && p.BranchStockId == stock.Id && !p.IsDeleted && (p.Status == "AVAILABLE" || p.Status == "QUARANTINED"), ct)).Sum(p => p.NetWeight);
        return BranchStockAvailability.GetRawSellableKg(stock, active);
    }
    private bool IsOwner => tenant.CurrentRole == SystemRoles.StoreOwner;
    private bool IsStaff => tenant.CurrentRole == SystemRoles.Staff;

    public async Task<PagedResult<TransferDto>> GetTransfersAsync(TransferFilterParams filter, CancellationToken ct)
    {
        EnsureTenantRole();
        var all = (await transfers.GetAllAsync(ct)).AsEnumerable();
        all = all.Where(x => string.Equals(x.TenantId, tenant.CurrentTenantId, StringComparison.Ordinal));
        if (!IsOwner)
        {
            if (IsStaff && tenant.CurrentBranchId is Guid branch) all = all.Where(x => x.FromBranchId == branch || x.ToBranchId == branch);
            else all = Enumerable.Empty<Transfer>();
        }
        if (filter.FromBranchId.HasValue) all = all.Where(x => x.FromBranchId == filter.FromBranchId);
        if (filter.ToBranchId.HasValue) all = all.Where(x => x.ToBranchId == filter.ToBranchId);
        var statuses = filter.Statuses is { Count: > 0 } ? filter.Statuses : filter.Status.HasValue ? [filter.Status.Value] : null;
        if (statuses is not null) all = all.Where(x => statuses.Contains(x.Status));
        var receiveDate = string.Equals(filter.DateField, "receive", StringComparison.OrdinalIgnoreCase);
        if (filter.FromDate.HasValue) all = all.Where(x => (receiveDate ? x.ReceivedAt : x.DispatchedAt ?? x.CreatedAt) >= filter.FromDate);
        if (filter.ToDate.HasValue) all = all.Where(x => (receiveDate ? x.ReceivedAt : x.DispatchedAt ?? x.CreatedAt) < filter.ToDate.Value.Date.AddDays(1));
        if (!string.IsNullOrWhiteSpace(filter.Search)) all = all.Where(x => x.TransferCode.Contains(filter.Search, StringComparison.OrdinalIgnoreCase));
        if (string.Equals(filter.ReceiveStatus, "match", StringComparison.OrdinalIgnoreCase)
            || string.Equals(filter.ReceiveStatus, "mismatch", StringComparison.OrdinalIgnoreCase))
        {
            var matchRequested = string.Equals(filter.ReceiveStatus, "match", StringComparison.OrdinalIgnoreCase);
            var receiptFiltered = new List<Transfer>();
            foreach (var transfer in all)
            {
                if (transfer.Status != TransferStatus.Completed) continue;
                var lines = transfer.Details.Count > 0 ? transfer.Details.ToList() : (await details.FindAsync(x => x.TransferId == transfer.Id, ct)).ToList();
                var isMatch = lines.All(line => line.ReceivedQuantity == line.SentQuantity && line.DamagedQuantity == 0);
                if (isMatch == matchRequested) receiptFiltered.Add(transfer);
            }
            all = receiptFiltered;
        }
        var matching = all.OrderByDescending(x => x.CreatedAt).ToList();
        var page = Math.Max(filter.Page, 1); var size = Math.Clamp(filter.PageSize, 1, 100);
        var pageSlice = matching.Skip((page - 1) * size).Take(size).ToList();
        var result = await MapBatchAsync(pageSlice, ct);
        return new PagedResult<TransferDto> { Items = result, Page = page, PageSize = size, TotalItems = matching.Count };
    }

    public async Task<TransferDto> GetTransferAsync(Guid id, CancellationToken ct) => await MapAsync(await RequireAsync(id, ct), ct);

    public async Task<IReadOnlyList<TransferStockBatchDto>> GetStockBatchesAsync(Guid branchId, CancellationToken ct)
    {
        await EnsureCanViewBranchStockAsync(branchId, ct);
        var stock = await stocks.FindAsync(x => x.TenantId == tenant.CurrentTenantId && x.BranchId == branchId && x.QuantityOnHand > 0, ct);
        if (stock.Count == 0) return [];

        var stockIds = stock.Select(s => s.Id).ToHashSet();
        var packageWeights = packages == null
            ? new Dictionary<Guid, decimal>()
            : (await packages.FindAsync(p => p.TenantId == tenant.CurrentTenantId
                && stockIds.Contains(p.BranchStockId)
                && !p.IsDeleted
                && (p.Status == "AVAILABLE" || p.Status == "QUARANTINED"), ct))
                .GroupBy(p => p.BranchStockId)
                .ToDictionary(g => g.Key, g => g.Sum(p => p.NetWeight));

        return stock
            .Select(item => new TransferStockBatchDto(
                item.ProductId,
                item.BatchCode,
                item.ExpiryDate,
                BranchStockAvailability.GetRawSellableKg(item, packageWeights.GetValueOrDefault(item.Id))))
            .Where(dto => dto.AvailableQuantity > 0)
            .ToList();
    }

    private async Task<BranchStock> ResolveStockSourceAsync(Guid branchId, Guid productId, string? batchCode, DateTime? expiryDate, CancellationToken ct, Guid? branchStockId = null)
    {
        if (branchStockId.HasValue)
        {
            var exact = await stocks.GetByIdAsync(branchStockId.Value, ct);
            if (exact == null || exact.IsDeleted || exact.TenantId != tenant.CurrentTenantId || exact.BranchId != branchId || exact.ProductId != productId)
                throw new InvalidOperationException("Dòng tồn đã chọn cho phiếu chuyển không còn hợp lệ.");
            return exact;
        }
        var branchStocks = await stocks.FindAsync(s => s.TenantId == tenant.CurrentTenantId && s.BranchId == branchId && s.ProductId == productId, ct);
        if (branchStocks.Count == 0)
            throw new InvalidOperationException("Không tìm thấy tồn kho cho sản phẩm này tại chi nhánh gửi.");

        var normalizedBatch = batchCode?.Trim();
        if (!string.IsNullOrWhiteSpace(normalizedBatch))
        {
            var batchMatches = branchStocks.Where(s => string.Equals(s.BatchCode?.Trim(), normalizedBatch, StringComparison.OrdinalIgnoreCase)).ToList();
            if (batchMatches.Count == 1) return batchMatches[0];
            if (batchMatches.Count > 1)
            {
                if (expiryDate.HasValue)
                {
                    var dateMatches = batchMatches.Where(s => s.ExpiryDate.HasValue && s.ExpiryDate.Value.Date == expiryDate.Value.Date).ToList();
                    if (dateMatches.Count == 1) return dateMatches[0];
                }
                throw new InvalidOperationException("Mã lô khớp nhiều dòng tồn; cần chọn đúng dòng tồn trước khi chuyển kho.");
            }
            throw new InvalidOperationException("Lô yêu cầu chuyển không còn tồn tại tại chi nhánh nguồn.");
        }

        if (branchStocks.Count == 1)
            return branchStocks[0];

        if (expiryDate.HasValue)
        {
            var dateMatches = branchStocks.Where(s => s.ExpiryDate.HasValue && s.ExpiryDate.Value.Date == expiryDate.Value.Date).ToList();
            if (dateMatches.Count > 0) return dateMatches.OrderByDescending(s => s.QuantityOnHand - s.QuantityReserved - s.QuantityQuarantined).First();
        }

        return branchStocks.OrderByDescending(s => s.QuantityOnHand - s.QuantityReserved - s.QuantityQuarantined).First();
    }

    public async Task<TransferDto> CreateTransferAsync(CreateTransferRequest request, CancellationToken ct)
    {
        EnsureOwner();
        if (request.FromBranchId == request.ToBranchId) throw new InvalidOperationException("Chi nhánh gửi và nhận phải khác nhau.");
        if (request.Items is null || request.Items.Count == 0 || request.Items.Any(x => x.Quantity <= 0 || x.UnitPrice < 0 || string.IsNullOrWhiteSpace(x.Unit) || x.Unit.Length > 30 || x.BatchCode?.Length > 50))
            throw new InvalidOperationException("Phiếu cần mặt hàng hợp lệ, số lượng dương, giá không âm và đơn vị đúng định dạng.");
        var from = await branches.GetByIdAsync(request.FromBranchId, ct) ?? throw new KeyNotFoundException("Không tìm thấy chi nhánh gửi.");
        var to = await branches.GetByIdAsync(request.ToBranchId, ct) ?? throw new KeyNotFoundException("Không tìm thấy chi nhánh nhận.");
        if (!string.Equals(from.TenantId, tenant.CurrentTenantId, StringComparison.Ordinal) || !string.Equals(to.TenantId, tenant.CurrentTenantId, StringComparison.Ordinal)) throw new InvalidOperationException("Hai chi nhánh phải thuộc cùng tenant hiện tại.");
        foreach (var item in request.Items)
        {
            var product = await products.GetByIdAsync(item.ProductId, ct) ?? throw new KeyNotFoundException("Không tìm thấy sản phẩm.");
            if (!string.Equals(product.TenantId, tenant.CurrentTenantId, StringComparison.Ordinal)) throw new InvalidOperationException("Sản phẩm không thuộc tenant hiện tại.");
        }
        if (!from.IsActive || !to.IsActive) throw new InvalidOperationException("Chi nhánh không hoạt động.");
        await unitOfWork.BeginTransactionAsync(ct);
        try
        {
            var transfer = new Transfer
            {
                TenantId = tenant.CurrentTenantId,
                TransferCode = $"TRF-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}"[..21].ToUpperInvariant(),
                FromBranchId = request.FromBranchId,
                ToBranchId = request.ToBranchId,
                Status = TransferStatus.Draft,
                Origin = request.Origin,
                Priority = request.Priority,
                RelatedOrderId = request.RelatedOrderId,
                ExpectedDeliveryDate = request.ExpectedDeliveryDate,
                Note = request.Note?.Trim(),
                CreatedBy = tenant.CurrentUserId,
                CreatedByStaff = tenant.CurrentRawUserId
            };
            var cumulativeRequestedByStock = new Dictionary<Guid, decimal>();
            foreach (var input in request.Items)
            {
                var product = await products.GetByIdAsync(input.ProductId, ct) ?? throw new KeyNotFoundException("Không tìm thấy sản phẩm.");
                var (baseQuantity, normalizedUnit) = await NormalizeToBaseUnitAsync(product, input.Quantity, input.Unit, ct);
                var stock = await ResolveStockSourceAsync(request.FromBranchId, input.ProductId, input.BatchCode, input.ExpiryDate, ct);
                var available = await RawAvailableAsync(stock, ct);
                var nextCumulative = cumulativeRequestedByStock.GetValueOrDefault(stock.Id) + baseQuantity;
                if (available < nextCumulative)
                    throw new InvalidOperationException($"Không đủ tồn khả dụng cho {product.Name}; hiện có {available}, tổng yêu cầu chuyển {nextCumulative}.");
                cumulativeRequestedByStock[stock.Id] = nextCumulative;
                var unitCost = stock.LandedCostPerUnit;
                transfer.Details.Add(new TransferDetail
                {
                    ProductId = input.ProductId,
                    SentQuantity = baseQuantity,
                    PackagingUnit = normalizedUnit,
                    BatchNumber = string.IsNullOrWhiteSpace(input.BatchCode) ? stock.BatchCode : input.BatchCode.Trim(),
                    BranchStockId = stock.Id,
                    ExpiryDate = input.ExpiryDate ?? stock.ExpiryDate,
                    UnitPrice = unitCost,
                    Amount = baseQuantity * unitCost
                });
            }
            transfer.TotalAmount = transfer.Details.Sum(x => x.Amount);
            await transfers.AddAsync(transfer, ct);
            foreach (var detail in transfer.Details) await details.AddAsync(detail, ct);
            await AddHistoryAsync(transfer, null, TransferStatus.Draft, "Created", request.Note, ct);
            await unitOfWork.SaveChangesAsync(ct); await unitOfWork.CommitTransactionAsync(ct);
            return await MapAsync(transfer, ct);
        }
        catch { await unitOfWork.RollbackTransactionAsync(ct); throw; }
    }

    public async Task<TransferDto> SubmitTransferAsync(Guid id, CancellationToken ct)
    {
        EnsureOwner();
        var transfer = await RequireAsync(id, ct);
        if (transfer.Status != TransferStatus.Draft)
            throw new InvalidOperationException("Chỉ có thể gửi phiếu đang ở trạng thái nháp.");
        var previous = transfer.Status;
        transfer.Status = TransferStatus.Pending;
        transfer.SubmittedAt = DateTime.UtcNow;
        transfer.SubmittedByUserId = tenant.CurrentUserId;
        transfers.Update(transfer);
        await AddHistoryAsync(transfer, previous, transfer.Status, "Submitted", null, ct);
        if (notificationOutbox != null)
            await notificationOutbox.EnqueueTransferAsync(transfer, BusinessNotificationTypes.TransferDispatchRequired,
                "Phiếu điều chuyển chờ xuất kho", transfer.FromBranchId, false, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return await MapAsync(transfer, ct);
    }

    public async Task<TransferDto> ReceiveTransferAsync(Guid id, ReceiveTransferRequest request, CancellationToken ct)
    {
        EnsureTenantRole();
        var transfer = await RequireAsync(id, ct); EnsureBranch(transfer.ToBranchId);
        if (transfer.Status != TransferStatus.InTransit) throw new InvalidOperationException("Chỉ nhận được phiếu đang vận chuyển.");
        if (request.Items is null) throw new InvalidOperationException("Thiếu thông tin kiểm đếm mặt hàng.");
        var lines = (transfer.Details.Count > 0 ? transfer.Details : (await details.FindAsync(x => x.TransferId == id, ct)).ToList()).ToDictionary(x => x.Id);
        if (request.Items.Count != lines.Count || request.Items.Select(x => x.DetailId).Distinct().Count() != lines.Count || request.Items.Any(x => !lines.ContainsKey(x.DetailId))) throw new InvalidOperationException("Cần đúng một kết quả nhận cho từng mặt hàng.");
        if (request.Items.Any(x => x.ReceivedQuantity < 0 || x.DamagedQuantity < 0 || x.ReceivedQuantity + x.DamagedQuantity > lines[x.DetailId].SentQuantity)) throw new InvalidOperationException("Số nhận/hỏng không hợp lệ hoặc vượt số xuất.");
        if (request.Items.Any(x => x.DamagedQuantity > 0 && string.IsNullOrWhiteSpace(x.DiscrepancyReason))) throw new InvalidOperationException("Cần nêu lý do hàng hỏng.");
        await unitOfWork.BeginTransactionAsync(ct);
        try
        {
            foreach (var input in request.Items)
            {
                var line = lines[input.DetailId]; line.ReceivedQuantity = input.ReceivedQuantity; line.DamagedQuantity = input.DamagedQuantity; line.DiscrepancyReason = input.DiscrepancyReason?.Trim();
                line.LineStatus = input.DamagedQuantity > 0 ? TransferLineStatus.Damaged : input.ReceivedQuantity == line.SentQuantity ? TransferLineStatus.Matched : TransferLineStatus.Deficit; details.Update(line);
                if (input.ReceivedQuantity + input.DamagedQuantity == 0) continue;
                var destStocks = await stocks.FindAsync(s => s.BranchId == transfer.ToBranchId && s.ProductId == line.ProductId, ct);
                BranchStock? destStock = null;
                var normalizedBatch = line.BatchNumber?.Trim();
                if (!string.IsNullOrWhiteSpace(normalizedBatch))
                {
                    destStock = destStocks.FirstOrDefault(s => string.Equals(s.BatchCode?.Trim(), normalizedBatch, StringComparison.OrdinalIgnoreCase));
                }
                else
                {
                    destStock = destStocks.FirstOrDefault(s => string.IsNullOrWhiteSpace(s.BatchCode))
                        ?? (destStocks.Count == 1 ? destStocks[0] : null);
                }

                var receivedTotal = input.ReceivedQuantity + input.DamagedQuantity;
                if (destStock == null)
                {
                    await stocks.AddAsync(new BranchStock
                    {
                        TenantId = tenant.CurrentTenantId,
                        BranchId = transfer.ToBranchId,
                        ProductId = line.ProductId,
                        BatchCode = line.BatchNumber,
                        ExpiryDate = line.ExpiryDate,
                        QuantityOnHand = receivedTotal,
                        QuantityQuarantined = input.DamagedQuantity,
                        LandedCostPerUnit = line.UnitPrice
                    }, ct);
                }
                else
                {
                    var prevQty = Math.Max(0m, destStock.QuantityOnHand);
                    var nextQty = prevQty + receivedTotal;
                    if (nextQty > 0 && line.UnitPrice > 0)
                    {
                        destStock.LandedCostPerUnit = Math.Round(
                            ((prevQty * destStock.LandedCostPerUnit) + (receivedTotal * line.UnitPrice)) / nextQty,
                            4,
                            MidpointRounding.AwayFromZero);
                    }
                    destStock.QuantityOnHand += receivedTotal;
                    destStock.QuantityQuarantined += input.DamagedQuantity;
                    stocks.Update(destStock);
                }
            }
            var hasDiscrepancy = request.Items.Any(input =>
                input.ReceivedQuantity != lines[input.DetailId].SentQuantity || input.DamagedQuantity > 0);
            var previous = transfer.Status;
            transfer.Status = TransferStatus.Completed;
            transfer.CompletionResult = hasDiscrepancy
                ? TransferCompletionResult.ReceivedWithDiscrepancy
                : TransferCompletionResult.Received;
            transfer.HasDiscrepancy = hasDiscrepancy;
            transfer.ReceivedAt = DateTime.UtcNow;
            transfer.ReceivedByUserId = tenant.CurrentUserId;
            transfer.DiscrepancyNote = request.Note?.Trim();
            transfers.Update(transfer);
            await AddHistoryAsync(transfer, previous, transfer.Status, "Received", request.Note, ct);
            if (hasDiscrepancy && notificationOutbox != null)
                await notificationOutbox.EnqueueTransferAsync(transfer, BusinessNotificationTypes.TransferReceiptDiscrepancy,
                    "Phiếu điều chuyển nhận có chênh lệch", transfer.FromBranchId, true, ct);
            await unitOfWork.SaveChangesAsync(ct); await unitOfWork.CommitTransactionAsync(ct); return await MapAsync(transfer, ct);
        }
        catch { await unitOfWork.RollbackTransactionAsync(ct); throw; }
    }

    public async Task<TransferDto> DispatchTransferAsync(Guid id, CancellationToken ct)
    {
        EnsureTenantRole();
        var transfer = await RequireAsync(id, ct);
        EnsureBranch(transfer.FromBranchId);
        if (transfer.Status != TransferStatus.Pending) throw new InvalidOperationException("Chỉ có thể xuất phiếu đang chờ chi nhánh nguồn xử lý.");
        var lines = transfer.Details.Count > 0 ? transfer.Details.ToList() : (await details.FindAsync(x => x.TransferId == id, ct)).ToList();
        await unitOfWork.BeginTransactionAsync(ct);
        try
        {
            foreach (var line in lines)
            {
                var stock = await ResolveStockSourceAsync(transfer.FromBranchId, line.ProductId, line.BatchNumber, line.ExpiryDate, ct, line.BranchStockId);
                if (transfer.IsStockReserved)
                {
                    if (stock.QuantityReserved < line.SentQuantity)
                        throw new InvalidOperationException("Dữ liệu giữ tồn của phiếu không còn hợp lệ; không thể xuất kho.");
                    stock.QuantityReserved -= line.SentQuantity;
                }
                var available = await RawAvailableAsync(stock, ct);
                if (available < line.SentQuantity) throw new InvalidOperationException($"Không đủ tồn khả dụng; còn {available}, yêu cầu chuyển {line.SentQuantity}.");
                stock.QuantityOnHand -= line.SentQuantity;
                stocks.Update(stock);

                if (string.IsNullOrWhiteSpace(line.BatchNumber) && !string.IsNullOrWhiteSpace(stock.BatchCode))
                {
                    line.BatchNumber = stock.BatchCode;
                    line.ExpiryDate ??= stock.ExpiryDate;
                    details.Update(line);
                }
            }
            var previous = transfer.Status;
            transfer.Status = TransferStatus.InTransit;
            transfer.SourceConfirmedAt = DateTime.UtcNow;
            transfer.SourceConfirmedByUserId = tenant.CurrentUserId;
            transfer.DispatchedAt = DateTime.UtcNow;
            transfer.DispatchedByUserId = tenant.CurrentUserId;
            transfer.IsStockReserved = false;
            transfers.Update(transfer);
            await AddHistoryAsync(transfer, previous, transfer.Status, "Dispatched", null, ct);
            if (notificationOutbox != null)
                await notificationOutbox.EnqueueTransferAsync(transfer, BusinessNotificationTypes.TransferReceiveRequired,
                    "Phiếu điều chuyển chờ nhận hàng", transfer.ToBranchId, false, ct);
            await unitOfWork.SaveChangesAsync(ct);
            await unitOfWork.CommitTransactionAsync(ct);
            return await MapAsync(transfer, ct);
        }
        catch { await unitOfWork.RollbackTransactionAsync(ct); throw; }
    }

    public async Task<TransferDto> CancelTransferAsync(Guid id, CancelTransferRequest request, CancellationToken ct)
    {
        EnsureTenantRole();
        if (string.IsNullOrWhiteSpace(request.Reason)) throw new InvalidOperationException("Cần nhập lý do hủy.");
        var transfer = await RequireAsync(id, ct);
        if (transfer.Status is not (TransferStatus.Draft or TransferStatus.Pending))
            throw new InvalidOperationException("Chỉ có thể hủy phiếu trước khi xuất hàng.");
        var isCreator = transfer.CreatedBy == tenant.CurrentUserId;
        var isSourceEmployee = IsStaff && tenant.CurrentBranchId == transfer.FromBranchId;
        if (!IsOwner && !isCreator && !isSourceEmployee)
            throw new UnauthorizedAccessException("Không có quyền hủy hoặc từ chối phiếu này.");
        await unitOfWork.BeginTransactionAsync(ct);
        try
        {
            if (transfer.IsStockReserved)
            {
                var lines = transfer.Details.Count > 0 ? transfer.Details : (await details.FindAsync(x => x.TransferId == id, ct)).ToList();
                foreach (var line in lines)
                {
                    var stock = await ResolveStockSourceAsync(transfer.FromBranchId, line.ProductId, line.BatchNumber, line.ExpiryDate, ct, line.BranchStockId);
                    if (stock.QuantityReserved < line.SentQuantity)
                        throw new InvalidOperationException("Dữ liệu giữ tồn của phiếu không còn hợp lệ; không thể hủy phiếu.");
                    stock.QuantityReserved -= line.SentQuantity;
                    stocks.Update(stock);
                }
                transfer.IsStockReserved = false;
            }
            var previous = transfer.Status;
            transfer.Status = TransferStatus.Cancelled;
            transfer.CancelReason = request.Reason.Trim();
            transfer.CancelledAt = DateTime.UtcNow;
            transfer.CancelledByUserId = tenant.CurrentUserId;
            transfers.Update(transfer);
            await AddHistoryAsync(transfer, previous, transfer.Status, "Cancelled", request.Reason, ct);
            await unitOfWork.SaveChangesAsync(ct); await unitOfWork.CommitTransactionAsync(ct); return await MapAsync(transfer, ct);
        }
        catch { await unitOfWork.RollbackTransactionAsync(ct); throw; }
    }

    public async Task<TransferDto> ReturnTransferAsync(Guid id, ReturnTransferRequest request, CancellationToken ct)
    {
        EnsureTenantRole();
        if (string.IsNullOrWhiteSpace(request.Reason)) throw new InvalidOperationException("Cần nhập lý do hoàn trả.");
        var transfer = await RequireAsync(id, ct);
        EnsureBranch(transfer.FromBranchId);
        if (transfer.Status != TransferStatus.InTransit)
            throw new InvalidOperationException("Chỉ có thể xác nhận hoàn trả phiếu đang vận chuyển.");
        var lines = transfer.Details.Count > 0 ? transfer.Details : (await details.FindAsync(x => x.TransferId == id, ct)).ToList();
        await unitOfWork.BeginTransactionAsync(ct);
        try
        {
            foreach (var line in lines)
            {
                var stock = await ResolveStockSourceAsync(transfer.FromBranchId, line.ProductId, line.BatchNumber, line.ExpiryDate, ct, line.BranchStockId);
                stock.QuantityOnHand += line.SentQuantity;
                stocks.Update(stock);
            }
            var previous = transfer.Status;
            transfer.Status = TransferStatus.Completed;
            transfer.CompletionResult = TransferCompletionResult.Returned;
            transfer.ReceivedAt = DateTime.UtcNow;
            transfer.ReceivedByUserId = tenant.CurrentUserId;
            transfer.DiscrepancyNote = request.Reason.Trim();
            transfers.Update(transfer);
            await AddHistoryAsync(transfer, previous, transfer.Status, "ReturnedToSource", request.Reason, ct);
            await unitOfWork.SaveChangesAsync(ct);
            await unitOfWork.CommitTransactionAsync(ct);
            return await MapAsync(transfer, ct);
        }
        catch { await unitOfWork.RollbackTransactionAsync(ct); throw; }
    }

    private async Task<Transfer> RequireAsync(Guid id, CancellationToken ct)
    {
        var transfer = await transfers.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException("Không tìm thấy phiếu điều chuyển.");
        if (string.IsNullOrWhiteSpace(tenant.CurrentTenantId) || !string.Equals(transfer.TenantId, tenant.CurrentTenantId, StringComparison.Ordinal)) throw new KeyNotFoundException("Không tìm thấy phiếu điều chuyển.");
        if (!IsOwner && (!IsStaff || transfer.FromBranchId != tenant.CurrentBranchId && transfer.ToBranchId != tenant.CurrentBranchId)) throw new UnauthorizedAccessException("Không có quyền truy cập phiếu.");
        return transfer;
    }
    private void EnsureBranch(Guid branchId) { if (!IsOwner && (!IsStaff || tenant.CurrentBranchId is not Guid currentBranch || currentBranch != branchId)) throw new UnauthorizedAccessException("Không có quyền thao tác tại chi nhánh này."); }
    private void EnsureTenantRole()
    {
        if (string.IsNullOrWhiteSpace(tenant.CurrentTenantId) || !IsOwner && !IsStaff)
            throw new UnauthorizedAccessException("Role không có quyền truy cập phân hệ điều chuyển.");
        if (IsStaff && tenant.CurrentBranchId is not Guid)
            throw new UnauthorizedAccessException("Staff chưa có chi nhánh được phân công.");
    }

    private void EnsureOwner()
    {
        EnsureTenantRole();
        if (!IsOwner)
            throw new UnauthorizedAccessException("Chỉ chủ cửa hàng được tạo và gửi phiếu điều chuyển trực tiếp.");
    }

    private async Task EnsureCanViewBranchStockAsync(Guid branchId, CancellationToken ct)
    {
        EnsureTenantRole();
        var branch = await branches.GetByIdAsync(branchId, ct);
        if (branch is null || branch.TenantId != tenant.CurrentTenantId || !branch.IsActive)
            throw new KeyNotFoundException("Không tìm thấy chi nhánh nguồn hợp lệ.");
        if (IsStaff && branchId == tenant.CurrentBranchId)
            throw new InvalidOperationException("Chi nhánh nguồn phải khác chi nhánh hiện tại.");
    }
    private Task AddHistoryAsync(Transfer transfer, TransferStatus? from, TransferStatus to, string action, string? reason, CancellationToken ct)
        => statusHistory.AddAsync(new TransferStatusHistory
        {
            TenantId = tenant.CurrentTenantId,
            TransferId = transfer.Id,
            FromStatus = from,
            ToStatus = to,
            ActorUserId = tenant.CurrentUserId,
            ActorBranchId = tenant.CurrentBranchId,
            ActorRole = tenant.CurrentRole?.Trim() ?? string.Empty,
            Action = action,
            Reason = reason?.Trim(),
            CreatedBy = tenant.CurrentUserId
        }, ct);

    private async Task<List<TransferDto>> MapBatchAsync(IReadOnlyList<Transfer> pageTransfers, CancellationToken ct)
    {
        if (pageTransfers.Count == 0) return [];
        var canViewCost = IsOwner;
        var transferIds = pageTransfers.Select(t => t.Id).ToHashSet();
        var branchIds = pageTransfers.SelectMany(t => new[] { t.FromBranchId, t.ToBranchId }).ToHashSet();

        var branchMap = (await branches.FindAsync(b => branchIds.Contains(b.Id), ct))
            .ToDictionary(b => b.Id, b => b.Name);
        var missingDetailsIds = pageTransfers.Where(t => t.Details.Count == 0).Select(t => t.Id).ToHashSet();
        var fetchedDetails = missingDetailsIds.Count == 0
            ? new Dictionary<Guid, List<TransferDetail>>()
            : (await details.FindAsync(d => missingDetailsIds.Contains(d.TransferId), ct))
                .GroupBy(d => d.TransferId)
                .ToDictionary(g => g.Key, g => g.ToList());
        var allProductIds = pageTransfers
            .SelectMany(t => t.Details.Count > 0 ? t.Details : (fetchedDetails.GetValueOrDefault(t.Id) ?? []))
            .Select(d => d.ProductId)
            .ToHashSet();
        var productMap = allProductIds.Count == 0
            ? new Dictionary<Guid, Product>()
            : (await products.FindAsync(p => allProductIds.Contains(p.Id), ct))
                .ToDictionary(p => p.Id);
        var historyMap = (await statusHistory.FindAsync(h => transferIds.Contains(h.TransferId), ct))
            .GroupBy(h => h.TransferId)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.CreatedAt)
                .Select(x => new TransferStatusHistoryDto(x.Id, x.FromStatus?.ToString(), x.ToStatus.ToString(), x.Action, x.ActorUserId, x.ActorBranchId, x.ActorRole, x.Reason, x.CreatedAt))
                .ToList());

        var dtos = new List<TransferDto>(pageTransfers.Count);
        foreach (var transfer in pageTransfers)
        {
            var lines = transfer.Details.Count > 0 ? transfer.Details : (fetchedDetails.GetValueOrDefault(transfer.Id) ?? []);
            var items = lines.Select(line =>
            {
                productMap.TryGetValue(line.ProductId, out var product);
                return new TransferLineDto(line.Id, line.ProductId, product?.ProductCode ?? "", product?.Name ?? "", line.PackagingUnit, line.SentQuantity, line.ReceivedQuantity, line.DamagedQuantity, line.BatchNumber, line.ExpiryDate, canViewCost ? line.UnitPrice : 0, canViewCost ? line.Amount : 0, line.DiscrepancyReason);
            }).ToList();
            dtos.Add(new TransferDto(
                transfer.Id, transfer.TransferCode,
                transfer.FromBranchId, branchMap.GetValueOrDefault(transfer.FromBranchId, ""),
                transfer.ToBranchId, branchMap.GetValueOrDefault(transfer.ToBranchId, ""),
                transfer.Status.ToString(), transfer.CompletionResult?.ToString(), transfer.Origin.ToString(), transfer.Priority.ToString(),
                transfer.CreatedAt, transfer.SubmittedAt, transfer.DispatchedAt, transfer.ReceivedAt, transfer.ExpectedDeliveryDate,
                transfer.CreatedByStaff, canViewCost ? transfer.TotalAmount : 0, transfer.HasDiscrepancy,
                transfer.Note, transfer.DiscrepancyNote, transfer.CancelReason, items,
                historyMap.GetValueOrDefault(transfer.Id, [])));
        }
        return dtos;
    }

    private async Task<TransferDto> MapAsync(Transfer transfer, CancellationToken ct)
    {
        var canViewCost = IsOwner;
        var from = await branches.GetByIdAsync(transfer.FromBranchId, ct); var to = await branches.GetByIdAsync(transfer.ToBranchId, ct); var items = new List<TransferLineDto>();
        foreach (var line in (transfer.Details.Count > 0 ? transfer.Details : (await details.FindAsync(x => x.TransferId == transfer.Id, ct)).ToList()))
        {
            var product = await products.GetByIdAsync(line.ProductId, ct);
            items.Add(new TransferLineDto(line.Id, line.ProductId, product?.ProductCode ?? "", product?.Name ?? "", line.PackagingUnit, line.SentQuantity, line.ReceivedQuantity, line.DamagedQuantity, line.BatchNumber, line.ExpiryDate, canViewCost ? line.UnitPrice : 0, canViewCost ? line.Amount : 0, line.DiscrepancyReason));
        }
        var history = (await statusHistory.FindAsync(x => x.TransferId == transfer.Id, ct))
            .OrderBy(x => x.CreatedAt)
            .Select(x => new TransferStatusHistoryDto(x.Id, x.FromStatus?.ToString(), x.ToStatus.ToString(), x.Action, x.ActorUserId, x.ActorBranchId, x.ActorRole, x.Reason, x.CreatedAt))
            .ToList();
        return new TransferDto(transfer.Id, transfer.TransferCode, transfer.FromBranchId, from?.Name ?? "", transfer.ToBranchId, to?.Name ?? "", transfer.Status.ToString(), transfer.CompletionResult?.ToString(), transfer.Origin.ToString(), transfer.Priority.ToString(), transfer.CreatedAt, transfer.SubmittedAt, transfer.DispatchedAt, transfer.ReceivedAt, transfer.ExpectedDeliveryDate, transfer.CreatedByStaff, canViewCost ? transfer.TotalAmount : 0, transfer.HasDiscrepancy, transfer.Note, transfer.DiscrepancyNote, transfer.CancelReason, items, history);
    }
}
