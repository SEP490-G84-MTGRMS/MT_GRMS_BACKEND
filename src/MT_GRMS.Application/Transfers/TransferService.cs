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
    private bool IsOwner => !string.IsNullOrEmpty(tenant.CurrentRole) && (tenant.CurrentRole.Contains("OWNER", StringComparison.OrdinalIgnoreCase) || tenant.CurrentRole.Contains("ADMIN", StringComparison.OrdinalIgnoreCase));

    public async Task<PagedResult<TransferDto>> GetTransfersAsync(TransferFilterParams filter, CancellationToken ct)
    {
        var all = (await transfers.GetAllAsync(ct)).AsEnumerable();
        if (!string.IsNullOrEmpty(tenant.CurrentTenantId))
        {
            all = all.Where(x => string.IsNullOrEmpty(x.TenantId) || string.Equals(x.TenantId, tenant.CurrentTenantId, StringComparison.Ordinal));
        }
        if (!IsOwner)
        {
            if (tenant.CurrentBranchId is Guid branch) all = all.Where(x => x.FromBranchId == branch || x.ToBranchId == branch);
            else all = Enumerable.Empty<Transfer>();
        }
        if (filter.FromBranchId.HasValue) all = all.Where(x => x.FromBranchId == filter.FromBranchId);
        if (filter.ToBranchId.HasValue) all = all.Where(x => x.ToBranchId == filter.ToBranchId);
        if (filter.Status.HasValue) all = all.Where(x => x.Status == filter.Status);
        if (filter.FromDate.HasValue) all = all.Where(x => (x.ReceivedAt ?? x.DispatchedAt ?? x.CreatedAt) >= filter.FromDate);
        if (filter.ToDate.HasValue) all = all.Where(x => (x.ReceivedAt ?? x.DispatchedAt ?? x.CreatedAt) < filter.ToDate.Value.AddDays(1));
        if (!string.IsNullOrWhiteSpace(filter.Search)) all = all.Where(x => x.TransferCode.Contains(filter.Search, StringComparison.OrdinalIgnoreCase));
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

    public async Task<TransferDto> CreateTransferAsync(CreateTransferRequest request, CancellationToken ct)
    {
        if (request.FromBranchId == request.ToBranchId) throw new InvalidOperationException("Chi nhánh gửi và nhận phải khác nhau.");
        if (request.Items is null || request.Items.Count == 0 || request.Items.Any(x => x.Quantity <= 0 || x.UnitPrice < 0 || string.IsNullOrWhiteSpace(x.Unit) || x.Unit.Length > 30 || x.BatchCode?.Length > 50) || request.Items.GroupBy(x => new { x.ProductId, x.BatchCode, x.ExpiryDate }).Any(g => g.Count() > 1)) throw new InvalidOperationException("Phiếu cần mặt hàng hợp lệ, số lượng dương, giá không âm, lô duy nhất và đơn vị đúng định dạng.");
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
                if (request.DispatchNow)
                {
                    var candidates = await stocks.FindAsync(s => s.BranchId == request.FromBranchId && s.ProductId == input.ProductId && s.BatchCode == input.BatchCode && s.ExpiryDate == input.ExpiryDate, ct);
                    if (candidates.Count != 1) throw new InvalidOperationException("Không xác định duy nhất lô tồn nguồn; cần mã lô và hạn dùng chính xác.");
                    var stock = candidates[0]; var available = stock.QuantityOnHand - stock.QuantityReserved - stock.QuantityQuarantined;
                    if (available < input.Quantity) throw new InvalidOperationException($"Không đủ tồn khả dụng cho {product.Name}; hiện có {available}.");
                    stock.QuantityOnHand -= input.Quantity; stocks.Update(stock);
                }
                var detail = new TransferDetail { TenantId = tenant.CurrentTenantId, TransferId = transfer.Id, ProductId = input.ProductId, SentQuantity = input.Quantity, PackagingUnit = input.Unit.Trim(), BatchNumber = input.BatchCode, ExpiryDate = input.ExpiryDate, UnitPrice = input.UnitPrice, Amount = input.Quantity * input.UnitPrice };
                transfer.Details.Add(detail);
                await details.AddAsync(detail, ct);
            }
            transfer.TotalAmount = transfer.Details.Sum(x => x.Amount);
            await transfers.AddAsync(transfer, ct); await unitOfWork.SaveChangesAsync(ct); await unitOfWork.CommitTransactionAsync(ct);
            return await MapAsync(transfer, ct);
        }
        catch { await unitOfWork.RollbackTransactionAsync(ct); throw; }
    }

    public async Task<TransferDto> ReceiveTransferAsync(Guid id, ReceiveTransferRequest request, CancellationToken ct)
    {
        var transfer = await RequireAsync(id, ct); EnsureBranch(transfer.ToBranchId);
        if (transfer.Status != TransferStatus.InTransit) throw new InvalidOperationException("Chỉ nhận được phiếu đang vận chuyển.");
        if (request.Items is null) throw new InvalidOperationException("Thiếu thông tin kiểm đếm mặt hàng.");
        var existingLines = transfer.Details.Count > 0 ? transfer.Details : await details.FindAsync(x => x.TransferId == id, ct);
        var lines = existingLines.ToDictionary(x => x.Id);
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
                var candidates = await stocks.FindAsync(s => s.BranchId == transfer.ToBranchId && s.ProductId == line.ProductId && s.BatchCode == line.BatchNumber && s.ExpiryDate == line.ExpiryDate, ct);
                if (candidates.Count > 1) throw new InvalidOperationException("Lô nhận không duy nhất vì tồn tại nhiều nhà cung cấp.");
                if (candidates.Count == 0) await stocks.AddAsync(new BranchStock { TenantId = tenant.CurrentTenantId, BranchId = transfer.ToBranchId, ProductId = line.ProductId, BatchCode = line.BatchNumber, ExpiryDate = line.ExpiryDate, QuantityOnHand = input.ReceivedQuantity + input.DamagedQuantity, QuantityQuarantined = input.DamagedQuantity, LandedCostPerUnit = line.UnitPrice }, ct);
                else { candidates[0].QuantityOnHand += input.ReceivedQuantity + input.DamagedQuantity; candidates[0].QuantityQuarantined += input.DamagedQuantity; stocks.Update(candidates[0]); }
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
        var lines = transfer.Details.Count > 0 ? transfer.Details : await details.FindAsync(x => x.TransferId == id, ct);
        await unitOfWork.BeginTransactionAsync(ct);
        try
        {
            foreach (var line in lines)
            {
                var candidates = await stocks.FindAsync(s => s.BranchId == transfer.FromBranchId && s.ProductId == line.ProductId && s.BatchCode == line.BatchNumber && s.ExpiryDate == line.ExpiryDate, ct);
                if (candidates.Count != 1) throw new InvalidOperationException("Không xác định duy nhất lô tồn nguồn; cần mã lô và hạn dùng chính xác.");
                var stock = candidates[0];
                var available = stock.QuantityOnHand - stock.QuantityReserved - stock.QuantityQuarantined;
                if (available < line.SentQuantity) throw new InvalidOperationException($"Không đủ tồn khả dụng; còn {available}.");
                stock.QuantityOnHand -= line.SentQuantity;
                stocks.Update(stock);
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
            {
                var lines = transfer.Details.Count > 0 ? transfer.Details : await details.FindAsync(x => x.TransferId == id, ct);
                foreach (var line in lines)
                {
                    var candidates = await stocks.FindAsync(s => s.BranchId == transfer.FromBranchId && s.ProductId == line.ProductId && s.BatchCode == line.BatchNumber && s.ExpiryDate == line.ExpiryDate, ct);
                    if (candidates.Count != 1) throw new InvalidOperationException("Không thể hoàn tồn vì lô nguồn không duy nhất.");
                    candidates[0].QuantityOnHand += line.SentQuantity; stocks.Update(candidates[0]);
                }
            }
            transfer.Status = TransferStatus.Cancelled; transfer.CancelReason = request.Reason.Trim(); transfers.Update(transfer);
            await unitOfWork.SaveChangesAsync(ct); await unitOfWork.CommitTransactionAsync(ct); return await MapAsync(transfer, ct);
        }
        catch { await unitOfWork.RollbackTransactionAsync(ct); throw; }
    }

    private async Task<Transfer> RequireAsync(Guid id, CancellationToken ct)
    {
        var transfer = await transfers.GetByIdAsync(id, ct) ?? throw new KeyNotFoundException("Không tìm thấy phiếu điều chuyển.");
        if (!string.IsNullOrEmpty(tenant.CurrentTenantId) && !string.IsNullOrEmpty(transfer.TenantId) && !string.Equals(transfer.TenantId, tenant.CurrentTenantId, StringComparison.Ordinal))
            throw new KeyNotFoundException("Không tìm thấy phiếu điều chuyển.");
        if (!IsOwner && transfer.FromBranchId != tenant.CurrentBranchId && transfer.ToBranchId != tenant.CurrentBranchId) throw new UnauthorizedAccessException("Không có quyền truy cập phiếu.");
        return transfer;
    }
    private void EnsureBranch(Guid branchId) { if (!IsOwner && (tenant.CurrentBranchId is not Guid currentBranch || currentBranch != branchId)) throw new UnauthorizedAccessException("Không có quyền thao tác tại chi nhánh này."); }
    private async Task<TransferDto> MapAsync(Transfer transfer, CancellationToken ct)
    {
        var from = await branches.GetByIdAsync(transfer.FromBranchId, ct); var to = await branches.GetByIdAsync(transfer.ToBranchId, ct); var items = new List<TransferLineDto>();
        var lines = transfer.Details.Count > 0 ? transfer.Details : await details.FindAsync(x => x.TransferId == transfer.Id, ct);
        foreach (var line in lines)
        {
            var product = await products.GetByIdAsync(line.ProductId, ct);
            items.Add(new TransferLineDto(line.Id, line.ProductId, product?.ProductCode ?? "", product?.Name ?? "", line.PackagingUnit, line.SentQuantity, line.ReceivedQuantity, line.DamagedQuantity, line.BatchNumber, line.ExpiryDate, line.UnitPrice, line.Amount, line.DiscrepancyReason));
        }
        return new TransferDto(transfer.Id, transfer.TransferCode, transfer.FromBranchId, from?.Name ?? "", transfer.ToBranchId, to?.Name ?? "", transfer.Status.ToString(), transfer.CreatedAt, transfer.DispatchedAt, transfer.ReceivedAt, transfer.ExpectedDeliveryDate, transfer.CreatedByStaff, transfer.TotalAmount, transfer.Note, transfer.DiscrepancyNote, transfer.CancelReason, items);
    }
}