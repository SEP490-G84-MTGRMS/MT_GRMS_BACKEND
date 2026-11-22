using MT_GRMS.Application.Common;
using MT_GRMS.Application.Inventory;
using MT_GRMS.Domain.Constants;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Domain.Entities.SaaS;
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
    IRepository<Package>? packages = null) : ITransferRequestService
{
    private sealed record Allocation(Guid BranchId, BranchStock Stock, Product Product, decimal Quantity, string Unit);

    private bool IsOwner => tenant.CurrentRole == SystemRoles.StoreOwner;
    private bool IsStaff => tenant.CurrentRole == SystemRoles.Staff;

    public async Task<TransferAllocationPreviewDto> PreviewAllocationAsync(CreateTransferRequestGroupRequest request, CancellationToken ct)
    {
        var allocation = await BuildAllocationAsync(request, ct);
        return await ToPreviewAsync(request, allocation, ct);
    }

    public async Task<TransferRequestGroupDto> CreateAndAllocateAsync(CreateTransferRequestGroupRequest request, CancellationToken ct)
    {
        var allocation = await BuildAllocationAsync(request, ct);
        var shortages = request.Items
            .Select(item => new
            {
                item.ProductId,
                Missing = item.Quantity - allocation.Where(x => x.Product.Id == item.ProductId).Sum(x => x.Quantity)
            })
            .Where(x => x.Missing > 0)
            .ToList();
        if (shortages.Count > 0)
            throw new InvalidOperationException("Tồn khả dụng toàn chuỗi không đủ để tạo trọn vẹn yêu cầu. Hãy xem lại phương án phân bổ.");
        await unitOfWork.BeginTransactionAsync(ct);
        try
        {
            var now = DateTime.UtcNow;
            var group = new TransferRequest
            {
                TenantId = tenant.CurrentTenantId,
                RequestCode = $"TRQ-{now:yyyyMMdd}-{Guid.NewGuid():N}"[..21].ToUpperInvariant(),
                ToBranchId = request.ToBranchId,
                Priority = TransferPriority.Urgent,
                RelatedOrderId = request.RelatedOrderId,
                RequiredAt = request.RequiredAt,
                Note = request.Note?.Trim(),
                CreatedBy = tenant.CurrentUserId
            };
            await requests.AddAsync(group, ct);

            foreach (var input in request.Items)
            {
                var allocated = allocation.Where(x => x.Product.Id == input.ProductId).Sum(x => x.Quantity);
                var detail = new TransferRequestDetail
                {
                    TenantId = tenant.CurrentTenantId,
                    TransferRequestId = group.Id,
                    ProductId = input.ProductId,
                    RequestedQuantity = input.Quantity,
                    AllocatedQuantity = allocated,
                    Unit = input.Unit.Trim(),
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
                    RelatedOrderId = request.RelatedOrderId,
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
                .Where(x => x.Status == TransferStatus.Completed.ToString())
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
            detail.ReceivedQuantity = received;
        }

        if (group.Status == TransferRequestStatus.Open && itemDtos.Count > 0
            && itemDtos.All(x => x.ReceivedQuantity >= x.RequestedQuantity))
        {
            group.Status = TransferRequestStatus.Completed;
            group.CompletedAt = DateTime.UtcNow;
            requests.Update(group);
            await unitOfWork.SaveChangesAsync(ct);
        }

        return new TransferRequestGroupDto(group.Id, group.RequestCode, group.ToBranchId, group.Status.ToString(),
            group.Priority.ToString(), group.CreatedAt, group.RequiredAt, group.RelatedOrderId, group.Note, itemDtos, transferDtos);
    }

    private async Task<List<Allocation>> BuildAllocationAsync(CreateTransferRequestGroupRequest request, CancellationToken ct)
    {
        EnsureDestinationAccess(request.ToBranchId);
        if (request.Items is null || request.Items.Count == 0 || request.Items.Any(x => x.Quantity <= 0 || string.IsNullOrWhiteSpace(x.Unit)))
            throw new InvalidOperationException("Yêu cầu phải có ít nhất một sản phẩm với số lượng dương.");
        if (request.Items.Select(x => x.ProductId).Distinct().Count() != request.Items.Count)
            throw new InvalidOperationException("Mỗi sản phẩm chỉ được xuất hiện một lần trong yêu cầu.");

        var destination = await branches.GetByIdAsync(request.ToBranchId, ct) ?? throw new KeyNotFoundException("Không tìm thấy chi nhánh nhận.");
        if (!destination.IsActive || destination.TenantId != tenant.CurrentTenantId)
            throw new InvalidOperationException("Chi nhánh nhận không hợp lệ.");

        var activeBranches = (await branches.GetAllAsync(ct))
            .Where(x => x.IsActive && x.Id != request.ToBranchId && x.TenantId == tenant.CurrentTenantId)
            .ToDictionary(x => x.Id);
        var result = new List<Allocation>();
        foreach (var input in request.Items)
        {
            var product = await products.GetByIdAsync(input.ProductId, ct) ?? throw new KeyNotFoundException("Không tìm thấy sản phẩm.");
            if (product.TenantId != tenant.CurrentTenantId) throw new KeyNotFoundException("Không tìm thấy sản phẩm.");
            var packageWeights = packages == null ? new Dictionary<Guid, decimal>() : (await packages.FindAsync(p => p.TenantId == tenant.CurrentTenantId
                && p.ProductId == input.ProductId && !p.IsDeleted && (p.Status == "AVAILABLE" || p.Status == "QUARANTINED"), ct))
                .GroupBy(p => p.BranchStockId).ToDictionary(g => g.Key, g => g.Sum(p => p.NetWeight));
            var candidates = (await stocks.FindAsync(x => x.TenantId == tenant.CurrentTenantId && x.ProductId == input.ProductId && x.BranchId != request.ToBranchId, ct))
                .Where(x => activeBranches.ContainsKey(x.BranchId) && (!x.ExpiryDate.HasValue || x.ExpiryDate.Value.Date >= DateTime.UtcNow.Date))
                .Select(x => new { Stock = x, Available = BranchStockAvailability.GetRawSellableKg(x, packageWeights.GetValueOrDefault(x.Id)) })
                .Where(x => x.Available > 0)
                .GroupBy(x => x.Stock.BranchId)
                .OrderByDescending(group => group.Sum(x => x.Available))
                .SelectMany(group => group.OrderBy(x => x.Stock.ExpiryDate ?? DateTime.MaxValue))
                .ToList();
            var remaining = input.Quantity;
            foreach (var candidate in candidates)
            {
                var quantity = Math.Min(remaining, candidate.Available);
                if (quantity <= 0) continue;
                result.Add(new Allocation(candidate.Stock.BranchId, candidate.Stock, product, quantity, input.Unit.Trim()));
                remaining -= quantity;
                if (remaining <= 0) break;
            }
        }
        return result;
    }

    private async Task<TransferAllocationPreviewDto> ToPreviewAsync(CreateTransferRequestGroupRequest request, List<Allocation> allocation, CancellationToken ct)
    {
        var totals = new List<TransferAllocationLineDto>();
        foreach (var input in request.Items)
        {
            var product = await products.GetByIdAsync(input.ProductId, ct);
            var allocated = allocation.Where(x => x.Product.Id == input.ProductId).Sum(x => x.Quantity);
            totals.Add(new TransferAllocationLineDto(input.ProductId, product?.ProductCode ?? string.Empty,
                product?.Name ?? string.Empty, input.Quantity, allocated, Math.Max(0, input.Quantity - allocated), input.Unit));
        }
        var sources = new List<TransferAllocationSourceDto>();
        foreach (var source in allocation.GroupBy(x => x.BranchId))
        {
            var branch = await branches.GetByIdAsync(source.Key, ct);
            var lines = source.GroupBy(x => x.Product.Id).Select(lines =>
            {
                var first = lines.First();
                var requested = request.Items.First(x => x.ProductId == lines.Key).Quantity;
                var allocated = lines.Sum(x => x.Quantity);
                return new TransferAllocationLineDto(lines.Key, first.Product.ProductCode, first.Product.Name,
                    requested, allocated, Math.Max(0, requested - allocated), first.Unit);
            }).ToList();
            sources.Add(new TransferAllocationSourceDto(source.Key, branch?.Name ?? string.Empty, lines));
        }
        return new TransferAllocationPreviewDto(request.ToBranchId, totals.All(x => x.ShortageQuantity == 0), totals, sources);
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
