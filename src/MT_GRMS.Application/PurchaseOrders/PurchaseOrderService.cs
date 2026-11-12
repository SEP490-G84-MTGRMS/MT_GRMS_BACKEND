using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.PurchaseOrders.Dispatch;
using MT_GRMS.Application.PurchaseOrders.Dtos;
using MT_GRMS.Domain.Entities.Supplier;
using MT_GRMS.Domain.Exceptions;

namespace MT_GRMS.Application.PurchaseOrders;

public class PurchaseOrderService : IPurchaseOrderService
{
    private readonly IPurchaseOrderRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITenantContext _tenantContext;
    private readonly ISupplierDispatchPackageService _dispatchPackageService;

    public PurchaseOrderService(
        IPurchaseOrderRepository repository,
        IUnitOfWork unitOfWork,
        ITenantContext tenantContext,
        ISupplierDispatchPackageService dispatchPackageService)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _tenantContext = tenantContext;
        _dispatchPackageService = dispatchPackageService;
    }

    private void EnsureBranchAccess(Guid branchId)
    {
        var role = (_tenantContext.CurrentRole ?? string.Empty).Trim().ToUpperInvariant();
        if (role is "EMPLOYEE" or "STAFF" or "CASHIER")
        {
            if (_tenantContext.CurrentBranchId.HasValue && _tenantContext.CurrentBranchId.Value != Guid.Empty)
            {
                if (branchId != _tenantContext.CurrentBranchId.Value)
                {
                    throw new UnauthorizedAccessException("Nhân viên chỉ có quyền truy cập và thao tác đặt hàng tại chi nhánh được phân công.");
                }
            }
        }
    }

    private async Task<string?> ResolveValidUserIdAsync(CancellationToken ct = default)
    {
        if (_tenantContext.CurrentUserId != Guid.Empty)
        {
            var strId = _tenantContext.CurrentUserId.ToString();
            if (await _repository.UserExistsAsync(strId, ct))
                return strId;
        }

        var role = (_tenantContext.CurrentRole ?? string.Empty).Trim().ToUpperInvariant();
        var fallbackId = (role is "EMPLOYEE" or "STAFF" or "CASHIER") ? "user-employee-001" : "user-owner-001";
        if (await _repository.UserExistsAsync(fallbackId, ct))
        {
            return fallbackId;
        }

        return null;
    }

    public async Task<OrderMatrixResponse> GetOrderMatrixAsync(
        OrderMatrixQuery query,
        CancellationToken cancellationToken = default)
    {
        EnsureBranchAccess(query.BranchId);

        var branch = await _repository.GetBranchByIdAsync(query.BranchId, cancellationToken);
        if (branch == null || branch.IsDeleted)
        {
            throw new NotFoundException("Chi nhánh", query.BranchId);
        }

        var products = await _repository.GetMatrixProductsAsync(query.BranchId, query.SupplierId, cancellationToken);
        var supplierConfigs = await _repository.GetMatrixSupplierConfigsAsync(query.BranchId, query.SupplierId, cancellationToken);

        var isAllSuppliers = !query.SupplierId.HasValue || query.SupplierId.Value == Guid.Empty;
        IReadOnlyList<PurchaseOrder> draftPos;
        PurchaseOrder? draftPo;
        if (isAllSuppliers)
        {
            draftPos = await _repository.GetDraftMatrixPosAsync(query.BranchId, cancellationToken);
            draftPo = draftPos.FirstOrDefault();
        }
        else
        {
            draftPo = await _repository.GetDraftMatrixPoAsync(
                query.BranchId, query.SupplierId, query.ViewFrom, cancellationToken);
            draftPos = draftPo != null
                ? new List<PurchaseOrder> { draftPo }
                : Array.Empty<PurchaseOrder>();
        }

        // Key ô = (SupplierId, ProductId, Date) — cùng SP nhiều NCC không đè nhau.
        var cellMap = new Dictionary<(Guid SupplierId, Guid ProductId, DateTime Date), OrderMatrixCellDto>();
        foreach (var draft in draftPos)
        {
            if (draft.Details == null) continue;
            foreach (var detail in draft.Details)
            {
                if (!detail.DeliveryDate.HasValue) continue;

                var finalQty = detail.FinalQty
                    ?? (detail.IsOwnerAdjusted ? (detail.StoreQty ?? 0m) : (detail.EmployeeQty ?? 0m));
                cellMap[(draft.SupplierId, detail.ProductId, detail.DeliveryDate.Value.Date)] = new OrderMatrixCellDto
                {
                    ProductId = detail.ProductId,
                    SupplierId = draft.SupplierId,
                    DeliveryDate = detail.DeliveryDate.Value.Date,
                    EmployeeQty = detail.EmployeeQty ?? 0m,
                    StoreQty = detail.StoreQty,
                    IsOwnerAdjusted = detail.IsOwnerAdjusted,
                    FinalQty = finalQty
                };
            }
        }

        var now = OrderMatrixRules.GetVietnamNow();
        var committedList = await _repository.GetCommittedPosInRangeAsync(
            query.BranchId, query.SupplierId, query.ViewFrom, query.DurationDays, cancellationToken);
        var committedDict = committedList
            .Where(p => p.DeliveryDate.HasValue)
            .GroupBy(p => (p.SupplierId, p.DeliveryDate!.Value.Date))
            .ToDictionary(g => g.Key, g => (g.First().Id, g.First().Code));

        // Overlay SL từ PO đã chốt:
        // - Ngày còn OPEN/CUTOFF_DUE + draft đã có ô → giữ draft (user vừa sửa để amend).
        // - Ngày FINALIZED / chưa có draft → hiện SL từ PO.
        var configBySupplierForOverlay = supplierConfigs.ToDictionary(c => c.SupplierId);
        foreach (var po in committedList)
        {
            if (!po.DeliveryDate.HasValue || po.Details == null || po.Details.Count == 0) continue;
            var date = po.DeliveryDate.Value.Date;
            configBySupplierForOverlay.TryGetValue(po.SupplierId, out var overlayCfg);
            var daySt = overlayCfg != null
                ? OrderMatrixRules.EvaluateDayStatus(po.SupplierId, date, overlayCfg, now, po.Code, po.Id)
                : null;
            var dayStillEditable = daySt != null
                && OrderMatrixRules.IsEditableStatus(daySt.DeliveryDateStatus);

            foreach (var detail in po.Details)
            {
                var qty = detail.OrderedQty ?? detail.FinalQty ?? 0m;
                if (qty <= 0) continue;

                var key = (po.SupplierId, detail.ProductId, date);
                if (dayStillEditable && cellMap.ContainsKey(key))
                {
                    continue; // Giữ số lượng draft (đã lưu) — không đè bằng PO cũ
                }

                cellMap[key] = new OrderMatrixCellDto
                {
                    ProductId = detail.ProductId,
                    SupplierId = po.SupplierId,
                    DeliveryDate = date,
                    EmployeeQty = detail.EmployeeQty ?? qty,
                    StoreQty = detail.StoreQty ?? qty,
                    IsOwnerAdjusted = detail.IsOwnerAdjusted || detail.StoreQty.HasValue,
                    FinalQty = qty
                };
            }
        }

        var dayStatuses = OrderMatrixRules.BuildDayStatuses(supplierConfigs, query.ViewFrom, query.DurationDays, now, committedDict);

        return new OrderMatrixResponse
        {
            BranchId = branch.Id,
            BranchName = branch.Name,
            BranchIsActive = branch.IsActive,
            SupplierId = query.SupplierId,
            ViewFrom = query.ViewFrom,
            DurationDays = query.DurationDays,
            Products = products,
            Cells = cellMap.Values.ToList(),
            SupplierConfigs = supplierConfigs,
            DayStatuses = dayStatuses,
            ServerNow = now,
            DraftPoId = draftPo?.Id,
            Status = draftPo?.Status ?? "DRAFT"
        };
    }

    public async Task<PurchaseOrderDto> SaveMatrixDraftAsync(
        SaveMatrixDraftCommand command,
        CancellationToken cancellationToken = default)
    {
        EnsureBranchAccess(command.BranchId);

        var branch = await _repository.GetBranchByIdAsync(command.BranchId, cancellationToken);
        if (branch == null || branch.IsDeleted)
        {
            throw new NotFoundException("Chi nhánh", command.BranchId);
        }

        var role = (_tenantContext.CurrentRole ?? string.Empty).Trim().ToUpperInvariant();
        bool isStoreOwner = string.IsNullOrEmpty(role) || role is "STORE_OWNER" or "ADMIN";

        var nowVn = OrderMatrixRules.GetVietnamNow();
        var matrixProducts = await _repository.GetMatrixProductsAsync(command.BranchId, null, cancellationToken);
        var productByKey = matrixProducts
            .GroupBy(p => (p.Id, p.SupplierId))
            .ToDictionary(g => g.Key, g => g.First());
        var fallbackSupplierByProduct = matrixProducts
            .GroupBy(p => p.Id)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Price <= 0 ? decimal.MaxValue : x.Price).First().SupplierId);

        var supplierConfigs = await _repository.GetMatrixSupplierConfigsAsync(command.BranchId, null, cancellationToken);
        var configBySupplier = supplierConfigs.ToDictionary(c => c.SupplierId);
        var dateFrom = command.Cells.Count > 0
            ? command.Cells.Min(c => c.DeliveryDate.Date)
            : nowVn.Date;
        var dateTo = command.Cells.Count > 0
            ? command.Cells.Max(c => c.DeliveryDate.Date)
            : nowVn.Date;
        var duration = Math.Max(1, (dateTo - dateFrom).Days + 1);
        var committedList = await _repository.GetCommittedPosInRangeAsync(
            command.BranchId, null, dateFrom, duration, cancellationToken);
        var committedDict = committedList
            .Where(p => p.DeliveryDate.HasValue)
            .GroupBy(p => (p.SupplierId, p.DeliveryDate!.Value.Date))
            .ToDictionary(g => g.Key, g => (g.First().Id, g.First().Code));

        // Resolve NCC cho từng ô: ưu tiên cell.SupplierId → command.SupplierId → giá rẻ nhất của SP.
        Guid ResolveCellSupplier(OrderMatrixCellDto cell)
        {
            if (cell.SupplierId != Guid.Empty) return cell.SupplierId;
            if (command.SupplierId.HasValue && command.SupplierId.Value != Guid.Empty) return command.SupplierId.Value;
            return fallbackSupplierByProduct.GetValueOrDefault(cell.ProductId);
        }

        var cellsBySupplier = command.Cells
            .Select(c => (Cell: c, SupplierId: ResolveCellSupplier(c)))
            .Where(x => x.SupplierId != Guid.Empty)
            .GroupBy(x => x.SupplierId)
            .ToList();

        PurchaseOrder? lastDraft = null;
        var submittedBy = await ResolveValidUserIdAsync(cancellationToken);

        foreach (var supplierGroup in cellsBySupplier)
        {
            var supplierId = supplierGroup.Key;
            var draft = await _repository.GetDraftMatrixPoAsync(
                command.BranchId, supplierId, command.ViewFrom, cancellationToken);

            if (draft == null)
            {
                draft = new PurchaseOrder
                {
                    TenantId = _tenantContext.CurrentTenantId,
                    Code = $"DRAFT-{DateTime.UtcNow:yyyyMMdd}-{branch.Code}-{Guid.NewGuid().ToString()[..4].ToUpper()}",
                    BranchId = command.BranchId,
                    SupplierId = supplierId,
                    ViewFromDate = command.ViewFrom,
                    ViewToDate = command.ViewTo,
                    Status = "DRAFT",
                    Note = command.Note,
                    CreatedByRole = _tenantContext.CurrentRole,
                    SubmittedByUserId = submittedBy,
                    SubmittedAtUtc = DateTime.UtcNow
                };
                await _repository.AddPurchaseOrderAsync(draft, cancellationToken);
            }
            else
            {
                draft.ViewFromDate = command.ViewFrom;
                draft.ViewToDate = command.ViewTo;
                draft.Note = command.Note;
            }

            var existingDetails = draft.Details
                .Where(d => d.DeliveryDate.HasValue)
                .ToDictionary(d => (d.ProductId, d.DeliveryDate!.Value.Date));

            foreach (var (cell, _) in supplierGroup)
            {
                var cellDate = cell.DeliveryDate.Date;

                if (configBySupplier.TryGetValue(supplierId, out var cellCfg))
                {
                    string? committedCode = null;
                    Guid? committedId = null;
                    if (committedDict.TryGetValue((supplierId, cellDate), out var committed))
                    {
                        committedId = committed.Id;
                        committedCode = committed.Code;
                    }

                    var daySt = OrderMatrixRules.EvaluateDayStatus(
                        supplierId, cellDate, cellCfg, nowVn, committedCode, committedId);
                    if (!OrderMatrixRules.IsEditableStatus(daySt.DeliveryDateStatus))
                    {
                        continue;
                    }
                }

                existingDetails.TryGetValue((cell.ProductId, cellDate), out var detail);

                decimal employeeQty;
                decimal? storeQty;
                bool isOwnerAdjusted;

                if (isStoreOwner)
                {
                    storeQty = cell.StoreQty;
                    isOwnerAdjusted = cell.IsOwnerAdjusted || cell.StoreQty.HasValue;
                    employeeQty = detail?.EmployeeQty ?? cell.EmployeeQty;
                }
                else
                {
                    var ownerLocked = detail != null
                        && (detail.IsOwnerAdjusted || detail.StoreQty.HasValue);
                    if (ownerLocked)
                    {
                        continue;
                    }

                    employeeQty = cell.EmployeeQty;
                    storeQty = null;
                    isOwnerAdjusted = false;
                }

                decimal finalQty = isOwnerAdjusted
                    ? (storeQty ?? 0m)
                    : employeeQty;

                if (detail == null)
                {
                    if (finalQty == 0 && employeeQty == 0 && !isOwnerAdjusted)
                    {
                        continue;
                    }

                    productByKey.TryGetValue((cell.ProductId, supplierId), out var matrixRow);
                    var product = await _repository.GetProductByIdAsync(cell.ProductId, cancellationToken);
                    var unitPrice = matrixRow?.Price > 0
                        ? matrixRow.Price
                        : (product?.CostPrice ?? product?.SellingPrice ?? 0m);
                    var lineAmt = finalQty * unitPrice;
                    var uom = !string.IsNullOrWhiteSpace(matrixRow?.Unit)
                        ? matrixRow!.Unit
                        : (product?.BaseUnit ?? "Gói");

                    detail = new PurchaseOrderDetail
                    {
                        TenantId = _tenantContext.CurrentTenantId,
                        PurchaseOrderId = draft.Id,
                        ProductId = cell.ProductId,
                        DeliveryDate = cellDate,
                        EmployeeQty = employeeQty,
                        StoreQty = storeQty,
                        IsOwnerAdjusted = isOwnerAdjusted,
                        FinalQty = finalQty,
                        OrderedQty = finalQty,
                        UnitPrice = unitPrice,
                        DeliveryUoM = uom,
                        LineAmount = lineAmt
                    };
                    draft.Details.Add(detail);
                    _repository.AddPurchaseOrderDetail(detail);
                }
                else
                {
                    detail.EmployeeQty = employeeQty;
                    detail.StoreQty = storeQty;
                    detail.IsOwnerAdjusted = isOwnerAdjusted;
                    detail.FinalQty = finalQty;
                    detail.OrderedQty = finalQty;
                    detail.LineAmount = finalQty * detail.UnitPrice;
                }
            }

            draft.TotalQty = draft.Details.Sum(d => d.FinalQty ?? 0m);
            draft.TotalAmount = draft.Details.Sum(d => d.LineAmount);
            lastDraft = draft;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (lastDraft == null)
        {
            // Không có ô hợp lệ — vẫn đảm bảo có draft rỗng theo filter NCC (nếu có).
            var fallbackSupplier = command.SupplierId is { } sid && sid != Guid.Empty
                ? sid
                : configBySupplier.Keys.FirstOrDefault();
            var empty = await _repository.GetDraftMatrixPoAsync(
                command.BranchId, fallbackSupplier == Guid.Empty ? null : fallbackSupplier, command.ViewFrom, cancellationToken);
            if (empty == null)
            {
                empty = new PurchaseOrder
                {
                    TenantId = _tenantContext.CurrentTenantId,
                    Code = $"DRAFT-{DateTime.UtcNow:yyyyMMdd}-{branch.Code}-{Guid.NewGuid().ToString()[..4].ToUpper()}",
                    BranchId = command.BranchId,
                    SupplierId = fallbackSupplier,
                    ViewFromDate = command.ViewFrom,
                    ViewToDate = command.ViewTo,
                    Status = "DRAFT",
                    Note = command.Note,
                    CreatedByRole = _tenantContext.CurrentRole,
                    SubmittedByUserId = submittedBy,
                    SubmittedAtUtc = DateTime.UtcNow
                };
                await _repository.AddPurchaseOrderAsync(empty, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return MapToDto(empty, branch.Name, "Nhiều nhà cung cấp");
        }

        return MapToDto(lastDraft, branch.Name, "Nhiều nhà cung cấp");
    }

    public async Task<DispatchHubResponse> GetDispatchHubAsync(
        DispatchHubQuery query,
        CancellationToken cancellationToken = default)
    {
        EnsureBranchAccess(query.BranchId);

        var now = OrderMatrixRules.GetVietnamNow();
        var scope = (query.Scope ?? string.Empty).Trim().ToUpperInvariant();

        DateTime viewFrom;
        int duration;
        DateTime? weekStartDate = null;
        DateTime? weekEndDate = null;

        if (scope == OrderMatrixRules.ScopeWeek)
        {
            DateTime monday;
            DateTime sunday;
            if (!string.IsNullOrWhiteSpace(query.WeekStartDate) && DateTime.TryParse(query.WeekStartDate, out var parsed))
            {
                (monday, sunday) = OrderMatrixRules.GetWeekRange(parsed);
            }
            else
            {
                (monday, sunday) = OrderMatrixRules.GetWeekRange(now);
            }
            viewFrom = monday;
            duration = 7;
            weekStartDate = monday;
            weekEndDate = sunday;
        }
        else if (scope == OrderMatrixRules.ScopeNearest)
        {
            viewFrom = now.Date;
            duration = 14;
        }
        else
        {
            viewFrom = (query.ViewFrom ?? DateTime.Today).Date;
            duration = Math.Clamp(query.DurationDays <= 0 ? 14 : query.DurationDays, 1, 31);
        }

        var matrix = await GetOrderMatrixAsync(new OrderMatrixQuery
        {
            BranchId = query.BranchId,
            ViewFrom = viewFrom,
            DurationDays = duration
        }, cancellationToken);

        var configsBySupplier = matrix.SupplierConfigs.ToDictionary(c => c.SupplierId, c => c);

        // Cell map: supplierId+productId+date -> qty (cùng SP nhiều NCC)
        var cells = matrix.Cells
            .GroupBy(c => (c.SupplierId, c.ProductId, c.DeliveryDate.Date))
            .ToDictionary(g => g.Key, g => g.First());

        var dayBySupplierDate = matrix.DayStatuses
            .ToDictionary(d => (d.SupplierId, d.DeliveryDate.Date), d => d);

        // SL đã chốt trên PO (để so với draft → HasPendingChanges)
        var committedPos = await _repository.GetCommittedPosInRangeAsync(
            query.BranchId, null, viewFrom, duration, cancellationToken);
        var committedQtyByKey = new Dictionary<(Guid SupplierId, Guid ProductId, DateTime Date), decimal>();
        foreach (var po in committedPos)
        {
            if (!po.DeliveryDate.HasValue || po.Details == null) continue;
            var dDate = po.DeliveryDate.Value.Date;
            foreach (var detail in po.Details.Where(d => !d.IsDeleted))
            {
                var q = detail.OrderedQty ?? detail.FinalQty ?? 0m;
                if (q <= 0) continue;
                var key = (po.SupplierId, detail.ProductId, dDate);
                committedQtyByKey[key] = committedQtyByKey.GetValueOrDefault(key) + q;
            }
        }

        var groups = new Dictionary<Guid, DispatchHubSupplierDto>();

        foreach (var product in matrix.Products)
        {
            if (product.IsRowDisabled) continue;
            if (!configsBySupplier.TryGetValue(product.SupplierId, out var cfg)) continue;

            for (var i = 0; i < duration; i++)
            {
                var date = viewFrom.AddDays(i);
                dayBySupplierDate.TryGetValue((product.SupplierId, date), out var dayStatus);
                var status = dayStatus?.DeliveryDateStatus ?? OrderMatrixRules.StatusOpen;

                cells.TryGetValue((product.SupplierId, product.Id, date), out var cell);
                var qty = cell?.FinalQty ?? 0m;
                if (qty <= 0) continue;

                // Chỉ FINALIZED (đã qua cutoff) mới coi là khóa cứng — PO tồn tại trước cutoff vẫn editable.
                var isFinalized = status == OrderMatrixRules.StatusFinalized;
                var isEditable = OrderMatrixRules.IsEditableStatus(status);

                if (!isFinalized && !isEditable) continue;

                if (!groups.TryGetValue(product.SupplierId, out var group))
                {
                    group = new DispatchHubSupplierDto
                    {
                        SupplierId = product.SupplierId,
                        Code = cfg.SupplierCode,
                        Name = cfg.SupplierName,
                        CutoffTimeStr = cfg.CutoffTime,
                        MinOrderValue = 0,
                        DraftPoId = matrix.DraftPoId,
                        Status = "draft",
                        PoCode = dayStatus?.PoCode,
                        Note = string.Empty
                    };
                    groups[product.SupplierId] = group;
                }

                if (isFinalized && !string.IsNullOrWhiteSpace(dayStatus?.PoCode))
                {
                    group.PoCode ??= dayStatus.PoCode;
                }

                if (!string.IsNullOrWhiteSpace(product.Category)
                    && !group.CategoryTags.Contains(product.Category, StringComparer.OrdinalIgnoreCase))
                {
                    group.CategoryTags.Add(product.Category);
                }

                var cutoff = dayStatus?.CutoffDateTime
                             ?? OrderMatrixRules.GetCutoffDateTime(date, cfg.CutoffTime, cfg.CutoffDayOffset);
                var remaining = (int)Math.Floor((cutoff - now).TotalMinutes);

                // Countdown / urgency chỉ theo ngày còn mở — không để ngày đã cutoff kéo cả NCC về "đã khóa"
                if (!isFinalized && (group.RemainingMinutes == null || remaining < group.RemainingMinutes))
                {
                    group.RemainingMinutes = remaining;
                    group.CutoffTimeStr = cutoff.ToString("HH:mm");
                    group.CountdownDisplay = FormatCountdown(remaining, finalized: false);
                    group.Urgency = ResolveUrgency(remaining, cutoff, now, finalized: false);
                    group.ExpectedDelivery = date.ToString("dd/MM/yyyy");
                }

                committedQtyByKey.TryGetValue((product.SupplierId, product.Id, date), out var poQty);

                group.Items.Add(new DispatchHubItemDto
                {
                    Id = $"{product.SupplierId:N}_{product.Id:N}_{date:yyyyMMdd}",
                    ProductId = product.Id,
                    Name = product.Name,
                    Sku = product.Sku,
                    Unit = product.Unit,
                    Price = product.Price,
                    Qty = qty,
                    CommittedQty = poQty,
                    DeliveryDate = date,
                    CutoffSchedule = $"{date:dd/MM} · cutoff {cutoff:HH:mm}"
                });

                // Đã có PO + draft ≠ PO → ghi nhận pending (EDITABLE hoặc LOCKED_CUTOFF)
                if (!string.IsNullOrWhiteSpace(dayStatus?.PoCode))
                {
                    if (qty != poQty)
                    {
                        var updateStatus = isEditable ? "EDITABLE" : "LOCKED_CUTOFF";
                        group.PendingChanges.Add(new DispatchPendingChangeLineDto
                        {
                            ProductId = product.Id,
                            ProductName = product.Name,
                            Sku = product.Sku,
                            Unit = product.Unit,
                            DeliveryDate = date,
                            DraftQty = qty,
                            CommittedQty = poQty,
                            PoCode = dayStatus?.PoCode,
                            UpdateStatus = updateStatus
                        });

                        if (isEditable)
                        {
                            group.PoCode = dayStatus.PoCode;
                            group.CanAmend = true;
                            group.HasPendingChanges = true;
                            group.PendingChangeLineCount++;
                        }
                    }
                    else if (isEditable)
                    {
                        group.PoCode = dayStatus.PoCode;
                        group.CanAmend = true;
                    }
                }
            }
        }

        var productsById = matrix.Products
            .GroupBy(p => p.Id)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Price <= 0 ? decimal.MaxValue : x.Price).First());

        // PO còn dòng mà draft đã về 0 (không vào Items) → cũng là thay đổi
        foreach (var kv in committedQtyByKey)
        {
            var (supplierId, productId, date) = kv.Key;
            dayBySupplierDate.TryGetValue((supplierId, date), out var ds);
            if (ds == null || string.IsNullOrWhiteSpace(ds.PoCode)) continue;

            var isEditableDay = OrderMatrixRules.IsEditableStatus(ds.DeliveryDateStatus);
            var isFinalizedDay = ds.DeliveryDateStatus == OrderMatrixRules.StatusFinalized;
            if (!isEditableDay && !isFinalizedDay) continue;

            cells.TryGetValue((supplierId, productId, date), out var cell);
            var draftQty = cell?.FinalQty ?? 0m;
            if (draftQty == kv.Value) continue;
            if (draftQty > 0) continue; // đã xử lý vòng trên

            if (!groups.TryGetValue(supplierId, out var group)) continue;
            productsById.TryGetValue(productId, out var pInfo);
            var updateStatus = isEditableDay ? "EDITABLE" : "LOCKED_CUTOFF";
            group.PendingChanges.Add(new DispatchPendingChangeLineDto
            {
                ProductId = productId,
                ProductName = pInfo?.Name ?? "(Sản phẩm)",
                Sku = pInfo?.Sku ?? string.Empty,
                Unit = pInfo?.Unit ?? string.Empty,
                DeliveryDate = date,
                DraftQty = 0m,
                CommittedQty = kv.Value,
                PoCode = ds.PoCode,
                UpdateStatus = updateStatus
            });

            if (isEditableDay)
            {
                group.CanAmend = true;
                group.PoCode ??= ds.PoCode;
                group.HasPendingChanges = true;
                group.PendingChangeLineCount++;
            }
        }

        // Nếu Scope == NEAREST: Chỉ giữ lại ngày giao gần nhất có FinalQty > 0 và còn hợp lệ (now < cutoff & editable)
        if (scope == OrderMatrixRules.ScopeNearest)
        {
            DateTime? nearestEligibleDate = null;
            var allItems = groups.Values.SelectMany(g => g.Items).ToList();
            var distinctDates = allItems
                .Select(it => it.DeliveryDate.Date)
                .Distinct()
                .OrderBy(d => d)
                .ToList();

            foreach (var date in distinctDates)
            {
                var hasEligibleSupplier = false;
                foreach (var g in groups.Values)
                {
                    if (!g.Items.Any(it => it.DeliveryDate.Date == date)) continue;
                    if (!configsBySupplier.TryGetValue(g.SupplierId, out var cfg)) continue;
                    dayBySupplierDate.TryGetValue((g.SupplierId, date), out var ds);
                    var cutoff = ds?.CutoffDateTime ?? OrderMatrixRules.GetCutoffDateTime(date, cfg.CutoffTime, cfg.CutoffDayOffset);
                    var st = ds?.DeliveryDateStatus ?? OrderMatrixRules.StatusOpen;

                    if (OrderMatrixRules.IsEditableStatus(st) && now < cutoff)
                    {
                        hasEligibleSupplier = true;
                        break;
                    }
                }

                if (hasEligibleSupplier)
                {
                    nearestEligibleDate = date;
                    break;
                }
            }

            if (nearestEligibleDate.HasValue)
            {
                var targetDate = nearestEligibleDate.Value;
                foreach (var g in groups.Values)
                {
                    g.Items = g.Items.Where(it => it.DeliveryDate.Date == targetDate).ToList();
                }
                var emptyKeys = groups.Where(kvp => kvp.Value.Items.Count == 0).Select(kvp => kvp.Key).ToList();
                foreach (var k in emptyKeys) groups.Remove(k);
            }
            else
            {
                groups.Clear();
            }
        }

        // Status NCC: chỉ "dispatched" khi mọi ngày còn qty đều đã FINALIZED
        foreach (var g in groups.Values)
        {
            var hasOpen = g.Items.Any(it =>
            {
                dayBySupplierDate.TryGetValue((g.SupplierId, it.DeliveryDate.Date), out var ds);
                return OrderMatrixRules.IsEditableStatus(ds?.DeliveryDateStatus);
            });
            var hasFinalized = g.Items.Any(it =>
            {
                dayBySupplierDate.TryGetValue((g.SupplierId, it.DeliveryDate.Date), out var ds);
                return ds?.DeliveryDateStatus == OrderMatrixRules.StatusFinalized;
            });

            g.Status = hasOpen ? "draft" : (hasFinalized ? "dispatched" : "draft");

            if (!hasOpen && hasFinalized)
            {
                g.CountdownDisplay = "Đã chốt PO";
                g.Urgency = "later";
            }
        }

        IEnumerable<DispatchHubSupplierDto> list = groups.Values;

        var kw = query.Keyword?.Trim();
        if (!string.IsNullOrWhiteSpace(kw))
        {
            list = list.Where(s =>
                s.Name.Contains(kw, StringComparison.OrdinalIgnoreCase)
                || s.Code.Contains(kw, StringComparison.OrdinalIgnoreCase)
                || s.CategoryTags.Any(t => t.Contains(kw, StringComparison.OrdinalIgnoreCase))
                || s.Items.Any(it =>
                    it.Name.Contains(kw, StringComparison.OrdinalIgnoreCase)
                    || it.Sku.Contains(kw, StringComparison.OrdinalIgnoreCase)));
        }

        var urgency = (query.Urgency ?? "all").Trim().ToLowerInvariant();
        list = urgency switch
        {
            "critical" => list.Where(s => s.Status != "dispatched" && s.Urgency == "critical"),
            "today" => list.Where(s => s.Status != "dispatched" && (s.Urgency is "today" or "critical")),
            "later" => list.Where(s => s.Status != "dispatched" && s.Urgency == "later"),
            "completed" => list.Where(s => s.Status == "dispatched"),
            _ => list
        };

        var allGroups = groups.Values.ToList();
        var resultList = list
            .OrderBy(s => s.Status == "dispatched" ? 1 : 0)
            .ThenBy(s => s.RemainingMinutes ?? int.MaxValue)
            .ThenBy(s => s.Name)
            .ToList();

        var draftItems = resultList.Where(s => s.Status != "dispatched").SelectMany(s => s.Items).ToList();
        var expectedPoCount = draftItems.Select(it => (it.ProductId, it.DeliveryDate.Date)).Distinct().Any()
            ? resultList.Where(s => s.Status != "dispatched")
                .SelectMany(s => s.Items.Select(it => (s.SupplierId, it.DeliveryDate.Date)))
                .Distinct()
                .Count()
            : 0;

        return new DispatchHubResponse
        {
            BranchId = matrix.BranchId,
            BranchName = matrix.BranchName,
            ServerNow = now,
            ViewFrom = viewFrom,
            DurationDays = duration,
            DraftPoId = matrix.DraftPoId,
            Scope = string.IsNullOrWhiteSpace(query.Scope) ? "ALL" : query.Scope.ToUpperInvariant(),
            WeekStartDate = weekStartDate,
            WeekEndDate = weekEndDate,
            ExpectedPoCount = expectedPoCount,
            TotalDraftAmount = draftItems.Sum(it => it.Qty * it.Price),
            TotalDraftLines = draftItems.Count,
            Suppliers = resultList,
            Counts = new DispatchHubCountsDto
            {
                All = allGroups.Count,
                Critical = allGroups.Count(s => s.Status != "dispatched" && s.Urgency == "critical"),
                Today = allGroups.Count(s => s.Status != "dispatched" && (s.Urgency is "today" or "critical")),
                Later = allGroups.Count(s => s.Status != "dispatched" && s.Urgency == "later"),
                Completed = allGroups.Count(s => s.Status == "dispatched")
            }
        };
    }

    private static string ResolveUrgency(int remainingMinutes, DateTime cutoff, DateTime now, bool finalized)
    {
        if (finalized) return "later";
        if (remainingMinutes <= 60) return "critical";
        if (cutoff.Date == now.Date) return "today";
        return "later";
    }

    private static string FormatCountdown(int remainingMinutes, bool finalized)
    {
        if (finalized) return "Đã chốt PO";
        if (remainingMinutes <= 0) return "Đã qua cutoff";
        if (remainingMinutes < 60) return $"Còn {remainingMinutes} phút";
        var hours = remainingMinutes / 60;
        var mins = remainingMinutes % 60;
        return mins > 0 ? $"Còn {hours}h {mins}p" : $"Còn {hours} giờ";
    }

    public async Task<DispatchSupplierHubResponse> GetDispatchSupplierHubAsync(
        DispatchSupplierHubQuery query,
        CancellationToken cancellationToken = default)
    {
        var now = OrderMatrixRules.GetVietnamNow();
        var duration = Math.Clamp(query.DurationDays <= 0 ? 14 : query.DurationDays, 1, 31);
        var viewFrom = now.Date;
        var branches = await _repository.GetActiveBranchesAsync(cancellationToken);

        var bySupplier = new Dictionary<Guid, DispatchSupplierHubItemDto>();

        foreach (var branch in branches)
        {
            var hub = await GetDispatchHubAsync(new DispatchHubQuery
            {
                BranchId = branch.Id,
                ViewFrom = viewFrom,
                DurationDays = duration,
                Scope = "ALL"
            }, cancellationToken);

            foreach (var s in hub.Suppliers)
            {
                if (!bySupplier.TryGetValue(s.SupplierId, out var row))
                {
                    row = new DispatchSupplierHubItemDto
                    {
                        SupplierId = s.SupplierId,
                        Code = s.Code,
                        Name = s.Name,
                        CutoffTimeStr = s.CutoffTimeStr,
                        CountdownDisplay = s.CountdownDisplay,
                        RemainingMinutes = s.RemainingMinutes,
                        Urgency = s.Urgency,
                        ExpectedDelivery = s.ExpectedDelivery,
                        Status = s.Status
                    };
                    bySupplier[s.SupplierId] = row;
                }

                var skuCount = s.Items.Count(it => it.Qty > 0);
                var totalQty = s.Items.Sum(it => it.Qty);
                var totalAmount = s.Items.Sum(it => it.Qty * it.Price);
                if (skuCount > 0 || s.Status == "dispatched" || s.HasPendingChanges)
                {
                    row.Branches.Add(new DispatchSupplierBranchSummaryDto
                    {
                        BranchId = branch.Id,
                        BranchCode = branch.Code,
                        BranchName = branch.Name,
                        SkuCount = skuCount,
                        TotalQty = totalQty,
                        TotalAmount = totalAmount,
                        Status = s.Status,
                        HasPendingChanges = s.HasPendingChanges
                    });
                }

                row.SkuCount += skuCount;
                row.TotalQty += totalQty;
                row.TotalAmount += totalAmount;
                row.LinkedBranchCount = Math.Max(row.LinkedBranchCount, row.Branches.Count);
                row.BranchesWithQty = row.Branches.Count(b => b.SkuCount > 0 || b.TotalQty > 0);

                if (row.RemainingMinutes == null
                    || (s.RemainingMinutes.HasValue && s.RemainingMinutes < row.RemainingMinutes))
                {
                    row.RemainingMinutes = s.RemainingMinutes;
                    row.CutoffTimeStr = s.CutoffTimeStr;
                    row.CountdownDisplay = s.CountdownDisplay;
                    row.Urgency = s.Urgency;
                    row.ExpectedDelivery = s.ExpectedDelivery;
                }

                if (s.CanAmend)
                {
                    row.CanAmend = true;
                    if (!string.IsNullOrWhiteSpace(s.PoCode))
                        row.PoCode = s.PoCode;
                }

                if (s.PendingChanges.Count > 0)
                {
                    foreach (var line in s.PendingChanges)
                    {
                        row.PendingChanges.Add(new DispatchPendingChangeLineDto
                        {
                            ProductId = line.ProductId,
                            ProductName = line.ProductName,
                            Sku = line.Sku,
                            Unit = line.Unit,
                            DeliveryDate = line.DeliveryDate,
                            DraftQty = line.DraftQty,
                            CommittedQty = line.CommittedQty,
                            PoCode = line.PoCode,
                            UpdateStatus = line.UpdateStatus,
                            BranchId = branch.Id,
                            BranchCode = branch.Code,
                            BranchName = branch.Name
                        });
                    }
                }

                if (s.HasPendingChanges)
                {
                    row.HasPendingChanges = true;
                    row.PendingChangeLineCount += s.PendingChangeLineCount;
                }

                if (s.Status == "draft")
                {
                    row.Status = "draft";
                }
                else if (row.Status != "draft" && s.Status == "dispatched")
                {
                    row.Status = "dispatched";
                }
            }
        }

        // Bổ sung NCC đã gán cửa hàng nhưng chưa có qty (vẫn hiện trong list)
        foreach (var branch in branches)
        {
            var configs = await _repository.GetMatrixSupplierConfigsAsync(branch.Id, null, cancellationToken);
            foreach (var cfg in configs.Where(c => c.IsAssignedToBranch && c.IsActive))
            {
                if (bySupplier.ContainsKey(cfg.SupplierId)) continue;
                bySupplier[cfg.SupplierId] = new DispatchSupplierHubItemDto
                {
                    SupplierId = cfg.SupplierId,
                    Code = cfg.SupplierCode,
                    Name = cfg.SupplierName,
                    CutoffTimeStr = cfg.CutoffTime,
                    CountdownDisplay = "Chưa có số lượng",
                    Urgency = "later",
                    Status = "draft",
                    LinkedBranchCount = 1,
                    BranchesWithQty = 0
                };
            }
        }

        // Đếm LinkedBranchCount chính xác hơn
        foreach (var row in bySupplier.Values)
        {
            var linked = await _repository.GetActiveBranchIdsForSupplierAsync(row.SupplierId, cancellationToken);
            row.LinkedBranchCount = linked.Count;
        }

        IEnumerable<DispatchSupplierHubItemDto> list = bySupplier.Values;

        var kw = query.Keyword?.Trim();
        if (!string.IsNullOrWhiteSpace(kw))
        {
            list = list.Where(s =>
                s.Name.Contains(kw, StringComparison.OrdinalIgnoreCase)
                || s.Code.Contains(kw, StringComparison.OrdinalIgnoreCase));
        }

        var urgency = (query.Urgency ?? "all").Trim().ToLowerInvariant();
        list = urgency switch
        {
            "critical" => list.Where(s => s.Status != "dispatched" && s.Urgency == "critical" && s.BranchesWithQty > 0),
            "today" => list.Where(s => s.Status != "dispatched" && (s.Urgency is "today" or "critical") && s.BranchesWithQty > 0),
            "later" => list.Where(s => s.Status != "dispatched" && s.Urgency == "later" && s.BranchesWithQty > 0),
            "completed" => list.Where(s => s.Status == "dispatched"),
            _ => list
        };

        var all = bySupplier.Values.ToList();
        var resultList = list
            .OrderByDescending(s => s.BranchesWithQty > 0)
            .ThenBy(s => s.Status == "dispatched" ? 1 : 0)
            .ThenBy(s => s.RemainingMinutes ?? int.MaxValue)
            .ThenBy(s => s.Name)
            .ToList();

        return new DispatchSupplierHubResponse
        {
            ServerNow = now,
            ViewFrom = viewFrom,
            DurationDays = duration,
            Suppliers = resultList,
            Counts = new DispatchHubCountsDto
            {
                All = all.Count,
                Critical = all.Count(s => s.Status != "dispatched" && s.Urgency == "critical" && s.BranchesWithQty > 0),
                Today = all.Count(s => s.Status != "dispatched" && (s.Urgency is "today" or "critical") && s.BranchesWithQty > 0),
                Later = all.Count(s => s.Status != "dispatched" && s.Urgency == "later" && s.BranchesWithQty > 0),
                Completed = all.Count(s => s.Status == "dispatched")
            }
        };
    }

    public async Task<DispatchSupplierDetailResponse> GetDispatchSupplierDetailAsync(
        DispatchSupplierDetailQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query.SupplierId == Guid.Empty)
        {
            throw new BusinessException("SUPPLIER_REQUIRED", "Thiếu mã nhà cung cấp.");
        }

        var supplier = await _repository.GetSupplierByIdAsync(query.SupplierId, cancellationToken);
        if (supplier == null || supplier.IsDeleted)
        {
            throw new NotFoundException("Nhà cung cấp", query.SupplierId);
        }

        var now = OrderMatrixRules.GetVietnamNow();
        var duration = Math.Clamp(query.DurationDays <= 0 ? 14 : query.DurationDays, 1, 31);
        var viewFrom = now.Date;
        var branchIds = await _repository.GetActiveBranchIdsForSupplierAsync(query.SupplierId, cancellationToken);
        var branches = new List<DispatchSupplierDetailBranchDto>();

        int? minRemaining = null;
        string cutoffStr = string.Empty;
        string countdown = "Chưa có số lượng";
        string urgency = "later";
        var hasDraft = false;
        var hasDispatched = false;

        foreach (var branchId in branchIds)
        {
            var hub = await GetDispatchHubAsync(new DispatchHubQuery
            {
                BranchId = branchId,
                ViewFrom = viewFrom,
                DurationDays = duration,
                Scope = "ALL"
            }, cancellationToken);

            var s = hub.Suppliers.FirstOrDefault(x => x.SupplierId == query.SupplierId);
            if (s == null || s.Items.Count == 0) continue;

            var branch = await _repository.GetBranchByIdAsync(branchId, cancellationToken);
            branches.Add(new DispatchSupplierDetailBranchDto
            {
                BranchId = branchId,
                BranchCode = branch?.Code ?? string.Empty,
                BranchName = branch?.Name ?? hub.BranchName,
                Status = s.Status,
                Items = s.Items.Select(it => new DispatchSupplierDetailLineDto
                {
                    Id = it.Id,
                    ProductId = it.ProductId,
                    Name = it.Name,
                    Sku = it.Sku,
                    Unit = it.Unit,
                    Price = it.Price,
                    Qty = it.Qty,
                    CommittedQty = it.CommittedQty,
                    DeliveryDate = it.DeliveryDate,
                    CutoffSchedule = it.CutoffSchedule,
                    DayStatus = s.Status == "dispatched" ? "FINALIZED" : "OPEN"
                }).OrderBy(it => it.DeliveryDate).ThenBy(it => it.Name).ToList()
            });

            if (s.Status == "draft") hasDraft = true;
            if (s.Status == "dispatched") hasDispatched = true;

            if (s.RemainingMinutes.HasValue
                && (minRemaining == null || s.RemainingMinutes < minRemaining))
            {
                minRemaining = s.RemainingMinutes;
                cutoffStr = s.CutoffTimeStr;
                countdown = s.CountdownDisplay;
                urgency = s.Urgency;
            }
        }

        return new DispatchSupplierDetailResponse
        {
            SupplierId = query.SupplierId,
            Code = supplier.Code,
            Name = supplier.Name,
            Email = supplier.Email,
            ServerNow = now,
            ViewFrom = viewFrom,
            DurationDays = duration,
            CutoffTimeStr = cutoffStr,
            CountdownDisplay = countdown,
            RemainingMinutes = minRemaining,
            Urgency = urgency,
            Status = hasDraft ? "draft" : (hasDispatched ? "dispatched" : "draft"),
            Branches = branches.OrderBy(b => b.BranchCode).ToList()
        };
    }

    public async Task<IReadOnlyList<PurchaseOrderDto>> SplitMatrixPoAsync(
        SplitMatrixPoCommand command,
        CancellationToken cancellationToken = default)
    {
        var batchResult = await SplitMatrixPoBatchAsync(command, cancellationToken);
        if (batchResult.SuccessCount == 0 && batchResult.FailedCount > 0)
        {
            var firstErr = batchResult.Results.FirstOrDefault(r => r.Status == OrderMatrixRules.ResultFailed);
            throw new BusinessException(firstErr?.ErrorCode ?? "SPLIT_FAILED", firstErr?.ErrorMessage ?? "Chốt PO thất bại.");
        }
        return batchResult.CreatedPurchaseOrders;
    }

    public async Task<SplitMatrixBatchResultDto> SplitMatrixPoBatchAsync(
        SplitMatrixPoCommand command,
        CancellationToken cancellationToken = default)
    {
        SplitMatrixBatchResultDto result;
        if (command.SupplierId.HasValue
            && command.SupplierId.Value != Guid.Empty
            && command.BranchId == Guid.Empty)
        {
            result = await SplitSupplierAcrossBranchesAsync(command, isDryRun: false, cancellationToken);
        }
        else
        {
            result = await ProcessMatrixSplitEngineAsync(command, isDryRun: false, cancellationToken);
            if (!string.IsNullOrWhiteSpace(command.EmailSendMode)
                && (result.SuccessCount > 0 || result.AmendedCount > 0 || result.NoChangesCount > 0))
            {
                result.DispatchPackage = await _dispatchPackageService.CreatePackageForDispatchAsync(
                    command,
                    result,
                    cancellationToken);
            }
        }

        return result;
    }

    public async Task<SplitMatrixBatchResultDto> PreviewSplitMatrixPoAsync(
        SplitMatrixPoCommand command,
        CancellationToken cancellationToken = default)
    {
        // Preview: không tạo package/email
        var previewCommand = new SplitMatrixPoCommand
        {
            BranchId = command.BranchId,
            SupplierId = command.SupplierId,
            ParentDraftId = command.ParentDraftId,
            Note = command.Note,
            Scope = command.Scope,
            WeekStartDate = command.WeekStartDate,
            Groups = command.Groups
        };

        if (previewCommand.SupplierId.HasValue
            && previewCommand.SupplierId.Value != Guid.Empty
            && previewCommand.BranchId == Guid.Empty)
        {
            return await SplitSupplierAcrossBranchesAsync(previewCommand, isDryRun: true, cancellationToken);
        }

        return await ProcessMatrixSplitEngineAsync(previewCommand, isDryRun: true, cancellationToken);
    }

    /// <summary>
    /// Chốt 1 NCC trên mọi cửa hàng đang liên kết và có draft qty trong phạm vi NEAREST/WEEK.
    /// </summary>
    private async Task<SplitMatrixBatchResultDto> SplitSupplierAcrossBranchesAsync(
        SplitMatrixPoCommand command,
        bool isDryRun,
        CancellationToken cancellationToken)
    {
        var supplierId = command.SupplierId!.Value;
        var branchIds = await _repository.GetActiveBranchIdsForSupplierAsync(supplierId, cancellationToken);
        var merged = new List<SplitGroupResultDto>();
        var created = new List<PurchaseOrderDto>();
        var now = OrderMatrixRules.GetVietnamNow();

        foreach (var branchId in branchIds)
        {
            var perBranch = await ProcessMatrixSplitEngineAsync(
                new SplitMatrixPoCommand
                {
                    BranchId = branchId,
                    SupplierId = supplierId,
                    Scope = command.Scope,
                    WeekStartDate = command.WeekStartDate,
                    Note = command.Note,
                    ParentDraftId = command.ParentDraftId
                },
                isDryRun,
                cancellationToken);

            merged.AddRange(perBranch.Results);
            created.AddRange(perBranch.CreatedPurchaseOrders);
        }

        var result = new SplitMatrixBatchResultDto
        {
            BranchId = Guid.Empty,
            Scope = command.Scope,
            ServerNow = now,
            TotalGroups = merged.Count,
            SuccessCount = merged.Count(r => r.Status == OrderMatrixRules.ResultSuccess),
            AmendedCount = merged.Count(r => r.Status == OrderMatrixRules.ResultAmended),
            NoChangesCount = merged.Count(r => r.Status == OrderMatrixRules.ResultNoChanges),
            FailedCount = merged.Count(r => r.Status == OrderMatrixRules.ResultFailed),
            SkippedCount = merged.Count(r => r.Status == OrderMatrixRules.ResultSkipped),
            AlreadyFinalizedCount = merged.Count(r => r.Status == OrderMatrixRules.ResultAlreadyFinalized),
            Results = merged,
            CreatedPurchaseOrders = created
        };

        if (!isDryRun
            && !string.IsNullOrWhiteSpace(command.EmailSendMode)
            && (result.SuccessCount > 0 || result.AmendedCount > 0 || result.NoChangesCount > 0))
        {
            result.DispatchPackage = await _dispatchPackageService.CreatePackageForDispatchAsync(
                command,
                result,
                cancellationToken);
        }

        return result;
    }

    private async Task<SplitMatrixBatchResultDto> ProcessMatrixSplitEngineAsync(
        SplitMatrixPoCommand command,
        bool isDryRun,
        CancellationToken cancellationToken)
    {
        EnsureBranchAccess(command.BranchId);

        var branch = await _repository.GetBranchByIdAsync(command.BranchId, cancellationToken);
        if (branch == null || branch.IsDeleted)
        {
            throw new NotFoundException("Chi nhánh", command.BranchId);
        }

        if (!branch.IsActive)
        {
            throw new BusinessException("BRANCH_INACTIVE", "Chi nhánh đang ngừng hoạt động. Không thể chốt PO.");
        }

        var products = await _repository.GetMatrixProductsAsync(command.BranchId, null, cancellationToken);
        var productLookup = products
            .GroupBy(p => (p.Id, p.SupplierId))
            .ToDictionary(g => g.Key, g => g.First());
        var productById = products.GroupBy(p => p.Id).ToDictionary(g => g.Key, g => g.First());

        var supplierConfigs = await _repository.GetMatrixSupplierConfigsAsync(command.BranchId, null, cancellationToken);
        var configBySupplier = supplierConfigs.ToDictionary(c => c.SupplierId);

        var draft = command.ParentDraftId.HasValue
            ? await _repository.GetPurchaseOrderByIdAsync(command.ParentDraftId.Value, cancellationToken)
            : await _repository.GetDraftMatrixPoAsync(command.BranchId, null, DateTime.Today, cancellationToken);

        var draftDetails = (draft?.Status == "DRAFT" ? draft.Details : null)?
            .Where(d => d.DeliveryDate.HasValue)
            .ToList()
            ?? new List<PurchaseOrderDetail>();

        var now = OrderMatrixRules.GetVietnamNow();
        var scope = (command.Scope ?? string.Empty).Trim().ToUpperInvariant();

        // 1. Resolve Target Groups (SupplierId, DeliveryDate)
        var targetGroups = new List<(Guid SupplierId, DateTime DeliveryDate)>();

        if (scope == OrderMatrixRules.ScopeNearest)
        {
            var detailsWithQty = draftDetails
                .Where(d =>
                {
                    var fq = OrderMatrixRules.ComputeFinalQty(d.EmployeeQty ?? 0m, d.StoreQty, d.IsOwnerAdjusted);
                    return fq > 0 && productById.TryGetValue(d.ProductId, out _);
                })
                .ToList();

            DateTime? nearestEligibleDate = null;
            var distinctDates = detailsWithQty
                .Select(d => d.DeliveryDate!.Value.Date)
                .Distinct()
                .OrderBy(d => d)
                .ToList();

            foreach (var date in distinctDates)
            {
                var datesSuppliers = detailsWithQty
                    .Where(d => d.DeliveryDate!.Value.Date == date && productById.TryGetValue(d.ProductId, out _))
                    .Select(d => productById[d.ProductId].SupplierId)
                    .Distinct();

                var hasValidSupplierForDate = false;
                foreach (var supId in datesSuppliers)
                {
                    if (!configBySupplier.TryGetValue(supId, out var cfg)) continue;
                    var ds = OrderMatrixRules.EvaluateDayStatus(supId, date, cfg, now);
                    if (OrderMatrixRules.IsEditableStatus(ds.DeliveryDateStatus) && now < ds.CutoffDateTime)
                    {
                        hasValidSupplierForDate = true;
                        break;
                    }
                }

                if (hasValidSupplierForDate)
                {
                    nearestEligibleDate = date;
                    break;
                }
            }

            if (nearestEligibleDate.HasValue)
            {
                var targetDate = nearestEligibleDate.Value;
                targetGroups = detailsWithQty
                    .Where(d => d.DeliveryDate!.Value.Date == targetDate && productById.TryGetValue(d.ProductId, out _))
                    .Select(d => (productById[d.ProductId].SupplierId, targetDate))
                    .Distinct()
                    .ToList();
            }
        }
        else if (scope == OrderMatrixRules.ScopeWeek)
        {
            DateTime monday;
            DateTime sunday;
            if (!string.IsNullOrWhiteSpace(command.WeekStartDate) && DateTime.TryParse(command.WeekStartDate, out var parsed))
            {
                (monday, sunday) = OrderMatrixRules.GetWeekRange(parsed);
            }
            else
            {
                (monday, sunday) = OrderMatrixRules.GetWeekRange(now);
            }

            var detailsInWeek = draftDetails
                .Where(d =>
                {
                    var date = d.DeliveryDate!.Value.Date;
                    if (date < monday || date > sunday) return false;
                    var fq = OrderMatrixRules.ComputeFinalQty(d.EmployeeQty ?? 0m, d.StoreQty, d.IsOwnerAdjusted);
                    return fq > 0 && productById.TryGetValue(d.ProductId, out _);
                })
                .ToList();

            targetGroups = detailsInWeek
                .Select(d => (productById[d.ProductId].SupplierId, d.DeliveryDate!.Value.Date))
                .Distinct()
                .OrderBy(g => g.Item2)
                .ThenBy(g => g.Item1)
                .ToList();
        }
        else if (OrderMatrixRules.TryResolveRollingDurationDays(scope).HasValue
                 || string.Equals(scope, OrderMatrixRules.ScopeAll, StringComparison.OrdinalIgnoreCase))
        {
            var days = OrderMatrixRules.TryResolveRollingDurationDays(scope)
                ?? Math.Clamp(command.DurationDays <= 0 ? 31 : command.DurationDays, 1, 31);
            var rangeFrom = now.Date;
            var rangeTo = rangeFrom.AddDays(days - 1);

            targetGroups = draftDetails
                .Where(d =>
                {
                    var date = d.DeliveryDate!.Value.Date;
                    if (date < rangeFrom || date > rangeTo) return false;
                    var fq = OrderMatrixRules.ComputeFinalQty(d.EmployeeQty ?? 0m, d.StoreQty, d.IsOwnerAdjusted);
                    return fq > 0 && productById.TryGetValue(d.ProductId, out _);
                })
                .Select(d => (productById[d.ProductId].SupplierId, d.DeliveryDate!.Value.Date))
                .Distinct()
                .OrderBy(g => g.Item2)
                .ThenBy(g => g.Item1)
                .ToList();
        }
        else if (command.Groups != null && command.Groups.Any())
        {
            targetGroups = command.Groups
                .Select(g => (g.SupplierId, g.DeliveryDate.Date))
                .Distinct()
                .ToList();
        }
        else
        {
            // Không có Scope/Groups: lấy toàn bộ draft còn qty (legacy).
            targetGroups = draftDetails
                .Where(d =>
                {
                    var fq = OrderMatrixRules.ComputeFinalQty(d.EmployeeQty ?? 0m, d.StoreQty, d.IsOwnerAdjusted);
                    return fq > 0 && productById.TryGetValue(d.ProductId, out _);
                })
                .Select(d => (productById[d.ProductId].SupplierId, d.DeliveryDate!.Value.Date))
                .Distinct()
                .OrderBy(g => g.Item2)
                .ToList();
        }

        if (command.SupplierId.HasValue && command.SupplierId.Value != Guid.Empty)
        {
            targetGroups = targetGroups
                .Where(g => g.SupplierId == command.SupplierId.Value)
                .ToList();
        }

        // 2. Chạy business engine xử lý từng group độc lập (Partial Success)
        var groupResults = new List<SplitGroupResultDto>();
        var createdPos = new List<PurchaseOrder>();

        foreach (var (supplierId, deliveryDate) in targetGroups)
        {
            configBySupplier.TryGetValue(supplierId, out var cfg);
            var supplier = await _repository.GetSupplierByIdAsync(supplierId, cancellationToken);
            var supName = supplier?.Name ?? cfg?.SupplierName ?? "NCC";
            var supCode = supplier?.Code ?? cfg?.SupplierCode ?? "NCC";

            var result = new SplitGroupResultDto
            {
                BranchId = command.BranchId,
                BranchName = branch.Name,
                SupplierId = supplierId,
                SupplierName = supName,
                SupplierCode = supCode,
                DeliveryDate = deliveryDate
            };

            if (cfg == null || !cfg.IsAssignedToBranch || !cfg.IsActive)
            {
                result.Status = OrderMatrixRules.ResultFailed;
                result.ErrorCode = "SUPPLIER_BRANCH_INACTIVE";
                result.ErrorMessage = $"Nhà cung cấp {supName} chưa được gán hoặc đang ngừng hoạt động tại chi nhánh.";
                groupResults.Add(result);
                continue;
            }

            // PO đã tồn tại: trước cutoff → AMEND; sau cutoff → ALREADY_FINALIZED (khóa).
            var existingPo = await _repository.FindCommittedPoAsync(command.BranchId, supplierId, deliveryDate, cancellationToken);

            var dayStatus = OrderMatrixRules.EvaluateDayStatus(
                supplierId, deliveryDate, cfg, now, existingPo?.Code, existingPo?.Id);

            if (dayStatus.DeliveryDateStatus == OrderMatrixRules.StatusFinalized)
            {
                result.Status = OrderMatrixRules.ResultAlreadyFinalized;
                result.PurchaseOrderId = existingPo?.Id;
                result.PurchaseOrderCode = existingPo?.Code;
                result.RevisionNumber = existingPo?.RevisionNumber;
                result.ErrorCode = "CUTOFF_PASSED";
                result.ErrorMessage =
                    $"Đơn {existingPo?.Code} đã khóa vì quá giờ cutoff ({dayStatus.CutoffDateTime:HH:mm dd/MM/yyyy}). Không thể amend.";
                groupResults.Add(result);
                continue;
            }

            if (now >= dayStatus.CutoffDateTime || dayStatus.DeliveryDateStatus == OrderMatrixRules.StatusLocked)
            {
                result.Status = OrderMatrixRules.ResultSkipped;
                result.ErrorCode = "CUTOFF_EXPIRED";
                result.ErrorMessage = $"Đã quá giờ chốt đơn (Cutoff Time: {dayStatus.CutoffDateTime:HH:mm dd/MM/yyyy}) của NCC cho ngày giao này.";
                groupResults.Add(result);
                continue;
            }

            if (dayStatus.DeliveryDateStatus == OrderMatrixRules.StatusSupplierOff)
            {
                result.Status = OrderMatrixRules.ResultSkipped;
                result.ErrorCode = dayStatus.PrimaryReasonCode ?? "SUPPLIER_OFF";
                result.ErrorMessage = $"Nhà cung cấp không giao hàng vào ngày {deliveryDate:dd/MM/yyyy} ({dayStatus.PrimaryReasonCode}).";
                groupResults.Add(result);
                continue;
            }

            if (!OrderMatrixRules.IsEditableStatus(dayStatus.DeliveryDateStatus))
            {
                result.Status = OrderMatrixRules.ResultSkipped;
                result.ErrorCode = dayStatus.PrimaryReasonCode ?? "NOT_EDITABLE";
                result.ErrorMessage = $"Ngày giao hàng không ở trạng thái có thể chốt đơn ({dayStatus.DeliveryDateStatus}).";
                groupResults.Add(result);
                continue;
            }

            // Lấy các mặt hàng: Ưu tiên Draft DB (Single source of truth).
            // Nếu không có trong Draft (chỉ xảy ra ở caller/test legacy gửi explicit Items), fallback items gửi lên.
            var detailsForGroup = draftDetails
                .Where(d => d.DeliveryDate!.Value.Date == deliveryDate
                            && productById.TryGetValue(d.ProductId, out var mp)
                            && mp.SupplierId == supplierId)
                .ToList();

            var legacyGroup = command.Groups?.FirstOrDefault(g => g.SupplierId == supplierId && g.DeliveryDate.Date == deliveryDate);
            var useLegacyItems = detailsForGroup.Count == 0 && legacyGroup?.Items != null && legacyGroup.Items.Any();

            var lineErrors = new List<string>();
            var validLines = new List<(OrderMatrixProductDto Product, decimal Qty, string Unit, decimal UnitPrice, PurchaseOrderDetail? DraftDetail)>();

            var candidateLines = new List<(Guid ProductId, decimal Qty, decimal? UnitPrice, string? Unit, PurchaseOrderDetail? DraftDetail)>();

            if (detailsForGroup.Count > 0)
            {
                foreach (var detail in detailsForGroup)
                {
                    var qty = OrderMatrixRules.ComputeFinalQty(detail.EmployeeQty ?? 0m, detail.StoreQty, detail.IsOwnerAdjusted);
                    if (qty > 0)
                    {
                        candidateLines.Add((detail.ProductId, qty, null, null, detail));
                    }
                }
            }
            else if (useLegacyItems)
            {
                foreach (var item in legacyGroup!.Items.Where(i => i.Quantity > 0))
                {
                    candidateLines.Add((item.ProductId, item.Quantity, item.UnitPrice > 0 ? item.UnitPrice : null, item.Unit, null));
                }
            }

            foreach (var lineCandidate in candidateLines)
            {
                var productId = lineCandidate.ProductId;
                var qty = lineCandidate.Qty;
                var detail = lineCandidate.DraftDetail;

                if (!productLookup.TryGetValue((productId, supplierId), out var mp)
                    && !productById.TryGetValue(productId, out mp))
                {
                    lineErrors.Add($"Sản phẩm {productId} không thuộc danh mục ma trận.");
                    continue;
                }

                if (mp.SupplierId != supplierId)
                {
                    lineErrors.Add($"Sản phẩm {mp.Sku} không thuộc NCC {supName}.");
                    continue;
                }

                if (mp.IsRowDisabled)
                {
                    lineErrors.Add(mp.RowDisabledReason ?? $"Sản phẩm {mp.Sku} đang ngừng kinh doanh.");
                    continue;
                }

                if (OrderMatrixRules.ViolatesMoq(qty, mp.Moq))
                {
                    lineErrors.Add($"Sản phẩm {mp.Sku}: SL đặt {qty} < MOQ ({mp.Moq}).");
                    continue;
                }

                if (OrderMatrixRules.ViolatesOrderMultiple(qty, mp.OrderMultiple))
                {
                    lineErrors.Add($"Sản phẩm {mp.Sku}: SL đặt {qty} không chia hết cho bội số đặt ({mp.OrderMultiple}).");
                    continue;
                }

                var sp = await _repository.GetSupplierProductAsync(supplierId, productId, cancellationToken);
                var unitPrice = lineCandidate.UnitPrice.HasValue && lineCandidate.UnitPrice.Value > 0
                    ? lineCandidate.UnitPrice.Value
                    : (sp != null && sp.AgreedWholesalePrice > 0 ? sp.AgreedWholesalePrice : mp.Price);
                var unit = !string.IsNullOrWhiteSpace(lineCandidate.Unit)
                    ? lineCandidate.Unit
                    : (!string.IsNullOrWhiteSpace(sp?.DeliveryUoM) ? sp!.DeliveryUoM : mp.Unit);

                validLines.Add((mp, qty, unit, unitPrice, detail));
            }

            if (lineErrors.Count > 0)
            {
                result.Status = OrderMatrixRules.ResultFailed;
                result.ErrorCode = "VALIDATION_FAILED";
                result.ErrorMessage = string.Join(" | ", lineErrors.Take(5));
                result.InvalidLineCount = lineErrors.Count;
                result.ValidLineCount = validLines.Count;
                groupResults.Add(result);
                continue;
            }

            if (validLines.Count == 0)
            {
                result.Status = OrderMatrixRules.ResultSkipped;
                result.ErrorCode = "EMPTY_LINES";
                result.ErrorMessage = "Không có sản phẩm nào có số lượng đặt lớn hơn 0.";
                groupResults.Add(result);
                continue;
            }

            var totalQty = validLines.Sum(l => l.Qty);
            var totalAmount = validLines.Sum(l => l.Qty * l.UnitPrice);
            var hasMissingPrice = validLines.Any(l => l.UnitPrice <= 0);

            result.ValidLineCount = validLines.Count;
            result.TotalQuantity = totalQty;
            result.TotalAmount = totalAmount;
            result.HasMissingPrice = hasMissingPrice;
            result.Lines = validLines.Select(l => new SplitGroupLineDto
            {
                ProductId = l.Product.Id,
                ProductName = l.Product.Name,
                Sku = l.Product.Sku,
                Unit = l.Unit,
                Quantity = l.Qty,
                UnitPrice = l.UnitPrice,
                LineAmount = l.Qty * l.UnitPrice,
                HasMissingPrice = l.UnitPrice <= 0
            }).ToList();

            if (isDryRun)
            {
                result.Status = existingPo != null ? OrderMatrixRules.ResultAmended : OrderMatrixRules.ResultSuccess;
                result.PurchaseOrderId = existingPo?.Id;
                result.PurchaseOrderCode = existingPo?.Code ?? $"(Dự kiến PO {deliveryDate:yyyyMMdd})";
                result.RevisionNumber = existingPo != null ? existingPo.RevisionNumber + 1 : 1;
                groupResults.Add(result);
            }
            else if (existingPo != null)
            {
                var previousSnapshot = BuildPoLinesSnapshot(existingPo);
                var newQtyByProduct = validLines.ToDictionary(l => l.Product.Id, l => l.Qty);
                var oldQtyByProduct = existingPo.Details
                    .Where(d => !d.IsDeleted)
                    .GroupBy(d => d.ProductId)
                    .ToDictionary(
                        g => g.Key,
                        g => g.Sum(d => d.OrderedQty ?? d.FinalQty ?? 0m));

                var changed = newQtyByProduct.Count != oldQtyByProduct.Count
                    || newQtyByProduct.Any(kv =>
                        !oldQtyByProduct.TryGetValue(kv.Key, out var oldQty) || oldQty != kv.Value)
                    || oldQtyByProduct.Keys.Any(id => !newQtyByProduct.ContainsKey(id));

                if (!changed)
                {
                    result.Status = OrderMatrixRules.ResultNoChanges;
                    result.PurchaseOrderId = existingPo.Id;
                    result.PurchaseOrderCode = existingPo.Code;
                    result.RevisionNumber = existingPo.RevisionNumber;
                    result.ErrorMessage = "Không có thay đổi số lượng so với PO hiện tại.";
                    // Vẫn đưa vào danh sách để có thể gửi lại email / package.
                    createdPos.Add(existingPo);
                    groupResults.Add(result);
                    continue;
                }

                var expectedRowVersion = existingPo.RowVersion;
                var actor = await ResolveValidUserIdAsync(cancellationToken);

                // Soft-delete dòng không còn trong validLines
                foreach (var detail in existingPo.Details.Where(d => !d.IsDeleted).ToList())
                {
                    if (!newQtyByProduct.ContainsKey(detail.ProductId))
                    {
                        detail.IsDeleted = true;
                        detail.OrderedQty = 0m;
                        detail.FinalQty = 0m;
                        detail.LineAmount = 0m;
                    }
                }

                foreach (var line in validLines)
                {
                    var unit = string.IsNullOrWhiteSpace(line.Unit) ? "Gói" : line.Unit;
                    var detail = existingPo.Details.FirstOrDefault(d => !d.IsDeleted && d.ProductId == line.Product.Id);
                    if (detail == null)
                    {
                        detail = new PurchaseOrderDetail
                        {
                            TenantId = _tenantContext.CurrentTenantId,
                            PurchaseOrderId = existingPo.Id,
                            ProductId = line.Product.Id,
                            DeliveryDate = deliveryDate,
                            UnitPrice = line.UnitPrice,
                            DeliveryUoM = unit,
                            ProductCodeSnapshot = line.Product.Sku,
                            ProductNameSnapshot = line.Product.Name,
                            PurchaseUnitNameSnapshot = unit
                        };
                        existingPo.Details.Add(detail);
                        _repository.AddPurchaseOrderDetail(detail);
                    }

                    detail.EmployeeQty = line.DraftDetail?.EmployeeQty ?? line.Qty;
                    detail.StoreQty = line.DraftDetail?.StoreQty;
                    detail.IsOwnerAdjusted = line.DraftDetail?.IsOwnerAdjusted ?? false;
                    detail.OrderedQty = line.Qty;
                    detail.FinalQty = line.Qty;
                    detail.LineAmount = line.Qty * detail.UnitPrice;
                    detail.DeliveryDate = deliveryDate;
                    detail.IsDeleted = false;
                    // Giữ snapshot giá/đơn vị đã chốt; chỉ set nếu thiếu
                    if (string.IsNullOrWhiteSpace(detail.ProductCodeSnapshot))
                        detail.ProductCodeSnapshot = line.Product.Sku;
                    if (string.IsNullOrWhiteSpace(detail.ProductNameSnapshot))
                        detail.ProductNameSnapshot = line.Product.Name;
                    if (string.IsNullOrWhiteSpace(detail.PurchaseUnitNameSnapshot))
                        detail.PurchaseUnitNameSnapshot = unit;
                }

                existingPo.TotalQty = totalQty;
                existingPo.TotalAmount = existingPo.Details
                    .Where(d => !d.IsDeleted)
                    .Sum(d => d.LineAmount);
                existingPo.Note = command.Note ?? existingPo.Note;
                existingPo.RevisionNumber = Math.Max(1, existingPo.RevisionNumber) + 1;
                existingPo.LastAmendedAtUtc = DateTime.UtcNow;
                existingPo.LastAmendedByUserId = actor;
                existingPo.RowVersion = expectedRowVersion + 1;
                existingPo.OrderedAtUtc ??= DateTime.UtcNow;
                existingPo.SubmittedAtUtc = DateTime.UtcNow;
                existingPo.SubmittedByUserId = actor ?? existingPo.SubmittedByUserId;

                _repository.AddPurchaseOrderRevision(new PurchaseOrderRevision
                {
                    TenantId = _tenantContext.CurrentTenantId,
                    PurchaseOrderId = existingPo.Id,
                    RevisionNumber = existingPo.RevisionNumber,
                    PreviousDataSnapshotJson = previousSnapshot,
                    NewDataSnapshotJson = BuildPoLinesSnapshot(existingPo),
                    ChangedByUserId = actor,
                    ChangedAtUtc = DateTime.UtcNow,
                    ChangeReason = "AMEND_BEFORE_CUTOFF"
                });

                createdPos.Add(existingPo);

                result.Status = OrderMatrixRules.ResultAmended;
                result.PurchaseOrderId = existingPo.Id;
                result.PurchaseOrderCode = existingPo.Code;
                result.RevisionNumber = existingPo.RevisionNumber;
                groupResults.Add(result);
            }
            else
            {
                var poCode = await _repository.GeneratePoCodeAsync(deliveryDate, cancellationToken);
                var actor = await ResolveValidUserIdAsync(cancellationToken);

                var po = new PurchaseOrder
                {
                    TenantId = _tenantContext.CurrentTenantId,
                    Code = poCode,
                    BranchId = command.BranchId,
                    Branch = branch,
                    SupplierId = supplierId,
                    Supplier = supplier,
                    DeliveryDate = deliveryDate,
                    Status = "PENDING_APPROVAL",
                    TotalQty = totalQty,
                    TotalAmount = totalAmount,
                    Note = command.Note,
                    ParentDraftId = command.ParentDraftId ?? draft?.Id,
                    CreatedByRole = _tenantContext.CurrentRole,
                    SubmittedByUserId = actor,
                    SubmittedAtUtc = DateTime.UtcNow,
                    OrderedAtUtc = DateTime.UtcNow,
                    RevisionNumber = 1,
                    SupplierCodeSnapshot = supplier?.Code ?? supCode,
                    SupplierNameSnapshot = supplier?.Name ?? supName,
                    SupplierEmailSnapshot = supplier?.Email,
                    StoreCodeSnapshot = branch.BranchCode,
                    StoreNameSnapshot = branch.Name,
                    StoreAddressSnapshot = branch.Address,
                    StorePhoneSnapshot = branch.Phone
                };

                foreach (var line in validLines)
                {
                    var unit = string.IsNullOrWhiteSpace(line.Unit) ? "Gói" : line.Unit;
                    po.Details.Add(new PurchaseOrderDetail
                    {
                        TenantId = _tenantContext.CurrentTenantId,
                        PurchaseOrderId = po.Id,
                        ProductId = line.Product.Id,
                        DeliveryDate = deliveryDate,
                        EmployeeQty = line.DraftDetail?.EmployeeQty ?? line.Qty,
                        StoreQty = line.DraftDetail?.StoreQty,
                        IsOwnerAdjusted = line.DraftDetail?.IsOwnerAdjusted ?? false,
                        OrderedQty = line.Qty,
                        FinalQty = line.Qty,
                        UnitPrice = line.UnitPrice,
                        DeliveryUoM = unit,
                        LineAmount = line.Qty * line.UnitPrice,
                        ProductCodeSnapshot = line.Product.Sku,
                        ProductNameSnapshot = line.Product.Name,
                        PurchaseUnitNameSnapshot = unit
                    });
                }

                await _repository.AddPurchaseOrderAsync(po, cancellationToken);
                createdPos.Add(po);

                // Giữ draft qty để còn amend trước cutoff; hub dùng DayStatus + PoCode để phân biệt.
                result.Status = OrderMatrixRules.ResultSuccess;
                result.PurchaseOrderId = po.Id;
                result.PurchaseOrderCode = po.Code;
                result.RevisionNumber = po.RevisionNumber;
                groupResults.Add(result);
            }
        }

        if (!isDryRun && createdPos.Count > 0)
        {
            if (draft != null)
            {
                draft.TotalQty = draft.Details.Sum(d =>
                    OrderMatrixRules.ComputeFinalQty(d.EmployeeQty ?? 0m, d.StoreQty, d.IsOwnerAdjusted));
                draft.TotalAmount = draft.Details.Sum(d =>
                {
                    var q = OrderMatrixRules.ComputeFinalQty(d.EmployeeQty ?? 0m, d.StoreQty, d.IsOwnerAdjusted);
                    return q * d.UnitPrice;
                });
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return new SplitMatrixBatchResultDto
        {
            BranchId = command.BranchId,
            Scope = command.Scope,
            ServerNow = now,
            TotalGroups = groupResults.Count,
            SuccessCount = groupResults.Count(r => r.Status == OrderMatrixRules.ResultSuccess),
            AmendedCount = groupResults.Count(r => r.Status == OrderMatrixRules.ResultAmended),
            NoChangesCount = groupResults.Count(r => r.Status == OrderMatrixRules.ResultNoChanges),
            FailedCount = groupResults.Count(r => r.Status == OrderMatrixRules.ResultFailed),
            SkippedCount = groupResults.Count(r => r.Status == OrderMatrixRules.ResultSkipped),
            AlreadyFinalizedCount = groupResults.Count(r => r.Status == OrderMatrixRules.ResultAlreadyFinalized),
            Results = groupResults,
            CreatedPurchaseOrders = createdPos.Select(po => MapToDto(po, branch.Name, po.Supplier?.Name ?? "NCC")).ToList()
        };
    }

    private static string BuildPoLinesSnapshot(PurchaseOrder po)
    {
        var lines = (po.Details ?? Array.Empty<PurchaseOrderDetail>())
            .Where(d => !d.IsDeleted)
            .Select(d => new
            {
                d.ProductId,
                ProductCode = d.ProductCodeSnapshot,
                Qty = d.OrderedQty ?? d.FinalQty ?? 0m,
                d.UnitPrice,
                d.LineAmount
            })
            .OrderBy(x => x.ProductCode)
            .ToList();

        return JsonSerializer.Serialize(new
        {
            po.Code,
            po.RevisionNumber,
            po.TotalQty,
            po.TotalAmount,
            Lines = lines
        });
    }

    public async Task<PagedResult<PurchaseOrderDto>> GetPagedPurchaseOrdersAsync(
        PurchaseOrderFilterQuery query,
        CancellationToken cancellationToken = default)
    {
        var pageIndex = Math.Max(1, query.PageIndex);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        var (items, totalCount) = await _repository.GetPagedPurchaseOrdersAsync(
            query.BranchId,
            query.SupplierId,
            query.Status,
            query.DateFrom,
            query.DateTo,
            pageIndex,
            pageSize,
            cancellationToken);

        var dtos = items.Select(po => MapToDto(po, po.Branch?.Name ?? "Chi nhánh", po.Supplier?.Name ?? "NCC")).ToList();
        await AttachActiveGoodsReceiptsAsync(dtos, cancellationToken);

        return new PagedResult<PurchaseOrderDto>
        {
            Items = dtos,
            Page = pageIndex,
            PageSize = pageSize,
            TotalItems = totalCount
        };
    }

    public async Task<PurchaseOrderDto> GetPurchaseOrderByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var po = await _repository.GetPurchaseOrderByIdAsync(id, cancellationToken);
        if (po == null || po.IsDeleted)
        {
            throw new NotFoundException("Đơn đặt hàng (PO)", id);
        }

        var dto = MapToDto(po, po.Branch?.Name ?? "Chi nhánh", po.Supplier?.Name ?? "NCC");
        await AttachActiveGoodsReceiptsAsync(new List<PurchaseOrderDto> { dto }, cancellationToken);
        return dto;
    }

    public async Task<PurchaseOrderDto> UpdatePurchaseOrderStatusAsync(
        Guid id,
        string status,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var po = await _repository.GetPurchaseOrderByIdAsync(id, cancellationToken);
        if (po == null || po.IsDeleted)
        {
            throw new NotFoundException("Đơn đặt hàng (PO)", id);
        }

        var normalizedStatus = status.Trim().ToUpper();
        po.Status = normalizedStatus;

        if (normalizedStatus == "APPROVED")
        {
            po.ApprovedByUserId = await ResolveValidUserIdAsync(cancellationToken);
            po.ApprovedAtUtc = DateTime.UtcNow;
        }
        else if (normalizedStatus == "REJECTED" || normalizedStatus == "CANCELLED")
        {
            po.RejectedByUserId = await ResolveValidUserIdAsync(cancellationToken);
            po.RejectedAtUtc = DateTime.UtcNow;
            po.RejectionReason = reason;
        }
        else if (normalizedStatus == "DISPATCHED" || normalizedStatus == "SENT")
        {
            po.DispatchedAtUtc = DateTime.UtcNow;
        }
        else if (normalizedStatus == "CONFIRMED")
        {
            po.ConfirmedBySupplierAtUtc = DateTime.UtcNow;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var dto = MapToDto(po, po.Branch?.Name ?? "Chi nhánh", po.Supplier?.Name ?? "NCC");
        await AttachActiveGoodsReceiptsAsync(new List<PurchaseOrderDto> { dto }, cancellationToken);
        return dto;
    }

    private async Task AttachActiveGoodsReceiptsAsync(
        IList<PurchaseOrderDto> dtos,
        CancellationToken cancellationToken)
    {
        if (dtos.Count == 0) return;

        var poIds = dtos.Select(d => d.Id).ToList();
        var receipts = await _repository.GetActiveGoodsReceiptsByPoIdsAsync(poIds, cancellationToken);
        foreach (var dto in dtos)
        {
            if (!receipts.TryGetValue(dto.Id, out var receipt)) continue;
            dto.ActiveGoodsReceiptId = receipt.ReceiptId;
            dto.ActiveGoodsReceiptCode = receipt.Code;
            dto.ActiveGoodsReceiptStatus = receipt.Status;
        }
    }

    private static PurchaseOrderDto MapToDto(PurchaseOrder po, string branchName, string supplierName)
    {
        var details = po.Details?.Select(d => new PurchaseOrderDetailDto
        {
            Id = d.Id,
            ProductId = d.ProductId,
            ProductName = d.Product?.Name ?? "Sản phẩm",
            Sku = d.Product?.ProductCode ?? string.Empty,
            Unit = d.DeliveryUoM,
            EmployeeQty = d.EmployeeQty,
            StoreQty = d.StoreQty,
            IsOwnerAdjusted = d.IsOwnerAdjusted,
            FinalQty = d.FinalQty,
            OrderedQty = d.OrderedQty ?? d.FinalQty ?? 0m,
            ReceivedQty = d.ReceivedQty,
            UnitPrice = d.UnitPrice,
            LineAmount = d.LineAmount,
            DeliveryDate = d.DeliveryDate
        }).ToList() ?? new List<PurchaseOrderDetailDto>();

        return new PurchaseOrderDto
        {
            Id = po.Id,
            Code = po.Code,
            BranchId = po.BranchId,
            BranchCode = po.Branch?.BranchCode ?? po.StoreCodeSnapshot ?? "BR",
            BranchName = branchName,
            SupplierId = po.SupplierId,
            SupplierCode = po.Supplier?.Code ?? po.SupplierCodeSnapshot ?? "SUP",
            SupplierName = supplierName,
            DeliveryDate = po.DeliveryDate,
            Status = po.Status,
            RevisionNumber = po.RevisionNumber <= 0 ? 1 : po.RevisionNumber,
            LastAmendedAtUtc = po.LastAmendedAtUtc,
            TotalQty = po.TotalQty,
            TotalAmount = po.TotalAmount,
            Note = po.Note,
            CreatedByRole = po.CreatedByRole,
            ParentDraftId = po.ParentDraftId,
            CreatedAt = po.CreatedAt,
            Details = details
        };
    }
}
