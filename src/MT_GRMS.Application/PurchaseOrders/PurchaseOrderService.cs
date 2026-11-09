using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.PurchaseOrders.Dtos;
using MT_GRMS.Domain.Entities.Supplier;
using MT_GRMS.Domain.Exceptions;

namespace MT_GRMS.Application.PurchaseOrders;

public class PurchaseOrderService : IPurchaseOrderService
{
    private readonly IPurchaseOrderRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITenantContext _tenantContext;

    public PurchaseOrderService(
        IPurchaseOrderRepository repository,
        IUnitOfWork unitOfWork,
        ITenantContext tenantContext)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _tenantContext = tenantContext;
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

        var draftPo = await _repository.GetDraftMatrixPoAsync(query.BranchId, query.SupplierId, query.ViewFrom, cancellationToken);

        var cells = new List<OrderMatrixCellDto>();
        if (draftPo != null && draftPo.Details != null)
        {
            foreach (var detail in draftPo.Details)
            {
                if (detail.DeliveryDate.HasValue)
                {
                    var finalQty = detail.FinalQty ?? (detail.IsOwnerAdjusted ? (detail.StoreQty ?? 0m) : (detail.EmployeeQty ?? 0m));
                    cells.Add(new OrderMatrixCellDto
                    {
                        ProductId = detail.ProductId,
                        DeliveryDate = detail.DeliveryDate.Value,
                        EmployeeQty = detail.EmployeeQty ?? 0m,
                        StoreQty = detail.StoreQty,
                        IsOwnerAdjusted = detail.IsOwnerAdjusted,
                        FinalQty = finalQty
                    });
                }
            }
        }

        return new OrderMatrixResponse
        {
            BranchId = branch.Id,
            BranchName = branch.Name,
            SupplierId = query.SupplierId,
            ViewFrom = query.ViewFrom,
            DurationDays = query.DurationDays,
            Products = products,
            Cells = cells,
            SupplierConfigs = supplierConfigs,
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

        var draft = await _repository.GetDraftMatrixPoAsync(command.BranchId, command.SupplierId, command.ViewFrom, cancellationToken);

        if (draft == null)
        {
            var code = $"DRAFT-{DateTime.UtcNow:yyyyMMdd}-{branch.Code}-{Guid.NewGuid().ToString().Substring(0, 4).ToUpper()}";
            var effectiveSupplierId = command.SupplierId;
            if (!effectiveSupplierId.HasValue || effectiveSupplierId.Value == Guid.Empty)
            {
                var configs = await _repository.GetMatrixSupplierConfigsAsync(command.BranchId, null, cancellationToken);
                effectiveSupplierId = configs.FirstOrDefault()?.SupplierId;
            }

            draft = new PurchaseOrder
            {
                TenantId = _tenantContext.CurrentTenantId,
                Code = code,
                BranchId = command.BranchId,
                SupplierId = effectiveSupplierId ?? Guid.Empty,
                ViewFromDate = command.ViewFrom,
                ViewToDate = command.ViewTo,
                Status = "DRAFT",
                Note = command.Note,
                CreatedByRole = _tenantContext.CurrentRole,
                SubmittedByUserId = await ResolveValidUserIdAsync(cancellationToken),
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

        var role = (_tenantContext.CurrentRole ?? string.Empty).Trim().ToUpperInvariant();
        bool isStoreOwner = string.IsNullOrEmpty(role) || role is "STORE_OWNER" or "ADMIN";

        // Map existing details theo (ProductId, DeliveryDate) để thực hiện upsert an toàn mà không làm mất lịch sử
        var existingDetails = draft.Details
            .Where(d => d.DeliveryDate.HasValue)
            .ToDictionary(d => (d.ProductId, d.DeliveryDate!.Value.Date));

        foreach (var cell in command.Cells)
        {
            var cellDate = cell.DeliveryDate.Date;
            existingDetails.TryGetValue((cell.ProductId, cellDate), out var detail);

            decimal employeeQty;
            decimal? storeQty;
            bool isOwnerAdjusted;

            if (isStoreOwner)
            {
                // Store Owner điều chỉnh
                storeQty = cell.StoreQty;
                isOwnerAdjusted = cell.IsOwnerAdjusted || cell.StoreQty.HasValue;
                // Giữ nguyên đề xuất ban đầu của Employee từ detail cũ nếu có
                employeeQty = detail?.EmployeeQty ?? cell.EmployeeQty;
            }
            else
            {
                // Employee nhập số lượng đề xuất: chỉ được cập nhật EmployeeQty
                employeeQty = cell.EmployeeQty;
                // Không được phép can thiệp owner override nếu đã tồn tại
                storeQty = detail?.StoreQty;
                isOwnerAdjusted = detail?.IsOwnerAdjusted ?? false;
            }

            // Tính finalQuantity chuẩn nghiệp vụ:
            // finalQuantity = isOwnerAdjusted ? (ownerQuantity ?? 0) : employeeQuantity;
            decimal finalQty = isOwnerAdjusted
                ? (storeQty ?? 0m)
                : employeeQty;

            if (detail == null)
            {
                // Nếu chưa có detail và cả 2 bên đều không có số lượng thì bỏ qua
                if (finalQty == 0 && employeeQty == 0 && !isOwnerAdjusted)
                {
                    continue;
                }

                var product = await _repository.GetProductByIdAsync(cell.ProductId, cancellationToken);
                var unitPrice = product?.CostPrice ?? product?.SellingPrice ?? 0m;
                var lineAmt = finalQty * unitPrice;

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
                    DeliveryUoM = product?.BaseUnit ?? "Gói",
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

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return MapToDto(draft, branch.Name, "Nhiều nhà cung cấp");
    }

    public async Task<IReadOnlyList<PurchaseOrderDto>> SplitMatrixPoAsync(
        SplitMatrixPoCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.Groups == null || !command.Groups.Any())
        {
            throw new BusinessException("EMPTY_SPLIT_GROUPS", "Không có đơn PO nào được chỉ định để tách.");
        }

        EnsureBranchAccess(command.BranchId);

        var branch = await _repository.GetBranchByIdAsync(command.BranchId, cancellationToken);
        if (branch == null || branch.IsDeleted)
        {
            throw new NotFoundException("Chi nhánh", command.BranchId);
        }

        var createdPos = new List<PurchaseOrder>();

        foreach (var group in command.Groups)
        {
            var validItems = group.Items.Where(i => i.Quantity > 0).ToList();
            if (!validItems.Any())
            {
                continue;
            }

            var supplier = await _repository.GetSupplierByIdAsync(group.SupplierId, cancellationToken);
            var supplierName = supplier?.Name ?? "Nhà cung cấp";

            var poCode = await _repository.GeneratePoCodeAsync(group.DeliveryDate, cancellationToken);

            var totalQty = validItems.Sum(i => i.Quantity);
            var totalAmount = validItems.Sum(i => i.Quantity * i.UnitPrice);

            var po = new PurchaseOrder
            {
                TenantId = _tenantContext.CurrentTenantId,
                Code = poCode,
                BranchId = command.BranchId,
                Branch = branch,
                SupplierId = group.SupplierId,
                Supplier = supplier,
                DeliveryDate = group.DeliveryDate,
                Status = "PENDING_APPROVAL",
                TotalQty = totalQty,
                TotalAmount = totalAmount,
                Note = command.Note,
                ParentDraftId = command.ParentDraftId,
                CreatedByRole = _tenantContext.CurrentRole,
                SubmittedByUserId = await ResolveValidUserIdAsync(cancellationToken),
                SubmittedAtUtc = DateTime.UtcNow,
                OrderedAtUtc = DateTime.UtcNow
            };

            foreach (var item in validItems)
            {
                po.Details.Add(new PurchaseOrderDetail
                {
                    TenantId = _tenantContext.CurrentTenantId,
                    PurchaseOrderId = po.Id,
                    ProductId = item.ProductId,
                    DeliveryDate = group.DeliveryDate,
                    OrderedQty = item.Quantity,
                    FinalQty = item.Quantity,
                    UnitPrice = item.UnitPrice,
                    DeliveryUoM = string.IsNullOrWhiteSpace(item.Unit) ? "Gói" : item.Unit,
                    LineAmount = item.Quantity * item.UnitPrice
                });
            }

            await _repository.AddPurchaseOrderAsync(po, cancellationToken);
            createdPos.Add(po);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return createdPos.Select(po => MapToDto(po, branch.Name, po.Supplier?.Name ?? "NCC")).ToList();
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

        return MapToDto(po, po.Branch?.Name ?? "Chi nhánh", po.Supplier?.Name ?? "NCC");
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

        return MapToDto(po, po.Branch?.Name ?? "Chi nhánh", po.Supplier?.Name ?? "NCC");
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
            BranchCode = po.Branch?.BranchCode ?? "BR",
            BranchName = branchName,
            SupplierId = po.SupplierId,
            SupplierCode = po.Supplier?.Code ?? "SUP",
            SupplierName = supplierName,
            DeliveryDate = po.DeliveryDate,
            Status = po.Status,
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
