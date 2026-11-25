using MT_GRMS.Application.Common;
using MT_GRMS.Application.Inventory;
using MT_GRMS.Application.Notifications;
using MT_GRMS.Domain.Constants;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Entities.Sales;
using MT_GRMS.Domain.Enums;

namespace MT_GRMS.Application.Transfers;

public sealed class TransferRequestService(
    IRepository<TransferRequest> requests,
    IRepository<TransferRequestDetail> requestDetails,
    IRepository<Transfer> transfers,
    IRepository<TransferDetail> transferDetails,
    IRepository<TransferStatusHistory> statusHistory,
    IRepository<BranchStock> stocks,
    IRepository<Product> products,
    IRepository<Branch> branches,
    ITransferService transferService,
    ITenantContext tenant,
    IUnitOfWork unitOfWork,
    NotificationOutbox? notificationOutbox = null,
    IRepository<Package>? packages = null,
    IRepository<ProductUnit>? productUnits = null,
    IRepository<Order>? orders = null,
    ITransferRepository? transferRepository = null) : ITransferRequestService
{
    private sealed record NormalizedDemand(Product Product, decimal BaseQuantity, string BaseUnit);
    private sealed record Allocation(Guid BranchId, BranchStock Stock, Product Product, decimal Quantity, string Unit);

    private static DateTime TodayVnDate => DateTime.UtcNow.AddHours(7).Date;
    private bool IsOwner => tenant.CurrentRole == SystemRoles.StoreOwner;
    private bool IsStaff => tenant.CurrentRole == SystemRoles.Staff;

    public async Task<IReadOnlyList<TransferAvailableProductDto>> GetAvailableProductsAsync(Guid? toBranchId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(tenant.CurrentTenantId) || (!IsOwner && !IsStaff))
            throw new UnauthorizedAccessException("Role không có quyền truy cập yêu cầu điều chuyển.");

        Guid excludeBranchId;
        if (IsStaff)
        {
            if (tenant.CurrentBranchId is not Guid assignedBranch)
                throw new UnauthorizedAccessException("Staff chưa có chi nhánh được phân công.");
            if (toBranchId.HasValue && toBranchId.Value != assignedBranch)
                throw new UnauthorizedAccessException("Staff chỉ được yêu cầu hàng cho chi nhánh hiện tại.");
            excludeBranchId = assignedBranch;
        }
        else
        {
            excludeBranchId = toBranchId ?? Guid.Empty;
        }

        var activeSourceBranchIds = (await branches.FindAsync(
                b => b.TenantId == tenant.CurrentTenantId && b.IsActive && b.Id != excludeBranchId,
                ct))
            .Select(b => b.Id)
            .Distinct()
            .ToList();

        if (activeSourceBranchIds.Count == 0) return [];

        var candidateStocks = await stocks.FindAsync(
            s => s.TenantId == tenant.CurrentTenantId
                && activeSourceBranchIds.Contains(s.BranchId)
                && s.QuantityOnHand > 0,
            ct);

        if (candidateStocks.Count == 0) return [];

        var stockIds = candidateStocks.Select(s => s.Id).Distinct().ToList();
        var packageWeights = packages == null
            ? new Dictionary<Guid, decimal>()
            : (await packages.FindAsync(
                p => p.TenantId == tenant.CurrentTenantId
                    && stockIds.Contains(p.BranchStockId)
                    && !p.IsDeleted
                    && (p.Status == "AVAILABLE" || p.Status == "QUARANTINED"),
                ct))
                .GroupBy(p => p.BranchStockId)
                .ToDictionary(g => g.Key, g => g.Sum(p => p.NetWeight));

        var todayVn = TodayVnDate;
        var availableByProduct = candidateStocks
            .Select(stock => new
            {
                stock.ProductId,
                Available = BranchStockAvailability.GetRawSellableKg(stock, packageWeights.GetValueOrDefault(stock.Id), todayVn)
            })
            .Where(x => x.Available > 0)
            .GroupBy(x => x.ProductId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Available));

        if (availableByProduct.Count == 0) return [];

        var productIds = availableByProduct.Keys.ToList();
        var matchingProducts = await products.FindAsync(
            p => p.TenantId == tenant.CurrentTenantId
                && productIds.Contains(p.Id)
                && p.Status == ProductStatus.Active,
            ct);

        return matchingProducts
            .Select(p => new TransferAvailableProductDto(
                p.Id,
                p.ProductCode,
                p.Name,
                string.IsNullOrWhiteSpace(p.BaseUnit) ? "Cái" : p.BaseUnit.Trim(),
                availableByProduct[p.Id]))
            .OrderBy(p => p.ProductName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<TransferAllocationPreviewDto> PreviewAllocationAsync(CreateTransferRequestGroupRequest request, CancellationToken ct)
    {
        EnsureDestinationAccess(request.ToBranchId);
        await ResolveRelatedOrderIdAsync(request, ct);
        var demands = await NormalizeDemandsAsync(request, ct);
        var allocation = await BuildAllocationAsync(request.ToBranchId, demands, lockForUpdate: false, ct);
        return await ToPreviewAsync(request.ToBranchId, demands, allocation, ct);
    }

    public async Task<TransferRequestGroupDto> CreateAndAllocateAsync(CreateTransferRequestGroupRequest request, CancellationToken ct)
    {
        EnsureDestinationAccess(request.ToBranchId);
        await unitOfWork.BeginTransactionAsync(ct);
        try
        {
            var resolvedOrderId = await ResolveRelatedOrderIdAsync(request, ct);
            var demands = await NormalizeDemandsAsync(request, ct);
            var allocation = await BuildAllocationAsync(request.ToBranchId, demands, lockForUpdate: true, ct);
            var shortages = demands
                .Select(demand => new
                {
                    ProductId = demand.Product.Id,
                    Missing = demand.BaseQuantity - allocation.Where(x => x.Product.Id == demand.Product.Id).Sum(x => x.Quantity)
                })
                .Where(x => x.Missing > 0)
                .ToList();
            if (shortages.Count > 0)
                throw new InvalidOperationException("Tồn khả dụng toàn chuỗi không đủ để tạo trọn vẹn yêu cầu. Hãy xem lại phương án phân bổ.");

            var now = DateTime.UtcNow;
            var group = new TransferRequest
            {
                TenantId = tenant.CurrentTenantId,
                RequestCode = $"TRQ-{now:yyyyMMdd}-{Guid.NewGuid():N}"[..21].ToUpperInvariant(),
                ToBranchId = request.ToBranchId,
                Priority = TransferPriority.Urgent,
                RelatedOrderId = resolvedOrderId,
                RequiredAt = request.RequiredAt,
                Note = request.Note?.Trim(),
                CreatedBy = tenant.CurrentUserId
            };
            await requests.AddAsync(group, ct);

            foreach (var demand in demands)
            {
                var allocated = allocation.Where(x => x.Product.Id == demand.Product.Id).Sum(x => x.Quantity);
                var detail = new TransferRequestDetail
                {
                    TenantId = tenant.CurrentTenantId,
                    TransferRequestId = group.Id,
                    ProductId = demand.Product.Id,
                    RequestedQuantity = demand.BaseQuantity,
                    AllocatedQuantity = allocated,
                    Unit = demand.BaseUnit,
                    CreatedBy = tenant.CurrentUserId
                };
                group.Details.Add(detail);
                await requestDetails.AddAsync(detail, ct);
            }

            foreach (var source in allocation.GroupBy(x => x.BranchId))
            {
                var transfer = new Transfer
                {
                    TenantId = tenant.CurrentTenantId,
                    TransferCode = $"TRF-{now:yyyyMMdd}-{Guid.NewGuid():N}"[..21].ToUpperInvariant(),
                    TransferRequestId = group.Id,
                    FromBranchId = source.Key,
                    ToBranchId = request.ToBranchId,
                    Status = TransferStatus.Pending,
                    Origin = TransferOrigin.StaffUrgentRequest,
                    Priority = TransferPriority.Urgent,
                    RelatedOrderId = resolvedOrderId,
                    SubmittedAt = now,
                    SubmittedByUserId = tenant.CurrentUserId,
                    ExpectedDeliveryDate = request.RequiredAt,
                    Note = request.Note?.Trim(),
                    IsStockReserved = true,
                    CreatedBy = tenant.CurrentUserId,
                    CreatedByStaff = tenant.CurrentRawUserId
                };

                foreach (var item in source)
                {
                    item.Stock.QuantityReserved += item.Quantity;
                    stocks.Update(item.Stock);
                    var line = new TransferDetail
                    {
                        TenantId = tenant.CurrentTenantId,
                        TransferId = transfer.Id,
                        ProductId = item.Product.Id,
                        SentQuantity = item.Quantity,
                        PackagingUnit = item.Unit,
                        BatchNumber = item.Stock.BatchCode,
                        BranchStockId = item.Stock.Id,
                        ExpiryDate = item.Stock.ExpiryDate,
                        UnitPrice = item.Stock.LandedCostPerUnit,
                        Amount = item.Quantity * item.Stock.LandedCostPerUnit,
                        CreatedBy = tenant.CurrentUserId
                    };
                    transfer.Details.Add(line);
                    await transferDetails.AddAsync(line, ct);
                }
                transfer.TotalAmount = transfer.Details.Sum(x => x.Amount);
                group.Transfers.Add(transfer);
                await transfers.AddAsync(transfer, ct);
                await statusHistory.AddAsync(new TransferStatusHistory
                {
                    TenantId = tenant.CurrentTenantId,
                    TransferId = transfer.Id,
                    FromStatus = null,
                    ToStatus = TransferStatus.Pending,
                    ActorUserId = tenant.CurrentUserId,
                    ActorBranchId = tenant.CurrentBranchId,
                    ActorRole = tenant.CurrentRole?.Trim() ?? string.Empty,
                    Action = "UrgentRequestAllocated",
                    Reason = request.Note?.Trim(),
                    CreatedBy = tenant.CurrentUserId
                }, ct);
                if (notificationOutbox != null)
                    await notificationOutbox.EnqueueTransferAsync(transfer, BusinessNotificationTypes.TransferDispatchRequired,
                        "Phiếu điều chuyển chờ xuất kho", transfer.FromBranchId, false, ct);
            }

            await unitOfWork.SaveChangesAsync(ct);
            await unitOfWork.CommitTransactionAsync(ct);
            return await GetAsync(group.Id, ct);
        }
        catch
        {
            await unitOfWork.RollbackTransactionAsync(ct);
            throw;
        }
    }

    public async Task<TransferRequestGroupDto> GetAsync(Guid id, CancellationToken ct)
    {
        var group = await requests.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException("Không tìm thấy yêu cầu điều chuyển.");
        if (group.TenantId != tenant.CurrentTenantId)
            throw new KeyNotFoundException("Không tìm thấy yêu cầu điều chuyển.");
        EnsureDestinationAccess(group.ToBranchId);
        var detailRows = await requestDetails.FindAsync(x => x.TransferRequestId == id, ct);
        var transferRows = await transfers.FindAsync(x => x.TransferRequestId == id, ct);
        var transferDtos = new List<TransferDto>();
        foreach (var transfer in transferRows.OrderBy(x => x.CreatedAt))
            transferDtos.Add(await transferService.GetTransferAsync(transfer.Id, ct));

        var itemDtos = new List<TransferAllocationLineDto>();
        foreach (var detail in detailRows)
        {
            var product = await products.GetByIdAsync(detail.ProductId, ct);
            var received = transferDtos
                .Where(x => x.Status == TransferStatus.Completed.ToString()
                    && !string.Equals(x.CompletionResult, TransferCompletionResult.Returned.ToString(), StringComparison.OrdinalIgnoreCase))
                .SelectMany(x => x.Items)
                .Where(x => x.ProductId == detail.ProductId)
                .Sum(x => x.ReceivedQuantity);
            itemDtos.Add(new TransferAllocationLineDto(
                detail.ProductId,
                product?.ProductCode ?? string.Empty,
                product?.Name ?? string.Empty,
                detail.RequestedQuantity,
                detail.AllocatedQuantity,
                Math.Max(0, detail.RequestedQuantity - detail.AllocatedQuantity),
                detail.Unit,
                received));
        }

        // Compute effective status without mutating DB on HTTP GET (completion is persisted inside Receive/Cancel/Return)
        var effectiveStatus = group.Status == TransferRequestStatus.Open
            && itemDtos.Count > 0
            && itemDtos.All(x => x.ReceivedQuantity >= x.RequestedQuantity)
                ? TransferRequestStatus.Completed
                : group.Status;

        return new TransferRequestGroupDto(group.Id, group.RequestCode, group.ToBranchId, effectiveStatus.ToString(),
            group.Priority.ToString(), group.CreatedAt, group.RequiredAt, group.RelatedOrderId, group.Note, itemDtos, transferDtos);
    }

    private async Task<Guid?> ResolveRelatedOrderIdAsync(CreateTransferRequestGroupRequest request, CancellationToken ct)
    {
        if (request.RelatedOrderId.HasValue && request.RelatedOrderId.Value != Guid.Empty)
            return request.RelatedOrderId.Value;

        if (string.IsNullOrWhiteSpace(request.RelatedOrderCode))
            return null;

        var trimmedCode = request.RelatedOrderCode.Trim();
        if (Guid.TryParse(trimmedCode, out var parsedId))
            return parsedId;

        if (orders != null)
        {
            var upperCode = trimmedCode.ToUpperInvariant();
            var matched = (await orders.FindAsync(
                o => o.TenantId == tenant.CurrentTenantId
                    && o.BranchId == request.ToBranchId
                    && o.OrderCode.ToUpper() == upperCode,
                ct)).FirstOrDefault();
            if (matched != null)
                return matched.Id;
        }

        throw new InvalidOperationException($"Không tìm thấy đơn hàng có mã '{trimmedCode}' tại chi nhánh nhận.");
    }

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

    private async Task<List<NormalizedDemand>> NormalizeDemandsAsync(CreateTransferRequestGroupRequest request, CancellationToken ct)
    {
        if (request.Items is null || request.Items.Count == 0 || request.Items.Any(x => x.Quantity <= 0 || string.IsNullOrWhiteSpace(x.Unit)))
            throw new InvalidOperationException("Yêu cầu phải có ít nhất một sản phẩm với số lượng dương.");
        if (request.Items.Select(x => x.ProductId).Distinct().Count() != request.Items.Count)
            throw new InvalidOperationException("Mỗi sản phẩm chỉ được xuất hiện một lần trong yêu cầu.");

        var destination = await branches.GetByIdAsync(request.ToBranchId, ct) ?? throw new KeyNotFoundException("Không tìm thấy chi nhánh nhận.");
        if (!destination.IsActive || destination.TenantId != tenant.CurrentTenantId)
            throw new InvalidOperationException("Chi nhánh nhận không hợp lệ.");

        var demands = new List<NormalizedDemand>(request.Items.Count);
        foreach (var input in request.Items)
        {
            var product = await products.GetByIdAsync(input.ProductId, ct) ?? throw new KeyNotFoundException("Không tìm thấy sản phẩm.");
            if (product.TenantId != tenant.CurrentTenantId) throw new KeyNotFoundException("Không tìm thấy sản phẩm.");
            var (baseQty, baseUnit) = await NormalizeToBaseUnitAsync(product, input.Quantity, input.Unit, ct);
            demands.Add(new NormalizedDemand(product, baseQty, baseUnit));
        }

        return demands;
    }

    private async Task<List<Allocation>> BuildAllocationAsync(
        Guid toBranchId,
        IReadOnlyList<NormalizedDemand> demands,
        bool lockForUpdate,
        CancellationToken ct)
    {
        var activeBranches = (await branches.GetAllAsync(ct))
            .Where(x => x.IsActive && x.Id != toBranchId && x.TenantId == tenant.CurrentTenantId)
            .ToDictionary(x => x.Id);

        var todayVn = TodayVnDate;
        var result = new List<Allocation>();
        foreach (var demand in demands)
        {
            var packageWeights = packages == null ? new Dictionary<Guid, decimal>() : (await packages.FindAsync(p => p.TenantId == tenant.CurrentTenantId
                && p.ProductId == demand.Product.Id && !p.IsDeleted && (p.Status == "AVAILABLE" || p.Status == "QUARANTINED"), ct))
                .GroupBy(p => p.BranchStockId).ToDictionary(g => g.Key, g => g.Sum(p => p.NetWeight));

            var rawStocks = lockForUpdate && transferRepository != null
                ? await transferRepository.GetBranchStocksByProductForUpdateAsync(tenant.CurrentTenantId, demand.Product.Id, toBranchId, ct)
                : await stocks.FindAsync(x => x.TenantId == tenant.CurrentTenantId && x.ProductId == demand.Product.Id && x.BranchId != toBranchId, ct);

            var candidates = rawStocks
                .Where(x => activeBranches.ContainsKey(x.BranchId) && (!x.ExpiryDate.HasValue || x.ExpiryDate.Value.Date >= todayVn))
                .Select(x => new { Stock = x, Available = BranchStockAvailability.GetRawSellableKg(x, packageWeights.GetValueOrDefault(x.Id), todayVn) })
                .Where(x => x.Available > 0)
                .GroupBy(x => x.Stock.BranchId)
                .OrderByDescending(group => group.Sum(x => x.Available))
                .SelectMany(group => group.OrderBy(x => x.Stock.ExpiryDate ?? DateTime.MaxValue))
                .ToList();
            var remaining = demand.BaseQuantity;
            foreach (var candidate in candidates)
            {
                var quantity = Math.Min(remaining, candidate.Available);
                if (quantity <= 0) continue;
                result.Add(new Allocation(candidate.Stock.BranchId, candidate.Stock, demand.Product, quantity, demand.BaseUnit));
                remaining -= quantity;
                if (remaining <= 0) break;
            }
        }
        return result;
    }

    private async Task<TransferAllocationPreviewDto> ToPreviewAsync(
        Guid toBranchId,
        IReadOnlyList<NormalizedDemand> demands,
        List<Allocation> allocation,
        CancellationToken ct)
    {
        var totals = new List<TransferAllocationLineDto>();
        foreach (var demand in demands)
        {
            var allocated = allocation.Where(x => x.Product.Id == demand.Product.Id).Sum(x => x.Quantity);
            totals.Add(new TransferAllocationLineDto(
                demand.Product.Id,
                demand.Product.ProductCode,
                demand.Product.Name,
                demand.BaseQuantity,
                allocated,
                Math.Max(0, demand.BaseQuantity - allocated),
                demand.BaseUnit));
        }
        var sources = new List<TransferAllocationSourceDto>();
        foreach (var source in allocation.GroupBy(x => x.BranchId))
        {
            var branch = await branches.GetByIdAsync(source.Key, ct);
            var lines = source.GroupBy(x => x.Product.Id).Select(group =>
            {
                var first = group.First();
                var requested = demands.First(x => x.Product.Id == group.Key).BaseQuantity;
                var allocated = group.Sum(x => x.Quantity);
                return new TransferAllocationLineDto(
                    group.Key,
                    first.Product.ProductCode,
                    first.Product.Name,
                    requested,
                    allocated,
                    Math.Max(0, requested - allocated),
                    first.Unit);
            }).ToList();
            sources.Add(new TransferAllocationSourceDto(source.Key, branch?.Name ?? string.Empty, lines));
        }
        return new TransferAllocationPreviewDto(toBranchId, totals.All(x => x.ShortageQuantity == 0), totals, sources);
    }

    private void EnsureDestinationAccess(Guid destinationBranchId)
    {
        if (string.IsNullOrWhiteSpace(tenant.CurrentTenantId) || !IsOwner && !IsStaff)
            throw new UnauthorizedAccessException("Role không có quyền truy cập yêu cầu điều chuyển.");
        if (IsOwner) return;
        if (tenant.CurrentBranchId is not Guid assignedBranch || assignedBranch != destinationBranchId)
            throw new UnauthorizedAccessException("Staff chỉ được yêu cầu hàng cho chi nhánh hiện tại.");
    }
}
