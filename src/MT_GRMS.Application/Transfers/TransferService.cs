using MT_GRMS.Application.Auth;
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
    IRepository<ProductUnit>? productUnits = null,
    IRepository<TransferRequest>? transferRequests = null,
    IRepository<TransferRequestDetail>? transferRequestDetails = null,
    ITransferRepository? transferRepository = null,
    IRepository<BranchProduct>? branchProducts = null,
    IUserRepository? userRepository = null) : ITransferService
{
    private static DateTime TodayVnDate => DateTime.UtcNow.AddHours(7).Date;

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

        return (Math.Round(requestedQuantity * matched.ConversionRate, 3), baseUnit);
    }
    private async Task<decimal> RawAvailableAsync(BranchStock stock, CancellationToken ct)
    {
        var active = packages == null ? 0m : (await packages.FindAsync(p => p.TenantId == tenant.CurrentTenantId
            && p.BranchStockId == stock.Id && !p.IsDeleted && (p.Status == "AVAILABLE" || p.Status == "QUARANTINED"), ct)).Sum(p => p.NetWeight);
        return BranchStockAvailability.GetRawSellableKg(stock, active, TodayVnDate);
    }

    private bool IsOwner => tenant.CurrentRole == SystemRoles.StoreOwner;
    private bool IsStaff => tenant.CurrentRole == SystemRoles.Staff;

    public async Task<PagedResult<TransferDto>> GetTransfersAsync(TransferFilterParams filter, CancellationToken ct)
    {
        EnsureTenantRole();
        var page = Math.Max(filter.Page, 1);
        var size = Math.Clamp(filter.PageSize, 1, 100);

        Guid? restrictToBranchId = null;
        if (!IsOwner)
        {
            if (IsStaff && tenant.CurrentBranchId is Guid branch)
            {
                restrictToBranchId = branch;
            }
            else
            {
                return new PagedResult<TransferDto> { Items = [], Page = page, PageSize = size, TotalItems = 0 };
            }
        }

        if (transferRepository != null)
        {
            var (pageSlice, totalItems) = await transferRepository.GetPagedAsync(
                tenant.CurrentTenantId,
                restrictToBranchId,
                filter,
                ct);
            var mapped = await MapBatchAsync(pageSlice, ct);
            return new PagedResult<TransferDto>
            {
                Items = mapped,
                Page = page,
                PageSize = size,
                TotalItems = totalItems
            };
        }

        // Fallback when ITransferRepository is not injected (unit tests with in-memory fakes)
        var all = (await transfers.GetAllAsync(ct)).AsEnumerable();
        all = all.Where(x => string.Equals(x.TenantId, tenant.CurrentTenantId, StringComparison.Ordinal));
        if (restrictToBranchId.HasValue)
        {
            var branch = restrictToBranchId.Value;
            all = all.Where(x => x.FromBranchId == branch || x.ToBranchId == branch);
        }
        if (filter.FromBranchId.HasValue) all = all.Where(x => x.FromBranchId == filter.FromBranchId);
        if (filter.ToBranchId.HasValue) all = all.Where(x => x.ToBranchId == filter.ToBranchId);
        var statuses = filter.Statuses is { Count: > 0 } ? filter.Statuses : filter.Status.HasValue ? [filter.Status.Value] : null;
        if (statuses is not null) all = all.Where(x => statuses.Contains(x.Status));
        if (filter.CompletionResult.HasValue) all = all.Where(x => x.CompletionResult == filter.CompletionResult.Value);
        var receiveDate = string.Equals(filter.DateField, "receive", StringComparison.OrdinalIgnoreCase);
        if (filter.FromDate.HasValue) all = all.Where(x => (receiveDate ? x.ReceivedAt : x.DispatchedAt ?? x.CreatedAt) >= filter.FromDate);
        if (filter.ToDate.HasValue) all = all.Where(x => (receiveDate ? x.ReceivedAt : x.DispatchedAt ?? x.CreatedAt) < filter.ToDate.Value.Date.AddDays(1));
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var q = filter.Search.Trim();
            all = all.Where(x =>
                x.TransferCode.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(x.Note) && x.Note.Contains(q, StringComparison.OrdinalIgnoreCase)));
        }
        if (string.Equals(filter.ReceiveStatus, "match", StringComparison.OrdinalIgnoreCase)
            || string.Equals(filter.ReceiveStatus, "mismatch", StringComparison.OrdinalIgnoreCase))
        {
            var matchRequested = string.Equals(filter.ReceiveStatus, "match", StringComparison.OrdinalIgnoreCase);
            var receiptFiltered = new List<Transfer>();
            foreach (var transfer in all)
            {
                if (transfer.Status != TransferStatus.Completed || transfer.CompletionResult == TransferCompletionResult.Returned) continue;
                var lines = transfer.Details.Count > 0 ? transfer.Details.ToList() : (await details.FindAsync(x => x.TransferId == transfer.Id, ct)).ToList();
                var isMatch = lines.All(line => line.ReceivedQuantity == line.SentQuantity && line.DamagedQuantity == 0);
                if (isMatch == matchRequested) receiptFiltered.Add(transfer);
            }
            all = receiptFiltered;
        }
        var matching = all.OrderByDescending(x => x.CreatedAt).ToList();
        var fallbackSlice = matching.Skip((page - 1) * size).Take(size).ToList();
        var result = await MapBatchAsync(fallbackSlice, ct);
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
                BranchStockAvailability.GetRawSellableKg(item, packageWeights.GetValueOrDefault(item.Id), TodayVnDate)))
            .Where(dto => dto.AvailableQuantity > 0)
            .ToList();
    }

    private async Task<BranchStock> ResolveStockSourceAsync(
        Guid branchId,
        Guid productId,
        string? batchCode,
        DateTime? expiryDate,
        CancellationToken ct,
        Guid? branchStockId = null,
        bool forUpdate = false)
    {
        if (branchStockId.HasValue)
        {
            var exact = forUpdate && transferRepository != null
                ? await transferRepository.GetBranchStockByIdForUpdateAsync(branchStockId.Value, ct)
                : await stocks.GetByIdAsync(branchStockId.Value, ct);
            if (exact == null || exact.IsDeleted || exact.TenantId != tenant.CurrentTenantId || exact.BranchId != branchId || exact.ProductId != productId)
                throw new InvalidOperationException("Dòng tồn đã chọn cho phiếu chuyển không còn hợp lệ.");
            return exact;
        }

        var branchStocks = forUpdate && transferRepository != null
            ? await transferRepository.GetBranchStocksForUpdateAsync(tenant.CurrentTenantId, branchId, productId, ct)
            : await stocks.FindAsync(s => s.TenantId == tenant.CurrentTenantId && s.BranchId == branchId && s.ProductId == productId, ct);
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
            var now = DateTime.UtcNow;
            var todayVn = TodayVnDate;
            var submitImmediately = request.SubmitImmediately;

            var transfer = new Transfer
            {
                TenantId = tenant.CurrentTenantId,
                TransferCode = $"TRF-{now:yyyyMMdd}-{Guid.NewGuid():N}"[..21].ToUpperInvariant(),
                FromBranchId = request.FromBranchId,
                ToBranchId = request.ToBranchId,
                Status = submitImmediately ? TransferStatus.Pending : TransferStatus.Draft,
                SubmittedAt = submitImmediately ? now : null,
                SubmittedByUserId = submitImmediately ? tenant.CurrentUserId : null,
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
                var stock = await ResolveStockSourceAsync(request.FromBranchId, input.ProductId, input.BatchCode, input.ExpiryDate, ct, forUpdate: true);
                if ((stock.ExpiryDate.HasValue && stock.ExpiryDate.Value.Date < todayVn)
                    || (input.ExpiryDate.HasValue && input.ExpiryDate.Value.Date < todayVn))
                    throw new InvalidOperationException("Không thể điều chuyển lô hàng đã hết hạn sử dụng.");
                var effectiveExpiry = input.ExpiryDate ?? stock.ExpiryDate;
                var available = await RawAvailableAsync(stock, ct);
                var nextCumulative = cumulativeRequestedByStock.GetValueOrDefault(stock.Id) + baseQuantity;
                if (available < nextCumulative)
                    throw new InvalidOperationException($"Không đủ tồn khả dụng cho {product.Name}; hiện có {available} {normalizedUnit}, tổng yêu cầu chuyển {nextCumulative} {normalizedUnit}.");
                cumulativeRequestedByStock[stock.Id] = nextCumulative;
                var unitCost = stock.LandedCostPerUnit;
                transfer.Details.Add(new TransferDetail
                {
                    ProductId = input.ProductId,
                    SentQuantity = baseQuantity,
                    PackagingUnit = normalizedUnit,
                    BatchNumber = string.IsNullOrWhiteSpace(input.BatchCode) ? stock.BatchCode : input.BatchCode.Trim(),
                    BranchStockId = stock.Id,
                    ExpiryDate = effectiveExpiry,
                    UnitPrice = unitCost,
                    Amount = baseQuantity * unitCost
                });
            }
            transfer.TotalAmount = transfer.Details.Sum(x => x.Amount);
            await transfers.AddAsync(transfer, ct);
            foreach (var detail in transfer.Details) await details.AddAsync(detail, ct);
            await AddHistoryAsync(transfer, null, TransferStatus.Draft, "Created", request.Note, ct);
            if (submitImmediately)
            {
                await AddHistoryAsync(transfer, TransferStatus.Draft, TransferStatus.Pending, "Submitted", null, ct);
                if (notificationOutbox != null)
                    await notificationOutbox.EnqueueTransferAsync(transfer, BusinessNotificationTypes.TransferDispatchRequired,
                        "Phiếu điều chuyển chờ xuất kho", transfer.FromBranchId, false, ct);
            }
            await unitOfWork.SaveChangesAsync(ct);
            await unitOfWork.CommitTransactionAsync(ct);
            return await MapAsync(transfer, ct);
        }
        catch { await unitOfWork.RollbackTransactionAsync(ct); throw; }
    }

    public async Task<TransferDto> SubmitTransferAsync(Guid id, CancellationToken ct)
    {
        EnsureOwner();
        await unitOfWork.BeginTransactionAsync(ct);
        try
        {
            var transfer = await RequireForUpdateAsync(id, ct);
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
            await unitOfWork.CommitTransactionAsync(ct);
            return await MapAsync(transfer, ct);
        }
        catch { await unitOfWork.RollbackTransactionAsync(ct); throw; }
    }

    public async Task<TransferDto> ReceiveTransferAsync(Guid id, ReceiveTransferRequest request, CancellationToken ct)
    {
        EnsureTenantRole();
        if (request.Items is null) throw new InvalidOperationException("Thiếu thông tin kiểm đếm mặt hàng.");

        await unitOfWork.BeginTransactionAsync(ct);
        try
        {
            var transfer = await RequireForUpdateAsync(id, ct);
            EnsureBranch(transfer.ToBranchId);
            if (transfer.Status != TransferStatus.InTransit) throw new InvalidOperationException("Chỉ nhận được phiếu đang vận chuyển.");
            var lines = (transfer.Details.Count > 0 ? transfer.Details : (await details.FindAsync(x => x.TransferId == id, ct)).ToList()).ToDictionary(x => x.Id);
            if (request.Items.Count != lines.Count || request.Items.Select(x => x.DetailId).Distinct().Count() != lines.Count || request.Items.Any(x => !lines.ContainsKey(x.DetailId)))
                throw new InvalidOperationException("Cần đúng một kết quả nhận cho từng mặt hàng.");
            if (request.Items.Any(x => x.ReceivedQuantity < 0 || x.DamagedQuantity < 0 || x.ReceivedQuantity + x.DamagedQuantity > lines[x.DetailId].SentQuantity))
                throw new InvalidOperationException("Số nhận/hỏng không hợp lệ hoặc vượt số xuất.");
            if (request.Items.Any(x => x.DamagedQuantity > 0 && string.IsNullOrWhiteSpace(x.DiscrepancyReason)))
                throw new InvalidOperationException("Cần nêu lý do hàng hỏng.");
            if (request.Items.Any(x =>
                x.ReceivedQuantity + x.DamagedQuantity < lines[x.DetailId].SentQuantity
                && string.IsNullOrWhiteSpace(x.DiscrepancyReason)
                && string.IsNullOrWhiteSpace(request.Note)))
                throw new InvalidOperationException("Cần nêu lý do chênh lệch khi số lượng nhận thiếu so với số lượng xuất.");

            var createdDestStocks = new Dictionary<(Guid ProductId, string BatchKey, DateTime? ExpDate, Guid? SupplierId), BranchStock>();
            var receivedProductIds = new HashSet<Guid>();

            foreach (var input in request.Items)
            {
                var line = lines[input.DetailId];
                line.ReceivedQuantity = input.ReceivedQuantity;
                line.DamagedQuantity = input.DamagedQuantity;
                line.DiscrepancyReason = !string.IsNullOrWhiteSpace(input.DiscrepancyReason)
                    ? input.DiscrepancyReason.Trim()
                    : (input.ReceivedQuantity + input.DamagedQuantity < line.SentQuantity ? request.Note?.Trim() : null);
                line.LineStatus = input.DamagedQuantity > 0
                    ? TransferLineStatus.Damaged
                    : input.ReceivedQuantity == line.SentQuantity
                        ? TransferLineStatus.Matched
                        : TransferLineStatus.Deficit;
                details.Update(line);

                var receivedTotal = input.ReceivedQuantity + input.DamagedQuantity;
                if (receivedTotal == 0) continue;

                receivedProductIds.Add(line.ProductId);

                // Preserve SupplierId & ManufactureDate from source BranchStock when transferring across branches (TRF-01)
                BranchStock? sourceStock = line.BranchStockId.HasValue
                    ? await stocks.GetByIdAsync(line.BranchStockId.Value, ct)
                    : null;
                var sourceSupplierId = sourceStock?.SupplierId;
                var sourceMfgDate = sourceStock?.ManufactureDate;
                var effectiveExpiry = line.ExpiryDate ?? sourceStock?.ExpiryDate;
                var normalizedBatch = line.BatchNumber?.Trim();
                var batchKey = (normalizedBatch ?? string.Empty).ToUpperInvariant();
                var cacheKey = (line.ProductId, batchKey, effectiveExpiry?.Date, sourceSupplierId);

                var isNewlyCreatedStock = createdDestStocks.TryGetValue(cacheKey, out var destStock);
                if (!isNewlyCreatedStock)
                {
                    var destStocks = transferRepository != null
                        ? await transferRepository.GetBranchStocksForUpdateAsync(tenant.CurrentTenantId, transfer.ToBranchId, line.ProductId, ct)
                        : await stocks.FindAsync(s => s.BranchId == transfer.ToBranchId && s.ProductId == line.ProductId && !s.IsDeleted, ct);

                    if (!string.IsNullOrWhiteSpace(normalizedBatch))
                    {
                        destStock = destStocks.FirstOrDefault(s =>
                            string.Equals(s.BatchCode?.Trim(), normalizedBatch, StringComparison.OrdinalIgnoreCase) &&
                            s.ExpiryDate?.Date == effectiveExpiry?.Date &&
                            s.SupplierId == sourceSupplierId);
                    }
                    else
                    {
                        destStock = destStocks.FirstOrDefault(s =>
                            string.IsNullOrWhiteSpace(s.BatchCode) &&
                            s.ExpiryDate?.Date == effectiveExpiry?.Date &&
                            s.SupplierId == sourceSupplierId);
                    }
                }

                if (destStock == null)
                {
                    destStock = new BranchStock
                    {
                        TenantId = tenant.CurrentTenantId,
                        BranchId = transfer.ToBranchId,
                        ProductId = line.ProductId,
                        SupplierId = sourceSupplierId,
                        BatchCode = normalizedBatch,
                        ManufactureDate = sourceMfgDate,
                        ExpiryDate = effectiveExpiry,
                        QuantityOnHand = receivedTotal,
                        QuantityQuarantined = input.DamagedQuantity,
                        LandedCostPerUnit = line.UnitPrice
                    };
                    await stocks.AddAsync(destStock, ct);
                    createdDestStocks[cacheKey] = destStock;
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
                    if (!isNewlyCreatedStock)
                    {
                        stocks.Update(destStock);
                    }
                }
            }

            if (branchProducts != null && receivedProductIds.Count > 0)
            {
                var receivedProductIdList = receivedProductIds.ToList();
                var existingBranchProducts = (await branchProducts.FindAsync(
                    bp => bp.TenantId == tenant.CurrentTenantId
                        && bp.BranchId == transfer.ToBranchId
                        && receivedProductIdList.Contains(bp.ProductId),
                    ct)).ToDictionary(bp => bp.ProductId);

                foreach (var productId in receivedProductIds)
                {
                    if (!existingBranchProducts.TryGetValue(productId, out var existingBp))
                    {
                        await branchProducts.AddAsync(new BranchProduct
                        {
                            TenantId = tenant.CurrentTenantId,
                            BranchId = transfer.ToBranchId,
                            ProductId = productId,
                            Status = "Active",
                            CreatedBy = tenant.CurrentUserId
                        }, ct);
                    }
                    else if (!string.Equals(existingBp.Status, "Active", StringComparison.OrdinalIgnoreCase))
                    {
                        existingBp.Status = "Active";
                        branchProducts.Update(existingBp);
                    }
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
            if (transfer.TransferRequestId.HasValue && transferRequests != null && transferRequestDetails != null)
            {
                await SyncTransferRequestStatusAsync(transfer.TransferRequestId.Value, transfer, lines.Values.ToList(), ct);
            }
            if (hasDiscrepancy && notificationOutbox != null)
                await notificationOutbox.EnqueueTransferAsync(transfer, BusinessNotificationTypes.TransferReceiptDiscrepancy,
                    "Phiếu điều chuyển nhận có chênh lệch", transfer.FromBranchId, true, ct);
            await unitOfWork.SaveChangesAsync(ct);
            await unitOfWork.CommitTransactionAsync(ct);
            return await MapAsync(transfer, ct);
        }
        catch { await unitOfWork.RollbackTransactionAsync(ct); throw; }
    }

    public async Task<TransferDto> DispatchTransferAsync(Guid id, CancellationToken ct)
    {
        EnsureTenantRole();
        await unitOfWork.BeginTransactionAsync(ct);
        try
        {
            var transfer = await RequireForUpdateAsync(id, ct);
            EnsureBranch(transfer.FromBranchId);
            if (transfer.Status != TransferStatus.Pending) throw new InvalidOperationException("Chỉ có thể xuất phiếu đang chờ chi nhánh nguồn xử lý.");
            var lines = transfer.Details.Count > 0 ? transfer.Details.ToList() : (await details.FindAsync(x => x.TransferId == id, ct)).ToList();
            var todayVn = TodayVnDate;

            foreach (var line in lines)
            {
                var stock = await ResolveStockSourceAsync(transfer.FromBranchId, line.ProductId, line.BatchNumber, line.ExpiryDate, ct, line.BranchStockId, forUpdate: true);
                if ((stock.ExpiryDate.HasValue && stock.ExpiryDate.Value.Date < todayVn)
                    || (line.ExpiryDate.HasValue && line.ExpiryDate.Value.Date < todayVn))
                    throw new InvalidOperationException("Không thể điều chuyển lô hàng đã hết hạn sử dụng.");
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

        await unitOfWork.BeginTransactionAsync(ct);
        try
        {
            var transfer = await RequireForUpdateAsync(id, ct);
            if (transfer.Status is not (TransferStatus.Draft or TransferStatus.Pending))
                throw new InvalidOperationException("Chỉ có thể hủy phiếu trước khi xuất hàng.");
            var isCreator = transfer.CreatedBy == tenant.CurrentUserId;
            var isSourceEmployee = IsStaff && tenant.CurrentBranchId == transfer.FromBranchId;
            if (!IsOwner && !isCreator && !isSourceEmployee)
                throw new UnauthorizedAccessException("Không có quyền hủy hoặc từ chối phiếu này.");

            if (transfer.IsStockReserved)
            {
                var lines = transfer.Details.Count > 0 ? transfer.Details : (await details.FindAsync(x => x.TransferId == id, ct)).ToList();
                foreach (var line in lines)
                {
                    var stock = await ResolveStockSourceAsync(transfer.FromBranchId, line.ProductId, line.BatchNumber, line.ExpiryDate, ct, line.BranchStockId, forUpdate: true);
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
            if (transfer.TransferRequestId.HasValue && transferRequests != null && transferRequestDetails != null)
            {
                await SyncTransferRequestStatusAsync(transfer.TransferRequestId.Value, transfer, null, ct);
            }
            await unitOfWork.SaveChangesAsync(ct);
            await unitOfWork.CommitTransactionAsync(ct);
            return await MapAsync(transfer, ct);
        }
        catch { await unitOfWork.RollbackTransactionAsync(ct); throw; }
    }

    public async Task<TransferDto> ReturnTransferAsync(Guid id, ReturnTransferRequest request, CancellationToken ct)
    {
        EnsureTenantRole();
        if (string.IsNullOrWhiteSpace(request.Reason)) throw new InvalidOperationException("Cần nhập lý do hoàn trả.");

        await unitOfWork.BeginTransactionAsync(ct);
        try
        {
            var transfer = await RequireForUpdateAsync(id, ct);
            EnsureBranch(transfer.FromBranchId);
            if (transfer.Status != TransferStatus.InTransit)
                throw new InvalidOperationException("Chỉ có thể xác nhận hoàn trả phiếu đang vận chuyển.");
            var lines = transfer.Details.Count > 0 ? transfer.Details : (await details.FindAsync(x => x.TransferId == id, ct)).ToList();

            foreach (var line in lines)
            {
                var stock = await ResolveStockSourceAsync(transfer.FromBranchId, line.ProductId, line.BatchNumber, line.ExpiryDate, ct, line.BranchStockId, forUpdate: true);
                stock.QuantityOnHand += line.SentQuantity;
                stocks.Update(stock);
            }
            var previous = transfer.Status;
            transfer.Status = TransferStatus.Completed;
            transfer.CompletionResult = TransferCompletionResult.Returned;
            transfer.DiscrepancyNote = request.Reason.Trim();
            transfers.Update(transfer);
            await AddHistoryAsync(transfer, previous, transfer.Status, "ReturnedToSource", request.Reason, ct);
            if (transfer.TransferRequestId.HasValue && transferRequests != null && transferRequestDetails != null)
            {
                await SyncTransferRequestStatusAsync(transfer.TransferRequestId.Value, transfer, null, ct);
            }
            await unitOfWork.SaveChangesAsync(ct);
            await unitOfWork.CommitTransactionAsync(ct);
            return await MapAsync(transfer, ct);
        }
        catch { await unitOfWork.RollbackTransactionAsync(ct); throw; }
    }

    private async Task<Transfer> RequireAsync(Guid id, CancellationToken ct)
    {
        var transfer = await transfers.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException("Không tìm thấy phiếu điều chuyển.");
        ValidateTransferAccess(transfer);
        return transfer;
    }

    private async Task<Transfer> RequireForUpdateAsync(Guid id, CancellationToken ct)
    {
        var transfer = transferRepository != null
            ? await transferRepository.GetTransferForUpdateAsync(id, ct)
            : await transfers.GetByIdAsync(id, ct);
        if (transfer == null) throw new KeyNotFoundException("Không tìm thấy phiếu điều chuyển.");
        ValidateTransferAccess(transfer);
        return transfer;
    }

    private void ValidateTransferAccess(Transfer transfer)
    {
        if (string.IsNullOrWhiteSpace(tenant.CurrentTenantId) || !string.Equals(transfer.TenantId, tenant.CurrentTenantId, StringComparison.Ordinal))
            throw new KeyNotFoundException("Không tìm thấy phiếu điều chuyển.");
        if (!IsOwner && (!IsStaff || transfer.FromBranchId != tenant.CurrentBranchId && transfer.ToBranchId != tenant.CurrentBranchId))
            throw new UnauthorizedAccessException("Không có quyền truy cập phiếu.");
    }

    private void EnsureBranch(Guid branchId)
    {
        if (!IsOwner && (!IsStaff || tenant.CurrentBranchId is not Guid currentBranch || currentBranch != branchId))
            throw new UnauthorizedAccessException("Không có quyền thao tác tại chi nhánh này.");
    }

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
        BranchScopeGuard.EnsureCanAccessBranch(tenant, branchId);
        var branch = await branches.GetByIdAsync(branchId, ct);
        if (branch is null || branch.TenantId != tenant.CurrentTenantId || !branch.IsActive)
            throw new KeyNotFoundException("Không tìm thấy chi nhánh nguồn hợp lệ.");
    }

    private async Task SyncTransferRequestStatusAsync(
        Guid requestId,
        Transfer currentTransfer,
        IReadOnlyList<TransferDetail>? currentLines,
        CancellationToken ct)
    {
        var group = await transferRequests!.GetByIdAsync(requestId, ct);
        if (group == null || group.Status != TransferRequestStatus.Open) return;

        var reqDetails = await transferRequestDetails!.FindAsync(d => d.TransferRequestId == requestId, ct);
        var siblingTransfers = (await transfers.FindAsync(t => t.TransferRequestId == requestId && t.Id != currentTransfer.Id, ct)).ToList();
        var allTransfers = new List<Transfer>(siblingTransfers.Count + 1);
        allTransfers.AddRange(siblingTransfers);
        allTransfers.Add(currentTransfer);

        static bool IsReturnedOrCancelled(Transfer t)
            => t.Status == TransferStatus.Cancelled
                || (t.Status == TransferStatus.Completed && t.CompletionResult == TransferCompletionResult.Returned);

        static bool IsOpen(Transfer t)
            => t.Status is TransferStatus.Draft or TransferStatus.Pending or TransferStatus.InTransit;

        static bool IsReceivedCompleted(Transfer t)
            => t.Status == TransferStatus.Completed && t.CompletionResult != TransferCompletionResult.Returned;

        var receivedSiblingIds = siblingTransfers
            .Where(IsReceivedCompleted)
            .Select(t => t.Id)
            .ToHashSet();

        var receivedLines = new List<TransferDetail>();
        if (IsReceivedCompleted(currentTransfer) && currentLines != null)
        {
            receivedLines.AddRange(currentLines);
        }
        if (receivedSiblingIds.Count > 0)
        {
            receivedLines.AddRange(await details.FindAsync(d => receivedSiblingIds.Contains(d.TransferId), ct));
        }

        var receivedByProduct = receivedLines
            .GroupBy(l => l.ProductId)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.ReceivedQuantity));

        foreach (var reqDetail in reqDetails)
        {
            reqDetail.ReceivedQuantity = receivedByProduct.GetValueOrDefault(reqDetail.ProductId, 0m);
            transferRequestDetails.Update(reqDetail);
        }

        if (allTransfers.Count > 0 && allTransfers.All(IsReturnedOrCancelled))
        {
            group.Status = TransferRequestStatus.Cancelled;
            group.CancelledAt = DateTime.UtcNow;
            group.CancelReason = currentTransfer.CancelReason ?? currentTransfer.DiscrepancyNote;
            transferRequests.Update(group);
            return;
        }

        if ((allTransfers.Count > 0 && !allTransfers.Any(IsOpen) && allTransfers.Any(IsReceivedCompleted))
            || (reqDetails.Count > 0 && reqDetails.All(d => d.ReceivedQuantity >= d.RequestedQuantity)))
        {
            group.Status = TransferRequestStatus.Completed;
            group.CompletedAt = DateTime.UtcNow;
            transferRequests.Update(group);
        }
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

    private async Task<Dictionary<string, string>> ResolveCreatorNamesAsync(IEnumerable<Transfer> transferList, CancellationToken ct)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (userRepository == null) return map;

        var candidateIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in transferList)
        {
            if (!string.IsNullOrWhiteSpace(t.CreatedByStaff))
                candidateIds.Add(t.CreatedByStaff.Trim());
            if (t.CreatedBy.HasValue && t.CreatedBy.Value != Guid.Empty)
                candidateIds.Add(t.CreatedBy.Value.ToString());
        }

        if (candidateIds.Count == 0) return map;

        var candidateIdList = candidateIds.ToList();
        var users = await userRepository.FindAsync(u => candidateIdList.Contains(u.Id), ct);
        foreach (var user in users)
        {
            var displayName = !string.IsNullOrWhiteSpace(user.FullName)
                ? user.FullName.Trim()
                : !string.IsNullOrWhiteSpace(user.UserName)
                    ? user.UserName.Trim()
                    : user.Email;
            if (!string.IsNullOrWhiteSpace(displayName))
            {
                map[user.Id] = displayName;
            }
        }

        return map;
    }

    private static string ResolveCreatorDisplay(Transfer transfer, Dictionary<string, string> creatorNames)
    {
        if (!string.IsNullOrWhiteSpace(transfer.CreatedByStaff)
            && creatorNames.TryGetValue(transfer.CreatedByStaff.Trim(), out var byStaffId))
        {
            return byStaffId;
        }

        if (transfer.CreatedBy.HasValue
            && transfer.CreatedBy.Value != Guid.Empty
            && creatorNames.TryGetValue(transfer.CreatedBy.Value.ToString(), out var byCreatedBy))
        {
            return byCreatedBy;
        }

        return transfer.CreatedByStaff;
    }

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
        var creatorNames = await ResolveCreatorNamesAsync(pageTransfers, ct);

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
                ResolveCreatorDisplay(transfer, creatorNames), canViewCost ? transfer.TotalAmount : 0, transfer.HasDiscrepancy,
                transfer.Note, transfer.DiscrepancyNote, transfer.CancelReason, items,
                historyMap.GetValueOrDefault(transfer.Id, [])));
        }
        return dtos;
    }

    private async Task<TransferDto> MapAsync(Transfer transfer, CancellationToken ct)
    {
        var canViewCost = IsOwner;
        var from = await branches.GetByIdAsync(transfer.FromBranchId, ct);
        var to = await branches.GetByIdAsync(transfer.ToBranchId, ct);
        var items = new List<TransferLineDto>();
        foreach (var line in (transfer.Details.Count > 0 ? transfer.Details : (await details.FindAsync(x => x.TransferId == transfer.Id, ct)).ToList()))
        {
            var product = await products.GetByIdAsync(line.ProductId, ct);
            items.Add(new TransferLineDto(line.Id, line.ProductId, product?.ProductCode ?? "", product?.Name ?? "", line.PackagingUnit, line.SentQuantity, line.ReceivedQuantity, line.DamagedQuantity, line.BatchNumber, line.ExpiryDate, canViewCost ? line.UnitPrice : 0, canViewCost ? line.Amount : 0, line.DiscrepancyReason));
        }
        var history = (await statusHistory.FindAsync(x => x.TransferId == transfer.Id, ct))
            .OrderBy(x => x.CreatedAt)
            .Select(x => new TransferStatusHistoryDto(x.Id, x.FromStatus?.ToString(), x.ToStatus.ToString(), x.Action, x.ActorUserId, x.ActorBranchId, x.ActorRole, x.Reason, x.CreatedAt))
            .ToList();
        var creatorNames = await ResolveCreatorNamesAsync([transfer], ct);
        return new TransferDto(
            transfer.Id,
            transfer.TransferCode,
            transfer.FromBranchId,
            from?.Name ?? "",
            transfer.ToBranchId,
            to?.Name ?? "",
            transfer.Status.ToString(),
            transfer.CompletionResult?.ToString(),
            transfer.Origin.ToString(),
            transfer.Priority.ToString(),
            transfer.CreatedAt,
            transfer.SubmittedAt,
            transfer.DispatchedAt,
            transfer.ReceivedAt,
            transfer.ExpectedDeliveryDate,
            ResolveCreatorDisplay(transfer, creatorNames),
            canViewCost ? transfer.TotalAmount : 0,
            transfer.HasDiscrepancy,
            transfer.Note,
            transfer.DiscrepancyNote,
            transfer.CancelReason,
            items,
            history);
    }
}
