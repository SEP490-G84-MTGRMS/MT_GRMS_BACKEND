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

    public async Task<OrderMatrixResponse> GetOrderMatrixAsync(
        OrderMatrixQuery query,
        CancellationToken cancellationToken = default)
    {
        var branch = await _repository.GetBranchByIdAsync(query.BranchId, cancellationToken);
        if (branch == null || branch.IsDeleted)
        {
            throw new NotFoundException("Chi nhánh", query.BranchId);
        }

        var products = await _repository.GetMatrixProductsAsync(query.BranchId, query.SupplierId, cancellationToken);

        var draftPo = await _repository.GetDraftMatrixPoAsync(query.BranchId, query.SupplierId, query.ViewFrom, cancellationToken);

        var cells = new List<OrderMatrixCellDto>();
        if (draftPo != null && draftPo.Details != null)
        {
            foreach (var detail in draftPo.Details)
            {
                if (detail.DeliveryDate.HasValue)
                {
                    cells.Add(new OrderMatrixCellDto
                    {
                        ProductId = detail.ProductId,
                        DeliveryDate = detail.DeliveryDate.Value,
                        EmployeeQty = detail.EmployeeQty ?? 0m,
                        StoreQty = detail.StoreQty,
                        FinalQty = detail.FinalQty ?? detail.StoreQty ?? detail.EmployeeQty ?? 0m
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
            DraftPoId = draftPo?.Id,
            Status = draftPo?.Status ?? "DRAFT"
        };
    }

    public async Task<PurchaseOrderDto> SaveMatrixDraftAsync(
        SaveMatrixDraftCommand command,
        CancellationToken cancellationToken = default)
    {
        var branch = await _repository.GetBranchByIdAsync(command.BranchId, cancellationToken);
        if (branch == null || branch.IsDeleted)
        {
            throw new NotFoundException("Chi nhánh", command.BranchId);
        }

        var draft = await _repository.GetDraftMatrixPoAsync(command.BranchId, command.SupplierId, command.ViewFrom, cancellationToken);

        if (draft == null)
        {
            var code = $"DRAFT-{DateTime.UtcNow:yyyyMMdd}-{branch.Code}-{Guid.NewGuid().ToString().Substring(0, 4).ToUpper()}";
            draft = new PurchaseOrder
            {
                TenantId = _tenantContext.CurrentTenantId,
                Code = code,
                BranchId = command.BranchId,
                SupplierId = command.SupplierId ?? Guid.Empty,
                ViewFromDate = command.ViewFrom,
                ViewToDate = command.ViewTo,
                Status = "DRAFT",
                Note = command.Note,
                CreatedByRole = _tenantContext.CurrentRole,
                SubmittedByUserId = _tenantContext.CurrentUserId.ToString(),
                SubmittedAtUtc = DateTime.UtcNow
            };
            await _repository.AddPurchaseOrderAsync(draft, cancellationToken);
        }
        else
        {
            draft.ViewFromDate = command.ViewFrom;
            draft.ViewToDate = command.ViewTo;
            draft.Note = command.Note;
            draft.Details.Clear();
        }

        decimal totalQty = 0m;
        decimal totalAmount = 0m;

        foreach (var cell in command.Cells.Where(c => c.FinalQty > 0 || c.EmployeeQty > 0 || (c.StoreQty.HasValue && c.StoreQty.Value > 0)))
        {
            var product = await _repository.GetProductByIdAsync(cell.ProductId, cancellationToken);
            var unitPrice = product?.CostPrice ?? product?.SellingPrice ?? 0m;
            var lineAmt = cell.FinalQty * unitPrice;

            draft.Details.Add(new PurchaseOrderDetail
            {
                TenantId = _tenantContext.CurrentTenantId,
                PurchaseOrderId = draft.Id,
                ProductId = cell.ProductId,
                DeliveryDate = cell.DeliveryDate,
                EmployeeQty = cell.EmployeeQty,
                StoreQty = cell.StoreQty,
                FinalQty = cell.FinalQty,
                OrderedQty = cell.FinalQty,
                UnitPrice = unitPrice,
                DeliveryUoM = product?.BaseUnit ?? "Gói",
                LineAmount = lineAmt
            });

            totalQty += cell.FinalQty;
            totalAmount += lineAmt;
        }

        draft.TotalQty = totalQty;
        draft.TotalAmount = totalAmount;

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
                SubmittedByUserId = _tenantContext.CurrentUserId.ToString(),
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
            po.ApprovedByUserId = _tenantContext.CurrentUserId.ToString();
            po.ApprovedAtUtc = DateTime.UtcNow;
        }
        else if (normalizedStatus == "REJECTED" || normalizedStatus == "CANCELLED")
        {
            po.RejectedByUserId = _tenantContext.CurrentUserId.ToString();
            po.RejectedAtUtc = DateTime.UtcNow;
            po.RejectionReason = reason;
        }
        else if (normalizedStatus == "DISPATCHED")
        {
            po.DispatchedAtUtc = DateTime.UtcNow;
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
            OrderedQty = d.OrderedQty ?? d.FinalQty ?? 0m,
            UnitPrice = d.UnitPrice,
            LineAmount = d.LineAmount,
            DeliveryDate = d.DeliveryDate
        }).ToList() ?? new List<PurchaseOrderDetailDto>();

        return new PurchaseOrderDto
        {
            Id = po.Id,
            Code = po.Code,
            BranchId = po.BranchId,
            BranchName = branchName,
            SupplierId = po.SupplierId,
            SupplierName = supplierName,
            DeliveryDate = po.DeliveryDate,
            Status = po.Status,
            TotalQty = po.TotalQty,
            TotalAmount = po.TotalAmount,
            Note = po.Note,
            ParentDraftId = po.ParentDraftId,
            CreatedAt = po.CreatedAt,
            Details = details
        };
    }
}
