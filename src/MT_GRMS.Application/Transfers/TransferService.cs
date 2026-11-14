using MT_GRMS.Application.Common;
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
    ITenantContext tenant,
    IUnitOfWork unitOfWork) : ITransferService
{
    private bool IsOwner =>
        string.Equals(tenant.CurrentRole?.Trim(), "STORE_OWNER", StringComparison.OrdinalIgnoreCase)
        || string.Equals(tenant.CurrentRole?.Trim(), "StoreOwner", StringComparison.OrdinalIgnoreCase)
        || string.Equals(tenant.CurrentRole?.Trim(), "ADMIN", StringComparison.OrdinalIgnoreCase)
        || string.Equals(tenant.CurrentRole?.Trim(), "Admin", StringComparison.OrdinalIgnoreCase)
        || string.Equals(tenant.CurrentRole?.Trim(), "SystemAdmin", StringComparison.OrdinalIgnoreCase);

    private bool IsBranchEmployee =>
        string.Equals(tenant.CurrentRole?.Trim(), "STAFF", StringComparison.OrdinalIgnoreCase)
        || string.Equals(tenant.CurrentRole?.Trim(), "EMPLOYEE", StringComparison.OrdinalIgnoreCase)
        || string.Equals(tenant.CurrentRole?.Trim(), "BRANCH_MANAGER", StringComparison.OrdinalIgnoreCase)
        || string.Equals(tenant.CurrentRole?.Trim(), "BranchManager", StringComparison.OrdinalIgnoreCase)
        || string.Equals(tenant.CurrentRole?.Trim(), "CASHIER", StringComparison.OrdinalIgnoreCase);

    public async Task<PagedResult<TransferDto>> GetTransfersAsync(TransferFilterParams filter, CancellationToken ct)
    {
        var all = (await transfers.GetAllAsync(ct)).AsEnumerable();
        if (string.IsNullOrWhiteSpace(tenant.CurrentTenantId))
            return new PagedResult<TransferDto> { Items = [], Page = Math.Max(filter.Page, 1), PageSize = Math.Clamp(filter.PageSize, 1, 100), TotalItems = 0 };
        all = all.Where(x => string.Equals(x.TenantId, tenant.CurrentTenantId, StringComparison.Ordinal));
        if (!IsOwner)
        {
            if (IsBranchEmployee && tenant.CurrentBranchId is Guid branch) all = all.Where(x => x.FromBranchId == branch || x.ToBranchId == branch);
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
                if (transfer.Status != TransferStatus.Received) continue;
                var lines = transfer.Details.Count > 0 ? transfer.Details.ToList() : (await details.FindAsync(x => x.TransferId == transfer.Id, ct)).ToList();
                var isMatch = lines.All(line => line.ReceivedQuantity == line.SentQuantity && line.DamagedQuantity == 0);
                if (isMatch == matchRequested) receiptFiltered.Add(transfer);
            }
            all = receiptFiltered;
        }
        var matching = all.OrderByDescending(x => x.CreatedAt).ToList();
        var page = Math.Max(filter.Page, 1); var size = Math.Clamp(filter.PageSize, 1, 100);
        var result = new List<TransferDto>();
        foreach (var transfer in matching.Skip((page - 1) * size).Take(size)) result.Add(await MapAsync(transfer, ct));
        return new PagedResult<TransferDto> { Items = result, Page = page, PageSize = size, TotalItems = matching.Count };
    }

    public async Task<TransferDto> GetTransferAsync(Guid id, CancellationToken ct) => await MapAsync(await RequireAsync(id, ct), ct);

    public async Task<IReadOnlyList<TransferStockBatchDto>> GetStockBatchesAsync(Guid branchId, CancellationToken ct)
    {
        EnsureBranch(branchId);
        var stock = await stocks.FindAsync(x => x.BranchId == branchId && x.QuantityOnHand > 0, ct);
        return stock.Select(x => new TransferStockBatchDto(x.ProductId, x.BatchCode, x.ExpiryDate,
            Math.Max(0, x.QuantityOnHand - x.QuantityReserved - x.QuantityQuarantined))).ToList();
    }

    private async Task<BranchStock> ResolveStockSourceAsync(Guid branchId, Guid productId, string? batchCode, DateTime? expiryDate, CancellationToken ct)
    {
        var branchStocks = await stocks.FindAsync(s => s.BranchId == branchId && s.ProductId == productId, ct);
        if (branchStocks.Count == 0)
            throw new InvalidOperationException("Không tìm thấy tồn kho cho sản phẩm này tại chi nhánh gửi.");

        if (branchStocks.Count == 1)
            return branchStocks[0];

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
                    if (dateMatches.Count > 0) return dateMatches[0];
                }
                return batchMatches.OrderByDescending(s => s.QuantityOnHand - s.QuantityReserved - s.QuantityQuarantined).First();
            }
        }

        if (expiryDate.HasValue)
        {
            var dateMatches = branchStocks.Where(s => s.ExpiryDate.HasValue && s.ExpiryDate.Value.Date == expiryDate.Value.Date).ToList();
            if (dateMatches.Count > 0) return dateMatches.OrderByDescending(s => s.QuantityOnHand - s.QuantityReserved - s.QuantityQuarantined).First();
        }

        return branchStocks.OrderByDescending(s => s.QuantityOnHand - s.QuantityReserved - s.QuantityQuarantined).First();
    }

    public async Task<TransferDto> CreateTransferAsync(CreateTransferRequest request, CancellationToken ct)
    {
        if (request.FromBranchId == request.ToBranchId) throw new InvalidOperationException("Chi nhánh gửi và nhận phải khác nhau.");
        if (request.Items is null || request.Items.Count == 0 || request.Items.Any(x => x.Quantity <= 0 || x.UnitPrice < 0 || string.IsNullOrWhiteSpace(x.Unit) || x.Unit.Length > 30 || x.BatchCode?.Length > 50))
            throw new InvalidOperationException("Phiếu cần mặt hàng hợp lệ, số lượng dương, giá không âm và đơn vị đúng định dạng.");
        EnsureBranch(request.FromBranchId);
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
            var transfer = new Transfer { TenantId = tenant.CurrentTenantId, TransferCode = $"TRF-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}"[..21].ToUpperInvariant(), FromBranchId = request.FromBranchId, ToBranchId = request.ToBranchId, Status = request.DispatchNow ? TransferStatus.InTransit : TransferStatus.Draft, DispatchedAt = request.DispatchNow ? DateTime.UtcNow : null, ExpectedDeliveryDate = request.ExpectedDeliveryDate, Note = request.Note?.Trim(), CreatedByStaff = tenant.CurrentRawUserId };
            foreach (var input in request.Items)
            {
                var product = await products.GetByIdAsync(input.ProductId, ct) ?? throw new KeyNotFoundException("Không tìm thấy sản phẩm.");
                string? actualBatch = input.BatchCode?.Trim();
                DateTime? actualExpiry = input.ExpiryDate;
                if (request.DispatchNow)
                {
                    var stock = await ResolveStockSourceAsync(request.FromBranchId, input.ProductId, input.BatchCode, input.ExpiryDate, ct);
                    var available = stock.QuantityOnHand - stock.QuantityReserved - stock.QuantityQuarantined;
                    if (available < input.Quantity) throw new InvalidOperationException($"Không đủ tồn khả dụng cho {product.Name}; hiện có {available}, yêu cầu chuyển {input.Quantity}.");
                    stock.QuantityOnHand -= input.Quantity; stocks.Update(stock);

                    if (string.IsNullOrWhiteSpace(actualBatch)) actualBatch = stock.BatchCode;
                    actualExpiry ??= stock.ExpiryDate;
                }
                transfer.Details.Add(new TransferDetail { ProductId = input.ProductId, SentQuantity = input.Quantity, PackagingUnit = input.Unit.Trim(), BatchNumber = actualBatch, ExpiryDate = actualExpiry, UnitPrice = input.UnitPrice, Amount = input.Quantity * input.UnitPrice });
            }
            transfer.TotalAmount = transfer.Details.Sum(x => x.Amount);
            await transfers.AddAsync(transfer, ct);
            foreach (var detail in transfer.Details) await details.AddAsync(detail, ct);
            await unitOfWork.SaveChangesAsync(ct); await unitOfWork.CommitTransactionAsync(ct);
            return await MapAsync(transfer, ct);
        }
        catch { await unitOfWork.RollbackTransactionAsync(ct); throw; }
    }

    public async Task<TransferDto> ReceiveTransferAsync(Guid id, ReceiveTransferRequest request, CancellationToken ct)
    {
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
                else if (destStocks.Count == 1)
                {
                    destStock = destStocks[0];
                }

                if (destStock == null)
                {
                    await stocks.AddAsync(new BranchStock
                    {
                        TenantId = tenant.CurrentTenantId,
                        BranchId = transfer.ToBranchId,
                        ProductId = line.ProductId,
                        BatchCode = line.BatchNumber,
                        ExpiryDate = line.ExpiryDate,
                        QuantityOnHand = input.ReceivedQuantity + input.DamagedQuantity,
                        QuantityQuarantined = input.DamagedQuantity,
                        LandedCostPerUnit = line.UnitPrice
                    }, ct);
                }
                else
                {
                    destStock.QuantityOnHand += input.ReceivedQuantity + input.DamagedQuantity;
                    destStock.QuantityQuarantined += input.DamagedQuantity;
                    stocks.Update(destStock);
                }
            }
            transfer.Status = TransferStatus.Received; transfer.ReceivedAt = DateTime.UtcNow; transfer.DiscrepancyNote = request.Note?.Trim(); transfers.Update(transfer);
            await unitOfWork.SaveChangesAsync(ct); await unitOfWork.CommitTransactionAsync(ct); return await MapAsync(transfer, ct);
        }
        catch { await unitOfWork.RollbackTransactionAsync(ct); throw; }
    }

    public async Task<TransferDto> DispatchTransferAsync(Guid id, CancellationToken ct)
    {
        var transfer = await RequireAsync(id, ct);
        EnsureBranch(transfer.FromBranchId);
        if (transfer.Status != TransferStatus.Draft) throw new InvalidOperationException("Chỉ có thể xuất chuyển phiếu nháp.");
        var lines = transfer.Details.Count > 0 ? transfer.Details.ToList() : (await details.FindAsync(x => x.TransferId == id, ct)).ToList();
        await unitOfWork.BeginTransactionAsync(ct);
        try
        {
            foreach (var line in lines)
            {
                var stock = await ResolveStockSourceAsync(transfer.FromBranchId, line.ProductId, line.BatchNumber, line.ExpiryDate, ct);
                var available = stock.QuantityOnHand - stock.QuantityReserved - stock.QuantityQuarantined;
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
            transfer.Status = TransferStatus.InTransit;
            transfer.DispatchedAt = DateTime.UtcNow;
            transfers.Update(transfer);
            await unitOfWork.SaveChangesAsync(ct);
            await unitOfWork.CommitTransactionAsync(ct);
            return await MapAsync(transfer, ct);
        }
        catch { await unitOfWork.RollbackTransactionAsync(ct); throw; }
    }

    public async Task<TransferDto> CancelTransferAsync(Guid id, CancelTransferRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Reason)) throw new InvalidOperationException("Cần nhập lý do hủy.");
        var transfer = await RequireAsync(id, ct); EnsureBranch(transfer.FromBranchId);
        if (transfer.Status is not (TransferStatus.Draft or TransferStatus.InTransit)) throw new InvalidOperationException("Chỉ hủy phiếu nháp hoặc đang vận chuyển.");
        if (transfer.Status == TransferStatus.InTransit && !IsOwner) throw new UnauthorizedAccessException("Chỉ chủ cửa hàng hoặc quản trị viên được hủy phiếu đã xuất chuyển.");
        await unitOfWork.BeginTransactionAsync(ct);
        try
        {
            if (transfer.Status == TransferStatus.InTransit)
                foreach (var line in (transfer.Details.Count > 0 ? transfer.Details : (await details.FindAsync(x => x.TransferId == id, ct)).ToList()))
                {
                    var stock = await ResolveStockSourceAsync(transfer.FromBranchId, line.ProductId, line.BatchNumber, line.ExpiryDate, ct);
                    stock.QuantityOnHand += line.SentQuantity;
                    stocks.Update(stock);
                }
            transfer.Status = TransferStatus.Cancelled; transfer.CancelReason = request.Reason.Trim(); transfers.Update(transfer);
            await unitOfWork.SaveChangesAsync(ct); await unitOfWork.CommitTransactionAsync(ct); return await MapAsync(transfer, ct);
        }
        catch { await unitOfWork.RollbackTransactionAsync(ct); throw; }
    }

    private async Task<Transfer> RequireAsync(Guid id, CancellationToken ct)
    {
        var transfer = await transfers.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException("Không tìm thấy phiếu điều chuyển.");
        if (string.IsNullOrWhiteSpace(tenant.CurrentTenantId) || !string.Equals(transfer.TenantId, tenant.CurrentTenantId, StringComparison.Ordinal)) throw new KeyNotFoundException("Không tìm thấy phiếu điều chuyển.");
        if (!IsOwner && (!IsBranchEmployee || transfer.FromBranchId != tenant.CurrentBranchId && transfer.ToBranchId != tenant.CurrentBranchId)) throw new UnauthorizedAccessException("Không có quyền truy cập phiếu.");
        return transfer;
    }
    private void EnsureBranch(Guid branchId) { if (!IsOwner && (!IsBranchEmployee || tenant.CurrentBranchId is not Guid currentBranch || currentBranch != branchId)) throw new UnauthorizedAccessException("Không có quyền thao tác tại chi nhánh này."); }
    private async Task<TransferDto> MapAsync(Transfer transfer, CancellationToken ct)
    {
        var from = await branches.GetByIdAsync(transfer.FromBranchId, ct); var to = await branches.GetByIdAsync(transfer.ToBranchId, ct); var items = new List<TransferLineDto>();
        foreach (var line in (transfer.Details.Count > 0 ? transfer.Details : (await details.FindAsync(x => x.TransferId == transfer.Id, ct)).ToList()))
        {
            var product = await products.GetByIdAsync(line.ProductId, ct);
            items.Add(new TransferLineDto(line.Id, line.ProductId, product?.ProductCode ?? "", product?.Name ?? "", line.PackagingUnit, line.SentQuantity, line.ReceivedQuantity, line.DamagedQuantity, line.BatchNumber, line.ExpiryDate, line.UnitPrice, line.Amount, line.DiscrepancyReason));
        }
        return new TransferDto(transfer.Id, transfer.TransferCode, transfer.FromBranchId, from?.Name ?? "", transfer.ToBranchId, to?.Name ?? "", transfer.Status.ToString(), transfer.CreatedAt, transfer.DispatchedAt, transfer.ReceivedAt, transfer.ExpectedDeliveryDate, transfer.CreatedByStaff, transfer.TotalAmount, transfer.Note, transfer.DiscrepancyNote, transfer.CancelReason, items);
    }
}