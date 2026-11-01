using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.GoodsReceipts.Dtos;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Domain.Entities.Supplier;
using MT_GRMS.Domain.Exceptions;

namespace MT_GRMS.Application.GoodsReceipts;

public class GoodsReceiptService : IGoodsReceiptService
{
    private readonly IGoodsReceiptRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITenantContext _tenantContext;

    public GoodsReceiptService(
        IGoodsReceiptRepository repository,
        IUnitOfWork unitOfWork,
        ITenantContext tenantContext)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _tenantContext = tenantContext;
    }

    public async Task<PagedResult<GoodsReceiptDto>> GetGoodsReceiptsAsync(
        GoodsReceiptFilterQuery query,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.CurrentTenantId ?? string.Empty;
        return await _repository.GetPagedAsync(tenantId, query, cancellationToken);
    }

    public async Task<GoodsReceiptDto> GetGoodsReceiptByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.CurrentTenantId ?? string.Empty;
        var receipt = await _repository.GetByIdAsync(tenantId, id, cancellationToken);
        if (receipt == null || receipt.IsDeleted)
        {
            throw new NotFoundException("Phiếu nhập kho", id);
        }

        return MapToDto(receipt);
    }

    public async Task<List<PurchaseOrderLookupDto>> GetPoLookupAsync(Guid? branchId, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.CurrentTenantId ?? string.Empty;
        var pos = await _repository.GetEligiblePurchaseOrdersAsync(tenantId, branchId, cancellationToken);

        return pos.Select(po => new PurchaseOrderLookupDto
        {
            Id = po.Id,
            Code = po.Code,
            SupplierId = po.SupplierId,
            SupplierCode = po.Supplier?.Code ?? string.Empty,
            SupplierName = po.Supplier?.Name ?? string.Empty,
            BranchId = po.BranchId,
            BranchCode = po.Branch?.BranchCode ?? string.Empty,
            BranchName = po.Branch?.Name ?? string.Empty,
            DeliveryDate = po.DeliveryDate,
            TotalQty = po.TotalQty,
            TotalAmount = po.TotalAmount,
            Status = po.Status
        }).ToList();
    }

    public async Task<PoReceiptDetailsDto> GetPoReceiptDetailsAsync(Guid poId, CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.CurrentTenantId ?? string.Empty;
        var po = await _repository.GetPurchaseOrderWithDetailsAsync(tenantId, poId, cancellationToken);
        if (po == null || po.IsDeleted)
        {
            throw new NotFoundException("Đơn đặt hàng PO", poId);
        }

        // Kiểm tra xem PO đã có phiếu nhập kho DRAFT hoặc COMPLETED chưa (Mục 4)
        var existingReceipt = await _repository.GetActiveReceiptByPoIdAsync(tenantId, poId, cancellationToken);
        if (existingReceipt != null)
        {
            throw new BusinessException(
                "PO_HAS_ACTIVE_RECEIPT",
                $"Đơn hàng {po.Code} đã có phiếu nhập kho {existingReceipt.Code} ở trạng thái {existingReceipt.Status}. Không thể tạo thêm phiếu nhập mới.");
        }

        return new PoReceiptDetailsDto
        {
            Id = po.Id,
            Code = po.Code,
            SupplierId = po.SupplierId,
            SupplierCode = po.Supplier?.Code ?? string.Empty,
            SupplierName = po.Supplier?.Name ?? string.Empty,
            BranchId = po.BranchId,
            BranchCode = po.Branch?.BranchCode ?? string.Empty,
            BranchName = po.Branch?.Name ?? string.Empty,
            DeliveryDate = po.DeliveryDate,
            TotalQty = po.TotalQty,
            TotalAmount = po.TotalAmount,
            Items = po.Details.Select(d => new PoReceiptLineItemDto
            {
                PurchaseOrderDetailId = d.Id,
                ProductId = d.ProductId,
                ProductCode = d.Product?.ProductCode ?? string.Empty,
                ProductName = d.Product?.Name ?? string.Empty,
                Unit = d.Product?.BaseUnit ?? "Cái",
                HasBatches = d.Product?.HasBatches ?? false,
                OrderedQty = d.OrderedQty ?? d.FinalQty ?? 0,
                UnitPrice = d.UnitPrice,
                LineAmount = d.LineAmount
            }).ToList()
        };
    }

    public async Task<GoodsReceiptDto> CreateGoodsReceiptAsync(
        CreateGoodsReceiptCommand command,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.CurrentTenantId ?? string.Empty;

        // 1. Kiểm tra đơn PO gốc
        var po = await _repository.GetPurchaseOrderWithDetailsAsync(tenantId, command.PurchaseOrderId, cancellationToken);
        if (po == null || po.IsDeleted)
        {
            throw new NotFoundException("Đơn đặt hàng PO", command.PurchaseOrderId);
        }

        // 2. Ràng buộc: Một PO chỉ có tối đa một phiếu nhập chưa bị hủy (Mục 4)
        var activeReceipt = await _repository.GetActiveReceiptByPoIdAsync(tenantId, command.PurchaseOrderId, cancellationToken);
        if (activeReceipt != null)
        {
            throw new BusinessException(
                "PO_ALREADY_HAS_RECEIPT",
                $"Đơn đặt hàng {po.Code} đã có phiếu nhập {activeReceipt.Code} ({activeReceipt.Status}). Mỗi đơn PO chỉ có tối đa một phiếu nhập chưa bị hủy.");
        }

        var code = await _repository.GenerateCodeAsync(tenantId, cancellationToken);

        var receipt = new GoodsReceipt
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Code = code,
            PurchaseOrderId = command.PurchaseOrderId,
            SupplierId = command.SupplierId,
            BranchId = command.BranchId,
            InvoiceNo = command.InvoiceNo,
            ReceivedAtUtc = command.ReceivedAtUtc ?? DateTime.UtcNow,
            ReceiverUserId = _tenantContext.CurrentUserId.ToString(),
            FreightFee = command.FreightFee,
            DiscountAmount = command.DiscountAmount,
            VatPercent = command.VatPercent,
            PaymentMethod = command.PaymentMethod,
            PaymentDueDate = command.PaymentDueDate,
            Note = command.Note,
            CreatedAt = DateTime.UtcNow
        };

        // 3. Tính toán và xác thực từng dòng sản phẩm
        decimal subTotal = 0;
        foreach (var l in command.Lines)
        {
            var lineAmount = l.AcceptedQty * l.UnitPrice;
            subTotal += lineAmount;
        }

        receipt.SubTotal = subTotal;
        var baseAmount = subTotal - command.DiscountAmount + command.FreightFee;
        receipt.VatAmount = Math.Round(Math.Max(0, baseAmount) * (command.VatPercent / 100m), 2);
        receipt.GrandTotal = Math.Max(0, baseAmount) + receipt.VatAmount;

        foreach (var l in command.Lines)
        {
            // Kiểm tra quy tắc số lượng: 0 <= AcceptedQty <= DeliveredQty
            if (l.AcceptedQty < 0 || l.DeliveredQty < 0)
            {
                throw new BusinessException("INVALID_QTY", "Số lượng nhận và chấp nhận không được âm.");
            }

            if (l.AcceptedQty > l.DeliveredQty)
            {
                throw new BusinessException("ACCEPTED_EXCEEDS_RECEIVED", "Số lượng chấp nhận không được vượt quá số lượng thực nhận.");
            }

            // Giao thừa: Không được nhập vượt quá số lượng đặt theo PO vào tồn kho (Mục 7.2)
            if (l.AcceptedQty > l.OrderedQty)
            {
                throw new BusinessException("ACCEPTED_EXCEEDS_ORDERED", "Số lượng chấp nhận nhập kho không được vượt quá số lượng đặt trên PO.");
            }

            var rejectedQty = l.DeliveredQty - l.AcceptedQty;
            if (rejectedQty > 0 && string.IsNullOrWhiteSpace(l.RejectionReason))
            {
                throw new BusinessException("REJECTION_REASON_REQUIRED", $"Dòng sản phẩm có {rejectedQty} hàng bị từ chối bắt buộc phải có lý do từ chối.");
            }

            // Phân bổ chi phí Landed cost
            var lineAmount = l.AcceptedQty * l.UnitPrice;
            var weight = subTotal > 0 ? (lineAmount / subTotal) : 0;
            var freightAlloc = Math.Round(command.FreightFee * weight, 2);
            var discountAlloc = Math.Round(command.DiscountAmount * weight, 2);
            var landedSubtotal = lineAmount - discountAlloc + freightAlloc;
            var vatAlloc = Math.Round(landedSubtotal * (command.VatPercent / 100m), 2);
            var landedTotal = landedSubtotal + vatAlloc;
            var landedUnitCost = l.AcceptedQty > 0 ? Math.Round(landedTotal / l.AcceptedQty, 2) : l.UnitPrice;

            var line = new GoodsReceiptLine
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                GoodsReceiptId = receipt.Id,
                ProductId = l.ProductId,
                PurchaseOrderDetailId = l.PurchaseOrderDetailId,
                OrderedQty = l.OrderedQty,
                DeliveredQty = l.DeliveredQty,
                AcceptedQty = l.AcceptedQty,
                RejectedQty = rejectedQty,
                UnitPrice = l.UnitPrice,
                LineAmount = lineAmount,
                BatchCode = l.BatchCode,
                ManufactureDate = l.ManufactureDate,
                ExpiryDate = l.ExpiryDate,
                FreightAllocated = freightAlloc,
                DiscountAllocated = discountAlloc,
                VatAllocated = vatAlloc,
                LandedUnitCost = landedUnitCost,
                QualityStatus = rejectedQty > 0 ? "PARTIAL_REJECT" : "ACCEPTED",
                RejectionReason = l.RejectionReason,
                Note = string.IsNullOrWhiteSpace(l.RejectionNote) ? l.Note : $"{l.RejectionNote} | {l.Note}",
                CreatedAt = DateTime.UtcNow
            };

            receipt.Lines.Add(line);
        }

        if (command.AutoComplete)
        {
            ValidateForCompletion(receipt);
            receipt.Status = "COMPLETED";
            receipt.ConfirmedByUserId = _tenantContext.CurrentUserId.ToString();
            receipt.ConfirmedAtUtc = DateTime.UtcNow;

            await ApplyStockAndPoUpdatesAsync(tenantId, receipt, po, cancellationToken);
        }
        else
        {
            receipt.Status = "DRAFT";
        }

        await _repository.AddAsync(receipt, cancellationToken);

        return await GetGoodsReceiptByIdAsync(receipt.Id, cancellationToken);
    }

    public async Task<GoodsReceiptDto> CompleteGoodsReceiptAsync(
        Guid id,
        CompleteGoodsReceiptCommand command,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.CurrentTenantId ?? string.Empty;
        var receipt = await _repository.GetByIdAsync(tenantId, id, cancellationToken);
        if (receipt == null || receipt.IsDeleted)
        {
            throw new NotFoundException("Phiếu nhập kho", id);
        }

        // Idempotency (Mục 7.10)
        if (receipt.Status == "COMPLETED")
        {
            throw new BusinessException("ALREADY_COMPLETED", "Phiếu nhập kho này đã được hoàn tất trước đó.");
        }

        if (receipt.Status == "CANCELLED")
        {
            throw new BusinessException("CANNOT_COMPLETE_CANCELLED", "Không thể hoàn tất phiếu nhập kho đã bị hủy.");
        }

        ValidateForCompletion(receipt);

        PurchaseOrder? po = null;
        if (receipt.PurchaseOrderId.HasValue)
        {
            po = await _repository.GetPurchaseOrderWithDetailsAsync(tenantId, receipt.PurchaseOrderId.Value, cancellationToken);
        }

        receipt.Status = "COMPLETED";
        receipt.ConfirmedByUserId = _tenantContext.CurrentUserId.ToString();
        receipt.ConfirmedAtUtc = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(command.Note))
        {
            receipt.Note = string.IsNullOrWhiteSpace(receipt.Note) ? command.Note : $"{receipt.Note} | {command.Note}";
        }
        receipt.UpdatedAt = DateTime.UtcNow;

        await ApplyStockAndPoUpdatesAsync(tenantId, receipt, po, cancellationToken);

        await _repository.UpdateAsync(receipt, cancellationToken);

        return await GetGoodsReceiptByIdAsync(receipt.Id, cancellationToken);
    }

    public async Task<GoodsReceiptDto> CancelGoodsReceiptAsync(
        Guid id,
        CancelGoodsReceiptCommand command,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.CurrentTenantId ?? string.Empty;
        var receipt = await _repository.GetByIdAsync(tenantId, id, cancellationToken);
        if (receipt == null || receipt.IsDeleted)
        {
            throw new NotFoundException("Phiếu nhập kho", id);
        }

        // Quy tắc Mục 8.1 & 11: Chỉ phiếu DRAFT mới được hủy, không cho hủy COMPLETED
        if (receipt.Status == "COMPLETED")
        {
            throw new BusinessException("CANNOT_CANCEL_COMPLETED", "Không cho phép hủy phiếu nhập kho đã ở trạng thái COMPLETED.");
        }

        if (receipt.Status == "CANCELLED")
        {
            throw new BusinessException("ALREADY_CANCELLED", "Phiếu nhập kho này đã bị hủy trước đó.");
        }

        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            throw new BusinessException("REASON_REQUIRED", "Bắt buộc phải ghi nhận lý do khi hủy phiếu nhập kho.");
        }

        receipt.Status = "CANCELLED";
        receipt.RejectedByUserId = _tenantContext.CurrentUserId.ToString();
        receipt.RejectedAtUtc = DateTime.UtcNow;
        receipt.RejectionReason = command.Reason;
        receipt.UpdatedAt = DateTime.UtcNow;

        await _repository.UpdateAsync(receipt, cancellationToken);

        return await GetGoodsReceiptByIdAsync(receipt.Id, cancellationToken);
    }

    private static void ValidateForCompletion(GoodsReceipt receipt)
    {
        foreach (var line in receipt.Lines)
        {
            if (line.AcceptedQty < 0 || line.DeliveredQty < 0)
            {
                throw new BusinessException("INVALID_QTY", $"Số lượng không hợp lệ tại dòng {line.Product?.Name}.");
            }

            if (line.AcceptedQty > line.DeliveredQty)
            {
                throw new BusinessException("ACCEPTED_EXCEEDS_RECEIVED", $"Số lượng chấp nhận không được vượt quá số lượng thực nhận tại dòng {line.Product?.Name}.");
            }

            if (line.RejectedQty > 0 && string.IsNullOrWhiteSpace(line.RejectionReason))
            {
                throw new BusinessException("REJECTION_REASON_REQUIRED", $"Dòng {line.Product?.Name} có hàng bị từ chối, bắt buộc phải chọn lý do từ chối.");
            }

            // Kiểm tra lô hạn với sản phẩm quản lý HSD (Mục 5 Bước 4 & 5)
            if (line.Product?.HasBatches == true && line.AcceptedQty > 0)
            {
                if (string.IsNullOrWhiteSpace(line.BatchCode))
                {
                    throw new BusinessException("BATCH_CODE_REQUIRED", $"Sản phẩm {line.Product?.Name} quản lý HSD bắt buộc phải nhập mã lô.");
                }

                if (!line.ExpiryDate.HasValue)
                {
                    throw new BusinessException("EXPIRY_DATE_REQUIRED", $"Sản phẩm {line.Product?.Name} quản lý HSD bắt buộc phải nhập hạn sử dụng.");
                }

                if (line.ExpiryDate.Value.Date < receipt.ReceivedAtUtc.Date)
                {
                    throw new BusinessException("EXPIRY_PAST", $"Hạn sử dụng của lô {line.BatchCode} không được trước ngày nhập hàng.");
                }
            }
        }
    }

    private async Task ApplyStockAndPoUpdatesAsync(
        string tenantId,
        GoodsReceipt receipt,
        PurchaseOrder? po,
        CancellationToken cancellationToken)
    {
        // 1. Cập nhật tồn kho chi nhánh BranchStocks (Mục 10: Chỉ AcceptedQty được cộng vào tồn kho)
        foreach (var line in receipt.Lines.Where(l => l.AcceptedQty > 0))
        {
            var stock = await _repository.GetBranchStockAsync(tenantId, receipt.BranchId, line.ProductId, line.BatchCode, cancellationToken);
            if (stock != null)
            {
                stock.QuantityOnHand += line.AcceptedQty;
                stock.LandedCostPerUnit = line.LandedUnitCost;
                if (line.ExpiryDate.HasValue) stock.ExpiryDate = line.ExpiryDate;
                if (line.ManufactureDate.HasValue) stock.ManufactureDate = line.ManufactureDate;
                stock.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                var newStock = new BranchStock
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    BranchId = receipt.BranchId,
                    ProductId = line.ProductId,
                    SupplierId = receipt.SupplierId,
                    BatchCode = line.BatchCode,
                    ManufactureDate = line.ManufactureDate,
                    ExpiryDate = line.ExpiryDate,
                    QuantityOnHand = line.AcceptedQty,
                    QuantityReserved = 0,
                    LandedCostPerUnit = line.LandedUnitCost,
                    CreatedAt = DateTime.UtcNow
                };
                await _repository.AddBranchStockAsync(newStock, cancellationToken);
                line.BranchStockId = newStock.Id;
            }
        }

        // 2. Cập nhật đơn PO nguồn sang COMPLETED (Mục 8.2: "Khi Goods Receipt được hoàn tất, PO chuyển sang COMPLETED, kể cả trường hợp nhà cung cấp giao thiếu.")
        if (po != null)
        {
            foreach (var line in receipt.Lines)
            {
                if (line.PurchaseOrderDetailId.HasValue)
                {
                    var pod = po.Details.FirstOrDefault(d => d.Id == line.PurchaseOrderDetailId.Value);
                    if (pod != null)
                    {
                        pod.ReceivedQty += line.AcceptedQty;
                        pod.UpdatedAt = DateTime.UtcNow;
                    }
                }
            }

            po.Status = "COMPLETED";
            po.UpdatedAt = DateTime.UtcNow;
        }
    }

    private static GoodsReceiptDto MapToDto(GoodsReceipt gr)
    {
        return new GoodsReceiptDto
        {
            Id = gr.Id,
            Code = gr.Code,
            PurchaseOrderId = gr.PurchaseOrderId,
            PurchaseOrderCode = gr.PurchaseOrder?.Code,
            SupplierId = gr.SupplierId,
            SupplierCode = gr.Supplier?.Code ?? string.Empty,
            SupplierName = gr.Supplier?.Name ?? string.Empty,
            BranchId = gr.BranchId,
            BranchCode = gr.Branch?.BranchCode ?? string.Empty,
            BranchName = gr.Branch?.Name ?? string.Empty,
            Status = gr.Status,
            ReceivedAtUtc = gr.ReceivedAtUtc,
            InvoiceNo = gr.InvoiceNo,
            ReceiverUserId = gr.ReceiverUserId,
            ConfirmedByUserId = gr.ConfirmedByUserId,
            ConfirmedAtUtc = gr.ConfirmedAtUtc,
            CancelledByUserId = gr.RejectedByUserId,
            CancelledAtUtc = gr.RejectedAtUtc,
            CancellationReason = gr.RejectionReason,
            SubTotal = gr.SubTotal,
            FreightFee = gr.FreightFee,
            DiscountAmount = gr.DiscountAmount,
            VatPercent = gr.VatPercent,
            VatAmount = gr.VatAmount,
            GrandTotal = gr.GrandTotal,
            PaymentMethod = gr.PaymentMethod,
            PaymentDueDate = gr.PaymentDueDate,
            TotalItems = gr.Lines.Count,
            TotalOrderedQty = gr.Lines.Sum(l => l.OrderedQty),
            TotalReceivedQty = gr.Lines.Sum(l => l.DeliveredQty),
            TotalAcceptedQty = gr.Lines.Sum(l => l.AcceptedQty),
            TotalRejectedQty = gr.Lines.Sum(l => l.RejectedQty),
            HasMismatch = gr.Lines.Any(l => l.AcceptedQty != l.OrderedQty || l.RejectedQty > 0),
            Note = gr.Note,
            Lines = gr.Lines.Select(l => new GoodsReceiptLineDto
            {
                Id = l.Id,
                ProductId = l.ProductId,
                ProductCode = l.Product?.ProductCode ?? string.Empty,
                ProductName = l.Product?.Name ?? string.Empty,
                Unit = l.Product?.BaseUnit ?? "Cái",
                HasBatches = l.Product?.HasBatches ?? false,
                PurchaseOrderDetailId = l.PurchaseOrderDetailId,
                OrderedQty = l.OrderedQty,
                DeliveredQty = l.DeliveredQty,
                AcceptedQty = l.AcceptedQty,
                RejectedQty = l.RejectedQty,
                UnitPrice = l.UnitPrice,
                LineAmount = l.LineAmount,
                BatchCode = l.BatchCode,
                ManufactureDate = l.ManufactureDate,
                ExpiryDate = l.ExpiryDate,
                FreightAllocated = l.FreightAllocated,
                DiscountAllocated = l.DiscountAllocated,
                VatAllocated = l.VatAllocated,
                LandedUnitCost = l.LandedUnitCost,
                QualityStatus = l.QualityStatus,
                RejectionReason = l.RejectionReason,
                Note = l.Note
            }).ToList()
        };
    }
}
