using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Stocktakes.Dtos;
using MT_GRMS.Application.Suppliers;
using MT_GRMS.Application.SupplierReturns;
using MT_GRMS.Application.SupplierReturns.Dtos;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Domain.Exceptions;

namespace MT_GRMS.Application.Stocktakes;

public class StocktakeService : IStocktakeService
{
    private readonly IStocktakeRepository _repository;
    private readonly ISupplierReturnService _supplierReturnService;
    private readonly ISupplierReturnRepository _supplierReturnRepository;
    private readonly ISupplierRepository _supplierRepository;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IRepository<Package>? _packages;

    public StocktakeService(
        IStocktakeRepository repository,
        ISupplierReturnService supplierReturnService,
        ISupplierReturnRepository supplierReturnRepository,
        ISupplierRepository supplierRepository,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork,
        IRepository<Package>? packages = null)
    {
        _repository = repository;
        _supplierReturnService = supplierReturnService;
        _supplierReturnRepository = supplierReturnRepository;
        _supplierRepository = supplierRepository;
        _tenantContext = tenantContext;
        _unitOfWork = unitOfWork;
        _packages = packages;
    }

    public async Task<PagedResult<StocktakeListItemDto>> GetPagedAsync(
        StocktakeFilterQuery query,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.CurrentTenantId ?? string.Empty;
        var effectiveQuery = EnforceBranchScopeForEmployee(query);
        return await _repository.GetPagedAsync(tenantId, effectiveQuery, cancellationToken);
    }

    public async Task<StocktakeDetailDto> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.CurrentTenantId ?? string.Empty;
        var detail = await _repository.GetDetailDtoByIdAsync(tenantId, id, cancellationToken);
        if (detail == null)
            throw new NotFoundException($"Không tìm thấy phiếu kiểm kê với ID {id}.");

        EnsureEmployeeCanAccessBranch(detail.BranchId);

        return detail;
    }

    public async Task<StocktakeDetailDto> CreateAsync(
        CreateStocktakeCommand command,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.CurrentTenantId ?? string.Empty;
        var branchId = ResolveBranchIdForCreate(command.BranchId);

        if (branchId == Guid.Empty)
            throw new BusinessException(
                IsEmployeeRole()
                    ? "Tài khoản chưa được gán chi nhánh — không thể lập phiếu kiểm kê."
                    : "Vui lòng chọn chi nhánh thực hiện kiểm kê.");

        // Employee: luôn kiểm kê toàn bộ tồn có QOH > 0 của chi nhánh được phân công (không tin scope từ FE).
        var scope = IsEmployeeRole()
            ? "ALL"
            : (string.IsNullOrWhiteSpace(command.Scope) ? "LOT" : command.Scope.Trim().ToUpperInvariant());
        var reasonType = string.IsNullOrWhiteSpace(command.ReasonType) ? "PERIODIC" : command.ReasonType.Trim().ToUpperInvariant();
        if (reasonType == "CUSTOMER_RETURN")
            throw new BusinessException("Phiếu kiểm kê hàng lỗi đổi/trả chỉ được tạo tự động từ giao dịch đổi/trả.");

        var stocks = await _repository.GetBranchStocksForScopeAsync(
            tenantId,
            branchId,
            scope,
            command.BranchStockIds,
            command.CategoryId,
            cancellationToken);

        if (!stocks.Any() && (command.Lines == null || !command.Lines.Any()))
            throw new BusinessException("Không tìm thấy lô hàng hoặc tồn kho nào thuộc phạm vi kiểm kê đã chọn.");

        var stockIds = stocks.Select(s => s.Id).ToList();
        var grnTraces = await _repository.GetGrnTraceByStockIdsAsync(tenantId, stockIds, cancellationToken);

        var inputLookup = (command.Lines ?? new List<StocktakeLineInputDto>())
            .GroupBy(x => x.BranchStockId ?? x.ProductId)
            .ToDictionary(
                g => g.Key,
                g => g.First());

        var lines = new List<StocktakeLine>();
        foreach (var stock in stocks)
        {
            inputLookup.TryGetValue(stock.Id, out var input);
            if (input == null)
            {
                inputLookup.TryGetValue(stock.ProductId, out input);
            }

            var systemQty = stock.QuantityOnHand;
            var goodQty = input != null ? input.GoodQty : systemQty;
            var defectiveQty = input?.DefectiveQty ?? 0m;
            var damagedQty = input?.DamagedQty ?? 0m;
            var nearExpiryQty = input?.NearExpiryQty ?? 0m;
            var expiredQty = input?.ExpiredQty ?? 0m;

            ValidateNonNegativeQuantities(goodQty, defectiveQty, damagedQty, nearExpiryQty, expiredQty);

            var actualQty = goodQty + defectiveQty + damagedQty + nearExpiryQty + expiredQty;
            var varianceQty = actualQty - systemQty;

            grnTraces.TryGetValue(stock.Id, out var trace);

            lines.Add(new StocktakeLine
            {
                TenantId = tenantId,
                ProductId = stock.ProductId,
                BranchStockId = stock.Id,
                BatchCode = stock.BatchCode,
                ManufactureDate = stock.ManufactureDate,
                ExpiryDate = stock.ExpiryDate,
                UnitCost = stock.LandedCostPerUnit,
                SystemQty = systemQty,
                GoodQty = goodQty,
                DefectiveQty = defectiveQty,
                DamagedQty = damagedQty,
                NearExpiryQty = nearExpiryQty,
                ExpiredQty = expiredQty,
                ActualQty = actualQty,
                VarianceQty = varianceQty,
                DefectiveReasonCode = defectiveQty > 0
                    ? (string.IsNullOrWhiteSpace(input?.DefectiveReasonCode) ? "DEFECTIVE_SUPPLIER" : input!.DefectiveReasonCode)
                    : null,
                NearExpiryAction = nearExpiryQty > 0
                    ? (string.IsNullOrWhiteSpace(input?.NearExpiryAction) ? "NEAR_EXPIRY_MARKDOWN" : input!.NearExpiryAction)
                    : null,
                ReturnedQty = 0m,
                OriginalGrnId = trace.GrnId != Guid.Empty ? trace.GrnId : null,
                OriginalGrnLineId = trace.GrnLineId != Guid.Empty ? trace.GrnLineId : null,
                OriginalGrnCode = trace.GrnCode,
                SupplierId = stock.SupplierId ?? (trace.SupplierId != Guid.Empty ? trace.SupplierId : null),
                EvidenceUrl = input?.EvidenceUrl,
                Note = input?.Note
            });
        }

        var code = await _repository.GenerateCodeAsync(tenantId, cancellationToken);
        var stocktake = new Stocktake
        {
            TenantId = tenantId,
            Code = code,
            BranchId = branchId,
            Scope = scope,
            ReasonType = reasonType,
            Status = command.AutoSubmit ? "PENDING_APPROVAL" : "DRAFT",
            CreatedBy = _tenantContext.CurrentUserId != Guid.Empty ? _tenantContext.CurrentUserId : null,
            CreatedByName = string.IsNullOrWhiteSpace(command.CreatedByName) ? "Nhân viên kiểm kê" : command.CreatedByName,
            StartedAt = DateTime.UtcNow,
            SubmittedAt = command.AutoSubmit ? DateTime.UtcNow : null,
            Note = command.Note,
            Lines = lines
        };

        await _repository.AddAsync(stocktake, cancellationToken);

        // Draft / gửi duyệt: không điều chỉnh OnHand / Quarantine — chỉ Owner APPROVE mới đổi tồn theo từng BranchStockId.
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(stocktake.Id, cancellationToken);
    }

    public async Task<StocktakeDetailDto> UpdateAsync(
        Guid id,
        UpdateStocktakeCommand command,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.CurrentTenantId ?? string.Empty;
        var entity = await _repository.GetByIdAsync(tenantId, id, cancellationToken);
        if (entity == null)
            throw new NotFoundException($"Không tìm thấy phiếu kiểm kê với ID {id}.");

        EnsureEmployeeCanAccessBranch(entity.BranchId);

        if (entity.Status != "DRAFT" && entity.Status != "NEEDS_RECOUNT")
            throw new BusinessException($"Chỉ được sửa phiếu kiểm kê ở trạng thái DRAFT hoặc NEEDS_RECOUNT (hiện tại: {entity.Status}).");

        if (entity.ReasonType == "CUSTOMER_RETURN" && !string.IsNullOrWhiteSpace(command.ReasonType) &&
            !string.Equals(command.ReasonType.Trim(), "CUSTOMER_RETURN", StringComparison.OrdinalIgnoreCase))
            throw new BusinessException("Không thể đổi loại phiếu kiểm kê phát sinh từ hàng lỗi đổi/trả.");

        if (!string.IsNullOrWhiteSpace(command.ReasonType))
            entity.ReasonType = command.ReasonType.Trim().ToUpperInvariant();

        if (command.Note != null)
            entity.Note = command.Note;

        if (command.Lines != null && command.Lines.Any())
        {
            var matchedLineIds = new HashSet<Guid>();
            var newBranchStockIds = command.Lines
                .Where(i => i.BranchStockId.HasValue && !entity.Lines.Any(l =>
                    (i.Id.HasValue && l.Id == i.Id.Value) ||
                    (l.BranchStockId.HasValue && l.BranchStockId.Value == i.BranchStockId.Value)))
                .Select(i => i.BranchStockId!.Value)
                .Distinct()
                .ToList();

            var newGrnTraces = newBranchStockIds.Count > 0
                ? await _repository.GetGrnTraceByStockIdsAsync(tenantId, newBranchStockIds, cancellationToken)
                : new Dictionary<Guid, (Guid GrnId, Guid GrnLineId, string GrnCode, Guid SupplierId)>();

            foreach (var input in command.Lines)
            {
                // Match theo line Id hoặc BranchStockId (lô). Không fallback ProductId — 1 SP nhiều lô.
                var line = entity.Lines.FirstOrDefault(l =>
                    (input.Id.HasValue && l.Id == input.Id.Value) ||
                    (input.BranchStockId.HasValue && l.BranchStockId == input.BranchStockId.Value));

                ValidateNonNegativeQuantities(input.GoodQty, input.DefectiveQty, input.DamagedQty, input.NearExpiryQty, input.ExpiredQty);

                if (line == null)
                {
                    if (entity.ReasonType == "CUSTOMER_RETURN" || !input.BranchStockId.HasValue)
                        continue;

                    var stock = await _repository.GetBranchStockByIdAsync(tenantId, input.BranchStockId.Value, cancellationToken);
                    if (stock == null || stock.BranchId != entity.BranchId)
                        continue;

                    newGrnTraces.TryGetValue(stock.Id, out var trace);
                    var newActual = input.GoodQty + input.DefectiveQty + input.DamagedQty + input.NearExpiryQty + input.ExpiredQty;
                    var newLine = new StocktakeLine
                    {
                        TenantId = tenantId,
                        StocktakeId = entity.Id,
                        ProductId = stock.ProductId,
                        BranchStockId = stock.Id,
                        BatchCode = stock.BatchCode,
                        ManufactureDate = stock.ManufactureDate,
                        ExpiryDate = stock.ExpiryDate,
                        UnitCost = stock.LandedCostPerUnit,
                        SystemQty = stock.QuantityOnHand,
                        GoodQty = input.GoodQty,
                        DefectiveQty = input.DefectiveQty,
                        DamagedQty = input.DamagedQty,
                        NearExpiryQty = input.NearExpiryQty,
                        ExpiredQty = input.ExpiredQty,
                        ActualQty = newActual,
                        VarianceQty = newActual - stock.QuantityOnHand,
                        DefectiveReasonCode = input.DefectiveQty > 0
                            ? (string.IsNullOrWhiteSpace(input.DefectiveReasonCode) ? "DEFECTIVE_SUPPLIER" : input.DefectiveReasonCode)
                            : null,
                        NearExpiryAction = input.NearExpiryQty > 0
                            ? (string.IsNullOrWhiteSpace(input.NearExpiryAction) ? "NEAR_EXPIRY_MARKDOWN" : input.NearExpiryAction)
                            : null,
                        ReturnedQty = 0m,
                        OriginalGrnId = trace.GrnId != Guid.Empty ? trace.GrnId : null,
                        OriginalGrnLineId = trace.GrnLineId != Guid.Empty ? trace.GrnLineId : null,
                        OriginalGrnCode = trace.GrnCode,
                        SupplierId = stock.SupplierId ?? (trace.SupplierId != Guid.Empty ? trace.SupplierId : null),
                        EvidenceUrl = input.EvidenceUrl,
                        Note = input.Note
                    };
                    entity.Lines.Add(newLine);
                    matchedLineIds.Add(newLine.Id);
                    continue;
                }

                matchedLineIds.Add(line.Id);

                if (entity.ReasonType == "CUSTOMER_RETURN" &&
                    (input.GoodQty != line.GoodQty || input.DefectiveQty != line.DefectiveQty ||
                     input.DamagedQty != line.DamagedQty || input.NearExpiryQty != line.NearExpiryQty ||
                     input.ExpiredQty != line.ExpiredQty))
                    throw new BusinessException("Số lượng trên phiếu kiểm kê hàng lỗi đổi/trả đã được cách ly; chỉ được cập nhật ghi chú và bằng chứng.");

                if (entity.Status == "NEEDS_RECOUNT" && line.BranchStockId.HasValue)
                {
                    var currentStock = await _repository.GetBranchStockByIdAsync(tenantId, line.BranchStockId.Value, cancellationToken);
                    if (currentStock != null)
                    {
                        line.SystemQty = currentStock.QuantityOnHand;
                    }
                }

                line.GoodQty = input.GoodQty;
                line.DefectiveQty = input.DefectiveQty;
                line.DamagedQty = input.DamagedQty;
                line.NearExpiryQty = input.NearExpiryQty;
                line.ExpiredQty = input.ExpiredQty;
                line.ActualQty = input.GoodQty + input.DefectiveQty + input.DamagedQty + input.NearExpiryQty + input.ExpiredQty;
                line.VarianceQty = line.ActualQty - line.SystemQty;
                line.DefectiveReasonCode = input.DefectiveQty > 0
                    ? (string.IsNullOrWhiteSpace(input.DefectiveReasonCode) ? "DEFECTIVE_SUPPLIER" : input.DefectiveReasonCode)
                    : null;
                line.NearExpiryAction = input.NearExpiryQty > 0
                    ? (string.IsNullOrWhiteSpace(input.NearExpiryAction) ? "NEAR_EXPIRY_MARKDOWN" : input.NearExpiryAction)
                    : null;
                line.EvidenceUrl = input.EvidenceUrl;
                line.Note = input.Note;
            }

            if (entity.ReasonType != "CUSTOMER_RETURN")
            {
                var toRemove = entity.Lines.Where(l => !matchedLineIds.Contains(l.Id)).ToList();
                foreach (var rem in toRemove)
                {
                    entity.Lines.Remove(rem);
                }
            }
        }

        if (command.AutoSubmit)
        {
            entity.Status = "PENDING_APPROVAL";
            entity.SubmittedAt = DateTime.UtcNow;
        }

        await _repository.UpdateAsync(entity, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(entity.Id, cancellationToken);
    }

    public async Task<StocktakeDetailDto> SubmitAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.CurrentTenantId ?? string.Empty;
        var entity = await _repository.GetByIdAsync(tenantId, id, cancellationToken);
        if (entity == null)
            throw new NotFoundException($"Không tìm thấy phiếu kiểm kê với ID {id}.");

        EnsureEmployeeCanAccessBranch(entity.BranchId);

        if (entity.Status != "DRAFT" && entity.Status != "NEEDS_RECOUNT")
            throw new BusinessException($"Chỉ có thể gửi duyệt phiếu kiểm kê từ trạng thái DRAFT hoặc NEEDS_RECOUNT (hiện tại: {entity.Status}).");

        entity.Status = "PENDING_APPROVAL";
        entity.SubmittedAt = DateTime.UtcNow;

        // Không quarantine / không đổi tồn khi gửi duyệt.
        await _repository.UpdateAsync(entity, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(entity.Id, cancellationToken);
    }

    public async Task<StocktakeDetailDto> ReviewAsync(
        Guid id,
        ReviewStocktakeCommand command,
        CancellationToken cancellationToken = default)
    {
        if (IsEmployeeRole())
            throw new BusinessException("Nhân viên (Employee) không được phép tự duyệt hoặc từ chối Phiếu kiểm kê.");

        var tenantId = _tenantContext.CurrentTenantId ?? string.Empty;
        var entity = await _repository.GetByIdAsync(tenantId, id, cancellationToken);
        if (entity == null)
            throw new NotFoundException($"Không tìm thấy phiếu kiểm kê với ID {id}.");

        if (entity.Status != "PENDING_APPROVAL")
            throw new BusinessException($"Chỉ có thể duyệt phiếu kiểm kê ở trạng thái PENDING_APPROVAL (hiện tại: {entity.Status}).");

        var decision = (command.Decision ?? string.Empty).Trim().ToUpperInvariant();
        entity.ReviewedByUserId = _tenantContext.CurrentUserId != Guid.Empty ? _tenantContext.CurrentUserId : null;
        entity.ReviewedByName = string.IsNullOrWhiteSpace(command.ReviewedByName) ? "Chủ cửa hàng (Store Owner)" : command.ReviewedByName;
        entity.ReviewedAt = DateTime.UtcNow;
        entity.ReviewNote = command.ReviewNote;

        if (decision == "APPROVE" || decision == "APPROVED")
        {
            var surplusLines = entity.Lines.Where(l => l.VarianceQty > 0).ToList();
            var surplusTotal = surplusLines.Sum(l => l.VarianceQty);
            if (surplusTotal > 0 && !command.ConfirmSurplusFound)
            {
                throw new BusinessException(
                    "SURPLUS_CONFIRM_REQUIRED",
                    $"Phiếu có hàng thừa (tổng +{surplusTotal:0.##}). Store Owner phải xác minh nguồn thừa (ConfirmSurplusFound=true) trước khi duyệt cộng tồn.");
            }

            ApplyLineDispositionActions(entity, command.LineActions);
            await ValidateReturnDispositionsAgainstPolicyAsync(tenantId, entity, cancellationToken);

            entity.Status = "APPROVED";

            // Chỉ điều chỉnh đúng BranchStock gắn với dòng (Batch/lô). Không FEFO / không phân bổ sang lô khác.
            foreach (var line in entity.Lines)
            {
                if (!line.BranchStockId.HasValue) continue;
                var stock = await _repository.GetBranchStockByIdAsync(tenantId, line.BranchStockId.Value, cancellationToken);
                if (stock == null) continue;
                if (stock.QuantityOnHand != line.SystemQty)
                    throw new BusinessException("STOCKTAKE_STALE_SNAPSHOT", "Tồn của lô đã thay đổi sau khi lập/đếm phiếu; cần kiểm lại trước khi duyệt.");

                // 1. Hết hạn / hư hỏng chọn WRITE_OFF → trừ OnHand.
                //    Chọn RETURN_SUPPLIER → giữ OnHand + đưa vào Quarantine (không bán được) đến khi gửi phiếu trả.
                var writeOffQty = GetLineWriteOffQty(line);
                if (writeOffQty > 0)
                {
                    if (entity.ReasonType == "CUSTOMER_RETURN")
                        stock.QuantityQuarantined = Math.Max(0m, stock.QuantityQuarantined - writeOffQty);
                    stock.QuantityOnHand = Math.Max(0, stock.QuantityOnHand - writeOffQty);
                }

                var returnHoldQty = GetLineEligibleReturnQty(line);
                if (returnHoldQty > 0 && entity.ReasonType != "CUSTOMER_RETURN")
                {
                    stock.QuantityQuarantined += returnHoldQty;
                }

                // 2. Thiếu (Variance < 0) → giảm OnHand đúng lô
                if (line.VarianceQty < 0)
                {
                    var missingQty = Math.Abs(line.VarianceQty);
                    stock.QuantityOnHand = Math.Max(0, stock.QuantityOnHand - missingQty);
                }

                // 3. Thừa (Variance > 0) — chỉ khi Owner ConfirmSurplusFound → cộng đúng lô
                if (line.VarianceQty > 0 && command.ConfirmSurplusFound)
                {
                    stock.QuantityOnHand += line.VarianceQty;
                }

                var packageWeight = _packages == null ? 0m : (await _packages.FindAsync(p => p.TenantId == tenantId
                    && p.BranchStockId == stock.Id && !p.IsDeleted && (p.Status == "AVAILABLE" || p.Status == "QUARANTINED"), cancellationToken)).Sum(p => p.NetWeight);
                if (stock.QuantityOnHand < stock.QuantityReserved + stock.QuantityQuarantined + packageWeight)
                    throw new BusinessException("STOCKTAKE_COMMITTED_STOCK", "Kiểm kê làm tồn thấp hơn lượng khay, cách ly hoặc giữ chỗ; cần đối soát từng phần trước khi duyệt.");

                stock.UpdatedAt = DateTime.UtcNow;
            }
        }
        else if (decision == "REJECT" || decision == "REJECTED")
        {
            entity.Status = "REJECTED";
        }
        else if (decision == "NEEDS_RECOUNT" || decision == "RECOUNT")
        {
            entity.Status = "NEEDS_RECOUNT";
        }
        else
        {
            throw new BusinessException($"Quyết định duyệt '{command.Decision}' không hợp lệ. Chỉ chấp nhận APPROVE, REJECT hoặc NEEDS_RECOUNT.");
        }

        await _repository.UpdateAsync(entity, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(entity.Id, cancellationToken);
    }

    public async Task<IReadOnlyList<SupplierReturnDetailDto>> CreateReturnFromStocktakeAsync(
        Guid stocktakeId,
        CreateReturnFromStocktakeCommand command,
        CancellationToken cancellationToken = default)
    {
        if (IsEmployeeRole())
            throw new BusinessException("Chỉ Store Owner mới có quyền tạo Phiếu trả hàng nhà cung cấp từ kết quả kiểm kê.");

        var tenantId = _tenantContext.CurrentTenantId ?? string.Empty;
        var stocktake = await _repository.GetByIdAsync(tenantId, stocktakeId, cancellationToken);
        if (stocktake == null)
            throw new NotFoundException($"Không tìm thấy phiếu kiểm kê với ID {stocktakeId}.");

        if (stocktake.Status != "APPROVED")
            throw new BusinessException("Không được tạo Phiếu trả hàng nhà cung cấp từ Phiếu kiểm kê chưa được phê duyệt (APPROVED).");

        if (command.Lines == null || !command.Lines.Any())
            throw new BusinessException("Vui lòng chọn ít nhất một dòng hàng đủ điều kiện trả nhà cung cấp.");

        var fallbackGrnId = command.GoodsReceiptId;
        var prepared = new List<(StocktakeLine StLine, CreateReturnFromStocktakeLineItemDto Req, Guid GrnId, string ReasonType, string ReasonText)>();

        foreach (var reqItem in command.Lines)
        {
            var stLine = stocktake.Lines.FirstOrDefault(l => l.Id == reqItem.StocktakeLineId);
            if (stLine == null)
                throw new BusinessException($"Dòng kiểm kê {reqItem.StocktakeLineId} không thuộc phiếu {stocktake.Code}.");

            var eligibleReturnQty = GetLineEligibleReturnQty(stLine);
            var remainingEligibleQty = Math.Max(0, eligibleReturnQty - stLine.ReturnedQty);

            if (reqItem.RequestedQty <= 0)
                throw new BusinessException("Số lượng đề nghị trả phải lớn hơn 0.");

            if (reqItem.RequestedQty > remainingEligibleQty)
                throw new BusinessException(
                    $"Số lượng đề nghị trả ({reqItem.RequestedQty}) vượt quá số lượng lỗi/cận hạn còn lại trên dòng kiểm kê ({remainingEligibleQty}).");

            var resolvedGrnId = reqItem.GoodsReceiptId ?? stLine.OriginalGrnId ?? fallbackGrnId;
            if (resolvedGrnId == Guid.Empty)
                throw new BusinessException("Vui lòng chọn Phiếu nhập kho gốc (GRN) để xác định nguồn nhập và Nhà cung cấp.");

            var (reasonText, reasonType) = ResolveReturnReason([stLine]);
            prepared.Add((stLine, reqItem, resolvedGrnId, reasonType, reasonText));
            stLine.ReturnedQty += reqItem.RequestedQty;
        }

        await _repository.UpdateAsync(stocktake, cancellationToken);

        var created = new List<SupplierReturnDetailDto>();
        var groups = prepared.GroupBy(x => (x.GrnId, x.ReasonType));

        foreach (var group in groups)
        {
            var groupLines = group.ToList();
            var reasonType = group.Key.ReasonType;
            var primaryReason = groupLines[0].ReasonText;
            var attachment = command.AttachmentUrl
                ?? string.Join(
                    ";",
                    groupLines
                        .Select(x => x.StLine.EvidenceUrl)
                        .Where(u => !string.IsNullOrWhiteSpace(u))
                        .Distinct());
            if (string.IsNullOrWhiteSpace(attachment))
                attachment = stocktake.Lines.Select(l => l.EvidenceUrl).FirstOrDefault(u => !string.IsNullOrWhiteSpace(u));

            var returnLinesToCreate = groupLines.Select(x => new CreateSupplierReturnLineDto(
                ProductId: x.StLine.ProductId,
                GoodsReceiptLineId: x.Req.GoodsReceiptLineId ?? x.StLine.OriginalGrnLineId,
                BatchCode: x.StLine.BatchCode,
                ExpiryDate: x.StLine.ExpiryDate,
                RequestedQty: x.Req.RequestedQty,
                UnitCost: x.StLine.UnitCost,
                Notes: x.Req.Notes ?? x.StLine.Note ?? $"Từ phiếu kiểm kê {stocktake.Code}",
                StocktakeLineId: x.StLine.Id
            )).ToList();

            var createReturnCmd = new CreateSupplierReturnCommand(
                GoodsReceiptId: group.Key.GrnId,
                Reason: primaryReason,
                ReasonNotes: string.IsNullOrWhiteSpace(command.ReasonNotes)
                    ? $"Khởi tạo từ Phiếu kiểm kê {stocktake.Code}"
                    : command.ReasonNotes,
                AttachmentUrl: attachment,
                Lines: returnLinesToCreate,
                AutoSubmit: command.AutoSubmit,
                StocktakeId: stocktake.Id,
                ResolutionType: NormalizeStocktakeResolution(command.ResolutionType),
                ReturnReasonType: reasonType
            );

            var createdReturn = await _supplierReturnService.CreateAsync(createReturnCmd, cancellationToken);

            var returnEntity = await _supplierReturnRepository.GetByIdAsync(tenantId, createdReturn.Id, cancellationToken);
            if (returnEntity != null)
            {
                returnEntity.StocktakeId = stocktake.Id;
                returnEntity.StocktakeCode = stocktake.Code;
                await _supplierReturnRepository.UpdateAsync(returnEntity, cancellationToken);
            }

            created.Add(await _supplierReturnService.GetByIdAsync(createdReturn.Id, cancellationToken));
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return created;
    }

    public static decimal GetLineEligibleReturnQty(StocktakeLine line)
    {
        var nearExpiryReturnable = line.NearExpiryAction == "NEAR_EXPIRY_RETURN" ? line.NearExpiryQty : 0m;
        var damagedReturnable = IsReturnDisposition(line.DamagedAction) ? line.DamagedQty : 0m;
        var expiredReturnable = IsReturnDisposition(line.ExpiredAction) ? line.ExpiredQty : 0m;
        return line.DefectiveQty + nearExpiryReturnable + damagedReturnable + expiredReturnable;
    }

    public static decimal GetLineWriteOffQty(StocktakeLine line)
    {
        var damaged = IsReturnDisposition(line.DamagedAction) ? 0m : line.DamagedQty;
        var expired = IsReturnDisposition(line.ExpiredAction) ? 0m : line.ExpiredQty;
        return damaged + expired;
    }

    public static bool IsReturnDisposition(string? action)
    {
        if (string.IsNullOrWhiteSpace(action)) return false;
        var raw = action.Trim().ToUpperInvariant();
        return raw is "RETURN_SUPPLIER" or "NEAR_EXPIRY_RETURN" or "RETURN";
    }

    private static string? NormalizeDispositionAction(string? action, decimal qty)
    {
        if (qty <= 0) return null;
        if (IsReturnDisposition(action)) return "RETURN_SUPPLIER";
        return "WRITE_OFF";
    }

    private static void ApplyLineDispositionActions(
        Stocktake entity,
        List<ReviewStocktakeLineActionDto>? lineActions)
    {
        if (lineActions == null || lineActions.Count == 0)
        {
            foreach (var line in entity.Lines)
            {
                if (line.DamagedQty > 0 && string.IsNullOrWhiteSpace(line.DamagedAction))
                    line.DamagedAction = "WRITE_OFF";
                if (line.ExpiredQty > 0 && string.IsNullOrWhiteSpace(line.ExpiredAction))
                    line.ExpiredAction = "WRITE_OFF";
            }
            return;
        }

        var lookup = lineActions
            .GroupBy(x => x.StocktakeLineId)
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var line in entity.Lines)
        {
            if (lookup.TryGetValue(line.Id, out var action))
            {
                line.DamagedAction = line.DamagedQty > 0
                    ? NormalizeDispositionAction(action.DamagedAction, line.DamagedQty)
                    : null;
                line.ExpiredAction = line.ExpiredQty > 0
                    ? NormalizeDispositionAction(action.ExpiredAction, line.ExpiredQty)
                    : null;
            }
            else
            {
                if (line.DamagedQty > 0 && string.IsNullOrWhiteSpace(line.DamagedAction))
                    line.DamagedAction = "WRITE_OFF";
                if (line.ExpiredQty > 0 && string.IsNullOrWhiteSpace(line.ExpiredAction))
                    line.ExpiredAction = "WRITE_OFF";
            }
        }
    }

    private static void ValidateNonNegativeQuantities(params decimal[] values)
    {
        if (values.Any(v => v < 0))
            throw new BusinessException("Số lượng kiểm kê theo từng phân loại không được nhỏ hơn 0.");
    }

    private bool IsEmployeeRole()
    {
        var role = (_tenantContext.CurrentRole ?? string.Empty).Trim().ToUpperInvariant();
        return role == "EMPLOYEE" || role == "STAFF" || role == "CASHIER";
    }

    /// <summary>
    /// Employee: BranchId luôn lấy từ token/tenant context — không tin giá trị frontend gửi lên.
    /// Store Owner / Admin: dùng BranchId từ command (Owner thường không gắn 1 CN cố định).
    /// </summary>
    private Guid ResolveBranchIdForCreate(Guid requestedBranchId)
    {
        if (IsEmployeeRole())
        {
            if (_tenantContext.CurrentBranchId.HasValue && _tenantContext.CurrentBranchId.Value != Guid.Empty)
            {
                if (requestedBranchId != Guid.Empty && requestedBranchId != _tenantContext.CurrentBranchId.Value)
                    throw new BusinessException("Nhân viên chỉ được lập phiếu kiểm kê cho chi nhánh được phân công.");

                return _tenantContext.CurrentBranchId.Value;
            }

            throw new BusinessException("Tài khoản nhân viên chưa được gán chi nhánh — không thể lập phiếu kiểm kê.");
        }

        // Store Owner / Admin: tin BranchId trên phiếu (đã chọn trên UI).
        return requestedBranchId;
    }

    private void EnsureEmployeeCanAccessBranch(Guid stocktakeBranchId)
    {
        if (!IsEmployeeRole()) return;

        if (!_tenantContext.CurrentBranchId.HasValue || _tenantContext.CurrentBranchId.Value == Guid.Empty)
            throw new BusinessException("Tài khoản nhân viên chưa được gán chi nhánh.");

        if (stocktakeBranchId != _tenantContext.CurrentBranchId.Value)
            throw new BusinessException("Bạn không có quyền xem hoặc chỉnh sửa phiếu kiểm kê của chi nhánh khác.");
    }

    private StocktakeFilterQuery EnforceBranchScopeForEmployee(StocktakeFilterQuery query)
    {
        var effectiveBranchId = BranchScopeGuard.ResolveEffectiveBranchId(_tenantContext, query.BranchId);
        return query with { BranchId = effectiveBranchId };
    }

    private static string NormalizeStocktakeResolution(string? resolutionType) =>
        SupplierReturnPolicyRules.NormalizeResolutionType(resolutionType);

    private static (string ReasonText, string ReasonType) ResolveReturnReason(IEnumerable<StocktakeLine> lines)
    {
        var list = lines.ToList();
        if (list.Any(l => l.DefectiveReasonCode == "RECALLED"))
            return ("Thỏa thuận thu hồi từ hãng", "RECALL");
        if (list.Any(l => l.DefectiveReasonCode == "WRONG_SPEC_DISCOVERED"))
            return ("Giao sai quy cách / mẫu mã", "WRONG_ITEM");
        if (list.Any(l => IsReturnDisposition(l.ExpiredAction) && l.ExpiredQty > 0))
            return ("Hàng hết hạn phát hiện khi kiểm kê", "EXPIRED");
        if (list.Any(l => l.NearExpiryAction == "NEAR_EXPIRY_RETURN" && l.NearExpiryQty > 0))
            return ("Sắp hết hạn (Near-Expiry)", "NEAR_EXPIRY");
        if (list.Any(l => IsReturnDisposition(l.DamagedAction) && l.DamagedQty > 0))
            return ("Hàng hư hỏng phát hiện khi kiểm kê", "QUALITY_DEFECT");
        return ("Hàng hư hỏng, lỗi chất lượng", "QUALITY_DEFECT");
    }

    private async Task ValidateReturnDispositionsAgainstPolicyAsync(
        string tenantId,
        Stocktake entity,
        CancellationToken cancellationToken)
    {
        var returnLines = entity.Lines
            .Where(l =>
                (IsReturnDisposition(l.DamagedAction) && l.DamagedQty > 0)
                || (IsReturnDisposition(l.ExpiredAction) && l.ExpiredQty > 0)
                || (l.NearExpiryAction == "NEAR_EXPIRY_RETURN" && l.NearExpiryQty > 0))
            .ToList();
        if (returnLines.Count == 0) return;

        var grnDates = new Dictionary<Guid, DateTime>();
        var grnIds = returnLines
            .Where(l => l.OriginalGrnId.HasValue && l.OriginalGrnId.Value != Guid.Empty)
            .Select(l => l.OriginalGrnId!.Value)
            .Distinct()
            .ToList();
        if (grnIds.Count > 0)
        {
            var eligible = await _supplierReturnRepository.GetEligibleGrnsAsync(
                tenantId,
                entity.BranchId,
                null,
                cancellationToken);
            foreach (var g in eligible.Where(x => grnIds.Contains(x.Id)))
                grnDates[g.Id] = g.ReceivedDate;
        }

        var policyCache = new Dictionary<Guid, Domain.Entities.Supplier.SupplierReturnPolicy?>();

        foreach (var line in returnLines)
        {
            if (!line.SupplierId.HasValue || line.SupplierId.Value == Guid.Empty)
                throw new BusinessException(
                    "SUPPLIER_REQUIRED",
                    $"Lô {(string.IsNullOrWhiteSpace(line.BatchCode) ? line.ProductId.ToString() : line.BatchCode)} chọn đổi/trả NCC nhưng chưa xác định nhà cung cấp.");

            var supplierId = line.SupplierId.Value;
            if (!policyCache.TryGetValue(supplierId, out var policy))
            {
                policy = await _supplierRepository.GetActiveReturnPolicyAsync(supplierId, cancellationToken);
                policyCache[supplierId] = policy;
            }

            DateTime? receiptDate = null;
            if (line.OriginalGrnId.HasValue && grnDates.TryGetValue(line.OriginalGrnId.Value, out var d))
                receiptDate = d;

            var hasEvidence = !string.IsNullOrWhiteSpace(line.EvidenceUrl);

            if (IsReturnDisposition(line.DamagedAction) && line.DamagedQty > 0)
            {
                SupplierReturnPolicyRules.EnsureDispositionAllowed(
                    policy,
                    "DAMAGED",
                    receiptDate,
                    hasEvidence);
            }

            if (IsReturnDisposition(line.ExpiredAction) && line.ExpiredQty > 0)
            {
                SupplierReturnPolicyRules.EnsureDispositionAllowed(
                    policy,
                    "EXPIRED",
                    receiptDate,
                    hasEvidence);
            }

            if (line.NearExpiryAction == "NEAR_EXPIRY_RETURN" && line.NearExpiryQty > 0)
            {
                SupplierReturnPolicyRules.EnsureDispositionAllowed(
                    policy,
                    "NEAR_EXPIRY",
                    receiptDate,
                    hasEvidence);
            }
        }
    }
}

