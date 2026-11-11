using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Stocktakes.Dtos;
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
    private readonly ITenantContext _tenantContext;
    private readonly IUnitOfWork _unitOfWork;

    public StocktakeService(
        IStocktakeRepository repository,
        ISupplierReturnService supplierReturnService,
        ISupplierReturnRepository supplierReturnRepository,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _supplierReturnService = supplierReturnService;
        _supplierReturnRepository = supplierReturnRepository;
        _tenantContext = tenantContext;
        _unitOfWork = unitOfWork;
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

        return detail;
    }

    public async Task<StocktakeDetailDto> CreateAsync(
        CreateStocktakeCommand command,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.CurrentTenantId ?? string.Empty;
        var branchId = command.BranchId;

        if (IsEmployeeRole() && _tenantContext.CurrentBranchId.HasValue && _tenantContext.CurrentBranchId.Value != Guid.Empty)
        {
            branchId = _tenantContext.CurrentBranchId.Value;
        }

        if (branchId == Guid.Empty)
            throw new BusinessException("Vui lòng chọn chi nhánh thực hiện kiểm kê.");

        var scope = string.IsNullOrWhiteSpace(command.Scope) ? "LOT" : command.Scope.Trim().ToUpperInvariant();
        var reasonType = string.IsNullOrWhiteSpace(command.ReasonType) ? "PERIODIC" : command.ReasonType.Trim().ToUpperInvariant();

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

        if (command.AutoSubmit)
        {
            await ApplyQuarantineOnSubmitAsync(tenantId, lines, cancellationToken);
        }

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

        if (entity.Status != "DRAFT" && entity.Status != "NEEDS_RECOUNT")
            throw new BusinessException($"Chỉ được sửa phiếu kiểm kê ở trạng thái DRAFT hoặc NEEDS_RECOUNT (hiện tại: {entity.Status}).");

        if (!string.IsNullOrWhiteSpace(command.ReasonType))
            entity.ReasonType = command.ReasonType.Trim().ToUpperInvariant();

        if (command.Note != null)
            entity.Note = command.Note;

        if (command.Lines != null && command.Lines.Any())
        {
            foreach (var input in command.Lines)
            {
                var line = entity.Lines.FirstOrDefault(l =>
                    (input.Id.HasValue && l.Id == input.Id.Value) ||
                    (input.BranchStockId.HasValue && l.BranchStockId == input.BranchStockId.Value) ||
                    l.ProductId == input.ProductId);

                if (line == null) continue;

                ValidateNonNegativeQuantities(input.GoodQty, input.DefectiveQty, input.DamagedQty, input.NearExpiryQty, input.ExpiredQty);

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
        }

        if (command.AutoSubmit)
        {
            entity.Status = "PENDING_APPROVAL";
            entity.SubmittedAt = DateTime.UtcNow;
            await ApplyQuarantineOnSubmitAsync(tenantId, entity.Lines, cancellationToken);
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

        if (entity.Status != "DRAFT" && entity.Status != "NEEDS_RECOUNT")
            throw new BusinessException($"Chỉ có thể gửi duyệt phiếu kiểm kê từ trạng thái DRAFT hoặc NEEDS_RECOUNT (hiện tại: {entity.Status}).");

        entity.Status = "PENDING_APPROVAL";
        entity.SubmittedAt = DateTime.UtcNow;

        await ApplyQuarantineOnSubmitAsync(tenantId, entity.Lines, cancellationToken);

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
            entity.Status = "APPROVED";

            foreach (var line in entity.Lines)
            {
                if (!line.BranchStockId.HasValue) continue;
                var stock = await _repository.GetBranchStockByIdAsync(tenantId, line.BranchStockId.Value, cancellationToken);
                if (stock == null) continue;

                // 1. Hàng hỏng tại cửa hàng (DAMAGED_IN_STORE) & Hàng hết hạn (EXPIRED) -> Xuất hủy nội bộ (Write-off), giải phóng khỏi cách ly và trừ tồn thực tế
                var writeOffQty = line.DamagedQty + line.ExpiredQty;
                if (writeOffQty > 0)
                {
                    stock.QuantityQuarantined = Math.Max(0, stock.QuantityQuarantined - writeOffQty);
                    stock.QuantityOnHand = Math.Max(0, stock.QuantityOnHand - writeOffQty);
                }

                // 2. Hàng thiếu (VarianceQty < 0: MISSING_LOST) -> Điều chỉnh giảm tồn kho (Stock Adjustment -)
                if (line.VarianceQty < 0)
                {
                    var missingQty = Math.Abs(line.VarianceQty);
                    stock.QuantityOnHand = Math.Max(0, stock.QuantityOnHand - missingQty);
                }

                // 3. Hàng lỗi NCC (DefectiveQty) & Hàng cận hạn đề nghị trả (NearExpiryAction == NEAR_EXPIRY_RETURN):
                // Giữ nguyên trong QuantityOnHand và tiếp tục giữ cách ly (QuantityQuarantined) chờ Store Owner tạo Phiếu trả hàng NCC.
            }
        }
        else if (decision == "REJECT" || decision == "REJECTED")
        {
            entity.Status = "REJECTED";
            await ReleaseQuarantineAsync(tenantId, entity.Lines, cancellationToken);
        }
        else if (decision == "NEEDS_RECOUNT" || decision == "RECOUNT")
        {
            entity.Status = "NEEDS_RECOUNT";
            await ReleaseQuarantineAsync(tenantId, entity.Lines, cancellationToken);
        }
        else
        {
            throw new BusinessException($"Quyết định duyệt '{command.Decision}' không hợp lệ. Chỉ chấp nhận APPROVE, REJECT hoặc NEEDS_RECOUNT.");
        }

        await _repository.UpdateAsync(entity, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(entity.Id, cancellationToken);
    }

    public async Task<SupplierReturnDetailDto> CreateReturnFromStocktakeAsync(
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

        var returnLinesToCreate = new List<CreateSupplierReturnLineDto>();
        Guid targetGrnId = command.GoodsReceiptId;
        var primaryReason = "Hàng hư hỏng, lỗi chất lượng";

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

            var resolvedGrnId = reqItem.GoodsReceiptId ?? stLine.OriginalGrnId ?? targetGrnId;
            if (resolvedGrnId == Guid.Empty)
                throw new BusinessException("Vui lòng chọn Phiếu nhập kho gốc (GRN) để xác định nguồn nhập và Nhà cung cấp.");

            targetGrnId = resolvedGrnId;

            if (stLine.DefectiveReasonCode == "RECALLED")
                primaryReason = "Thỏa thuận thu hồi từ hãng";
            else if (stLine.DefectiveReasonCode == "WRONG_SPEC_DISCOVERED")
                primaryReason = "Giao sai quy cách / mẫu mã";
            else if (stLine.NearExpiryAction == "NEAR_EXPIRY_RETURN" && stLine.DefectiveQty == 0)
                primaryReason = "Sắp hết hạn (Near-Expiry)";

            returnLinesToCreate.Add(new CreateSupplierReturnLineDto(
                ProductId: stLine.ProductId,
                GoodsReceiptLineId: reqItem.GoodsReceiptLineId ?? stLine.OriginalGrnLineId,
                BatchCode: stLine.BatchCode,
                ExpiryDate: stLine.ExpiryDate,
                RequestedQty: reqItem.RequestedQty,
                UnitCost: stLine.UnitCost,
                Notes: reqItem.Notes ?? stLine.Note ?? $"Từ phiếu kiểm kê {stocktake.Code}",
                StocktakeLineId: stLine.Id
            ));

            stLine.ReturnedQty += reqItem.RequestedQty;
        }

        await _repository.UpdateAsync(stocktake, cancellationToken);

        var createReturnCmd = new CreateSupplierReturnCommand(
            GoodsReceiptId: targetGrnId,
            Reason: primaryReason,
            ReasonNotes: string.IsNullOrWhiteSpace(command.ReasonNotes)
                ? $"Khởi tạo từ Phiếu kiểm kê {stocktake.Code}"
                : command.ReasonNotes,
            AttachmentUrl: command.AttachmentUrl ?? stocktake.Lines.Select(l => l.EvidenceUrl).FirstOrDefault(u => !string.IsNullOrWhiteSpace(u)),
            Lines: returnLinesToCreate,
            AutoSubmit: command.AutoSubmit,
            StocktakeId: stocktake.Id,
            ResolutionType: command.ResolutionType ?? "REDUCE_PAYABLE"
        );

        var createdReturn = await _supplierReturnService.CreateAsync(createReturnCmd, cancellationToken);

        // Cập nhật StocktakeCode trên SupplierReturn
        var returnEntity = await _supplierReturnRepository.GetByIdAsync(tenantId, createdReturn.Id, cancellationToken);
        if (returnEntity != null)
        {
            returnEntity.StocktakeId = stocktake.Id;
            returnEntity.StocktakeCode = stocktake.Code;
            await _supplierReturnRepository.UpdateAsync(returnEntity, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return await _supplierReturnService.GetByIdAsync(createdReturn.Id, cancellationToken);
    }

    public static decimal GetLineEligibleReturnQty(StocktakeLine line)
    {
        var nearExpiryReturnable = line.NearExpiryAction == "NEAR_EXPIRY_RETURN" ? line.NearExpiryQty : 0m;
        return line.DefectiveQty + nearExpiryReturnable;
    }

    private static decimal GetLineQuarantineQty(StocktakeLine line)
    {
        var nearExpiryHold = line.NearExpiryAction == "NEAR_EXPIRY_RETURN" ? line.NearExpiryQty : 0m;
        return line.DefectiveQty + line.DamagedQty + line.ExpiredQty + nearExpiryHold;
    }

    private async Task ApplyQuarantineOnSubmitAsync(
        string tenantId,
        IEnumerable<StocktakeLine> lines,
        CancellationToken cancellationToken)
    {
        foreach (var line in lines)
        {
            if (!line.BranchStockId.HasValue) continue;
            var holdQty = GetLineQuarantineQty(line);
            if (holdQty <= 0) continue;

            var stock = await _repository.GetBranchStockByIdAsync(tenantId, line.BranchStockId.Value, cancellationToken);
            if (stock != null)
            {
                var maxCanQuarantine = Math.Max(0, stock.QuantityOnHand - stock.QuantityReserved - stock.QuantityQuarantined);
                stock.QuantityQuarantined += Math.Min(holdQty, maxCanQuarantine);
            }
        }
    }

    private async Task ReleaseQuarantineAsync(
        string tenantId,
        IEnumerable<StocktakeLine> lines,
        CancellationToken cancellationToken)
    {
        foreach (var line in lines)
        {
            if (!line.BranchStockId.HasValue) continue;
            var holdQty = GetLineQuarantineQty(line);
            if (holdQty <= 0) continue;

            var stock = await _repository.GetBranchStockByIdAsync(tenantId, line.BranchStockId.Value, cancellationToken);
            if (stock != null)
            {
                stock.QuantityQuarantined = Math.Max(0, stock.QuantityQuarantined - holdQty);
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

    private StocktakeFilterQuery EnforceBranchScopeForEmployee(StocktakeFilterQuery query)
    {
        if (IsEmployeeRole() && _tenantContext.CurrentBranchId.HasValue && _tenantContext.CurrentBranchId.Value != Guid.Empty)
        {
            return query with { BranchId = _tenantContext.CurrentBranchId.Value };
        }
        return query;
    }
}

