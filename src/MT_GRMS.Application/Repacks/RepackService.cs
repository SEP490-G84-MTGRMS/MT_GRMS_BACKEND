using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Inventory;
using MT_GRMS.Application.Repacks.Dtos;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Domain.Exceptions;

namespace MT_GRMS.Application.Repacks;

public class RepackService : IRepackService
{
    private static readonly HashSet<string> AllowedRepackTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "PORTIONING", "PROCESSING"
    };

    private static readonly HashSet<string> AllowedWasteReasons = new(StringComparer.OrdinalIgnoreCase)
    {
        "PEEL_SEED", "TRIM", "SPILL_DAMAGE", "OTHER"
    };

    private readonly IRepackRepository _repository;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitOfWork _unitOfWork;

    public RepackService(
        IRepackRepository repository,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _tenantContext = tenantContext;
        _unitOfWork = unitOfWork;
    }

    public async Task<BranchStockPackageSummaryDto> GetBranchStockPackagesAsync(
        Guid branchStockId,
        CancellationToken ct = default)
    {
        var tenantId = RequireTenant();
        var stock = await _repository.GetBranchStockAsync(tenantId, branchStockId, ct)
            ?? throw new BusinessException("BRANCH_STOCK_NOT_FOUND", "Không tìm thấy lô tồn kho.");
        BranchScopeGuard.EnsureCanAccessBranch(_tenantContext, stock.BranchId);

        var product = await _repository.GetProductWithUnitsAsync(tenantId, stock.ProductId, ct)
            ?? throw new BusinessException("PRODUCT_NOT_FOUND", "Không tìm thấy sản phẩm.");

        var packages = await _repository.GetPackagesByBranchStockAsync(tenantId, branchStockId, ct);
        var activeWeight = packages
            .Where(p => p.Status is "AVAILABLE" or "QUARANTINED")
            .Sum(p => p.NetWeight);
        var activeCount = packages.Count(p => p.Status is "AVAILABLE" or "QUARANTINED");
        var isWeight = WeightProductHelper.IsWeightProduct(product);
        var raw = isWeight
            ? BranchStockAvailability.GetRawSellableKg(stock, activeWeight)
            : BranchStockAvailability.GetSellableAvailable(stock);

        return new BranchStockPackageSummaryDto(
            BranchStockId: stock.Id,
            ProductId: product.Id,
            ProductName: product.Name,
            BatchCode: stock.BatchCode ?? string.Empty,
            Unit: product.BaseUnit,
            IsWeightProduct: isWeight,
            AllowRepack: product.AllowRepack && isWeight,
            QuantityOnHand: stock.QuantityOnHand,
            QuantityReserved: stock.QuantityReserved,
            QuantityQuarantined: stock.QuantityQuarantined,
            PackagedQty: activeWeight,
            PackageCount: activeCount,
            RawSellableQty: raw,
            ExpiryDate: stock.ExpiryDate,
            LandedCostPerUnit: stock.LandedCostPerUnit,
            SuggestPricePerKg: ResolveProcessedPricePerKg(product, stock),
            Packages: packages.Select(MapPackage).ToList()
        );
    }

    public async Task<PackageScanDto?> FindSellablePackageByCodeAsync(Guid branchId, string code, CancellationToken ct = default)
    {
        if (branchId == Guid.Empty || string.IsNullOrWhiteSpace(code)) return null;
        BranchScopeGuard.EnsureCanAccessBranch(_tenantContext, branchId);
        var package = await _repository.FindPackageByCodeAsync(RequireTenant(), branchId, code.Trim(), ct);
        if (package == null || package.Status != "AVAILABLE" ||
            (package.ExpiryDate.HasValue && package.ExpiryDate.Value.Date < DateTime.UtcNow.Date)) return null;
        var stock = await _repository.GetBranchStockAsync(package.TenantId, package.BranchStockId, ct);
        if (stock == null || stock.BranchId != branchId) return null;
        var product = await _repository.GetProductWithUnitsAsync(package.TenantId, package.ProductId, ct);
        if (product == null) return null;
        return new PackageScanDto(MapPackage(package), product.Name, stock.BatchCode ?? string.Empty, stock.ExpiryDate);
    }

    public async Task<IReadOnlyList<PackageScanDto>> SearchSellablePackagesAsync(
        Guid branchId, string query, int limit = 20, CancellationToken ct = default)
    {
        if (branchId == Guid.Empty || string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<PackageScanDto>();
        }
        BranchScopeGuard.EnsureCanAccessBranch(_tenantContext, branchId);

        var rows = await _repository.SearchSellablePackagesAsync(
            RequireTenant(), branchId, query.Trim(), limit, ct);

        return rows
            .Select(r => new PackageScanDto(
                MapPackage(r.Package),
                r.ProductName,
                r.Stock.BatchCode ?? string.Empty,
                r.Stock.ExpiryDate))
            .ToList();
    }

    public async Task<RepackOrderDto> CreateRepackOrderAsync(
        CreateRepackOrderRequest request,
        CancellationToken ct = default)
    {
        var tenantId = RequireTenant();
        if (request.Packages == null || request.Packages.Count == 0)
        {
            throw new BusinessException("REPACK_NO_PACKAGES", "Cần ít nhất một khay.");
        }

        var repackType = (request.RepackType ?? string.Empty).Trim().ToUpperInvariant();
        if (!AllowedRepackTypes.Contains(repackType))
        {
            throw new BusinessException("REPACK_TYPE_INVALID", "Loại chế biến phải là PORTIONING hoặc PROCESSING.");
        }

        if (request.InputWeight <= 0)
        {
            throw new BusinessException("REPACK_INPUT_INVALID", "Khối lượng lấy ra phải > 0.");
        }

        if (request.WasteWeight < 0)
        {
            throw new BusinessException("REPACK_WASTE_INVALID", "Hao hụt không được âm.");
        }

        if (request.WasteWeight > 0)
        {
            var reason = (request.WasteReason ?? string.Empty).Trim().ToUpperInvariant();
            if (!AllowedWasteReasons.Contains(reason))
            {
                throw new BusinessException("REPACK_WASTE_REASON_REQUIRED", "Cần chọn lý do hao hụt.");
            }
        }

        var packageWeight = request.Packages.Sum(p => p.NetWeight);
        var expected = packageWeight + request.WasteWeight;
        if (Math.Abs(expected - request.InputWeight) > 0.005m)
        {
            throw new BusinessException(
                "REPACK_BALANCE_MISMATCH",
                $"Tổng cân khay ({packageWeight:0.###}) + hao ({request.WasteWeight:0.###}) phải bằng khối lượng lấy ra ({request.InputWeight:0.###}), sai số cho phép ±0,005 kg.");
        }

        var stock = await _repository.GetBranchStockAsync(tenantId, request.BranchStockId, ct)
            ?? throw new BusinessException("BRANCH_STOCK_NOT_FOUND", "Không tìm thấy lô tồn kho.");
        BranchScopeGuard.EnsureCanAccessBranch(_tenantContext, stock.BranchId);

        var product = await _repository.GetProductWithUnitsAsync(tenantId, stock.ProductId, ct)
            ?? throw new BusinessException("PRODUCT_NOT_FOUND", "Không tìm thấy sản phẩm.");

        if (!WeightProductHelper.IsWeightProduct(product))
        {
            throw new BusinessException("NOT_WEIGHT_PRODUCT", "Chỉ sản phẩm đơn vị nhập kg mới được chế biến khay.");
        }

        // SP cân kg được chế biến; tự bật AllowRepack nếu chưa.
        if (!product.AllowRepack)
        {
            product.AllowRepack = true;
            if (!product.HasBatches)
            {
                product.HasBatches = true;
            }
        }

        var existingActiveWeight = await _repository.GetActivePackageWeightAsync(tenantId, stock.Id, ct);
        var rawSellable = BranchStockAvailability.GetRawSellableKg(stock, existingActiveWeight);
        if (request.InputWeight > rawSellable + 0.0005m)
        {
            throw new BusinessException(
                "REPACK_INSUFFICIENT_RAW",
                $"Khối lượng lấy ra vượt nguyên quả còn bán được ({rawSellable:0.###} kg).");
        }

        var processedPerKg = ResolveProcessedPricePerKg(product, stock);
        if (processedPerKg <= 0)
        {
            throw new BusinessException(
                "PROCESSED_PRICE_MISSING",
                "Chưa cấu hình giá bán theo khay (đ/kg). Cập nhật giá đóng khay trên sản phẩm master trước.");
        }

        foreach (var line in request.Packages)
        {
            if (line.NetWeight <= 0)
            {
                throw new BusinessException("PACKAGE_WEIGHT_INVALID", "Mỗi khay phải có khối lượng > 0.");
            }

            if (line.ExpiryDate.HasValue && stock.ExpiryDate.HasValue
                && line.ExpiryDate.Value.Date > stock.ExpiryDate.Value.Date)
            {
                throw new BusinessException("PACKAGE_EXPIRY_INVALID", "Hạn dùng khay không được vượt hạn dùng của lô.");
            }

            var mfg = (line.ManufactureDate ?? DateTime.UtcNow).Date;
            if (line.ExpiryDate.HasValue && mfg > line.ExpiryDate.Value.Date)
            {
                throw new BusinessException(
                    "PACKAGE_MFG_INVALID",
                    "Ngày sản xuất khay không được sau hạn dùng.");
            }

            if (!string.IsNullOrWhiteSpace(line.Barcode)
                && await _repository.IsCodeConflictAsync(tenantId, null, line.Barcode, ct))
            {
                throw new BusinessException("PACKAGE_BARCODE_CONFLICT", $"Mã tem '{line.Barcode}' bị trùng.");
            }
        }

        var now = DateTime.UtcNow;
        var orderSeq = await _repository.CountRepackOrdersTodayAsync(tenantId, now, ct) + 1;
        var pkgSeqBase = await _repository.CountPackagesCreatedTodayAsync(tenantId, now, ct);

        var unitCost = stock.LandedCostPerUnit;
        var wasteCost = Math.Round(request.WasteWeight * unitCost, 2, MidpointRounding.AwayFromZero);
        var defaultPerKg = processedPerKg;

        var order = new RepackOrder
        {
            TenantId = tenantId,
            Code = $"RP-{now:yyyyMMdd}-{orderSeq:D3}",
            BranchId = stock.BranchId,
            ProductId = stock.ProductId,
            BranchStockId = stock.Id,
            RepackType = repackType,
            InputWeight = request.InputWeight,
            WasteWeight = request.WasteWeight,
            WasteReason = string.IsNullOrWhiteSpace(request.WasteReason)
                ? null
                : request.WasteReason.Trim().ToUpperInvariant(),
            WasteNote = request.WasteNote?.Trim(),
            UnitCostSnapshot = unitCost,
            WasteCostAmount = wasteCost,
            DefaultUnitPricePerKg = defaultPerKg,
            Status = "COMPLETED",
            CompletedAt = now,
            CreatedBy = null
        };

        var createdPackages = new List<Package>();
        var idx = 0;
        foreach (var line in request.Packages)
        {
            idx++;
            var internalCode = $"PKG-{now:yyMMdd}-{(pkgSeqBase + idx):D5}";
            if (await _repository.IsCodeConflictAsync(tenantId, internalCode, null, ct))
            {
                internalCode = $"PKG-{now:yyMMdd}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
            }

            // Giá bán khay = NetWeight(kg) × giá đóng khay đ/kg đã cấu hình (không nhận giá tay từ client).
            var autoPrice = WeightProductHelper.RoundPackagePrice(line.NetWeight * processedPerKg);
            if (autoPrice <= 0)
            {
                throw new BusinessException("PACKAGE_PRICE_INVALID", "Giá bán khay tính được phải > 0.");
            }

            createdPackages.Add(new Package
            {
                TenantId = tenantId,
                BranchId = stock.BranchId,
                ProductId = stock.ProductId,
                BranchStockId = stock.Id,
                InternalCode = internalCode,
                Name = BuildPackageName(product.Name, line.NetWeight),
                Barcode = string.IsNullOrWhiteSpace(line.Barcode) ? null : line.Barcode.Trim(),
                NetWeight = line.NetWeight,
                SellingPrice = autoPrice,
                ManufactureDate = (line.ManufactureDate ?? now).Date,
                ExpiryDate = line.ExpiryDate ?? stock.ExpiryDate,
                RepackType = repackType,
                RepackOrderId = order.Id,
                Status = "AVAILABLE"
            });
        }

        // Hao hụt trừ QOH; kg trong khay vẫn nằm trong QOH (chiếm ActivePackageWeight).
        stock.QuantityOnHand -= request.WasteWeight;
        var newActive = existingActiveWeight + packageWeight;
        if (!BranchStockAvailability.MeetsPackageInvariant(stock.QuantityOnHand, newActive))
        {
            throw new BusinessException(
                "PACKAGE_INVARIANT",
                "Sau chế biến, tồn không đủ để phủ khối lượng các khay. Kiểm tra lại số liệu.");
        }

        await _repository.AddRepackOrderAsync(order, ct);
        await _repository.AddPackagesAsync(createdPackages, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        foreach (var pkg in createdPackages)
        {
            pkg.RepackOrder = order;
        }

        return new RepackOrderDto(
            Id: order.Id,
            Code: order.Code,
            BranchId: order.BranchId,
            ProductId: order.ProductId,
            BranchStockId: order.BranchStockId,
            RepackType: order.RepackType,
            InputWeight: order.InputWeight,
            WasteWeight: order.WasteWeight,
            WasteReason: order.WasteReason,
            WasteNote: order.WasteNote,
            UnitCostSnapshot: order.UnitCostSnapshot,
            WasteCostAmount: order.WasteCostAmount,
            DefaultUnitPricePerKg: order.DefaultUnitPricePerKg,
            Status: order.Status,
            CompletedAt: order.CompletedAt,
            Packages: createdPackages.Select(MapPackage).ToList()
        );
    }

    public async Task<PackageDto> UpdatePackagePriceAsync(
        Guid packageId,
        UpdatePackagePriceRequest request,
        CancellationToken ct = default)
    {
        var tenantId = RequireTenant();
        var package = await _repository.GetPackageByIdAsync(tenantId, packageId, ct)
            ?? throw new BusinessException("PACKAGE_NOT_FOUND", "Không tìm thấy khay.");
        BranchScopeGuard.EnsureCanAccessBranch(_tenantContext, package.BranchId);

        if (package.Status is not ("AVAILABLE" or "QUARANTINED"))
        {
            throw new BusinessException("PACKAGE_NOT_EDITABLE", "Chỉ sửa giá khay chưa bán.");
        }

        if (request.SellingPrice <= 0)
        {
            throw new BusinessException("PACKAGE_PRICE_INVALID", "Giá bán phải > 0.");
        }

        var rounded = WeightProductHelper.RoundPackagePrice(request.SellingPrice);
        var history = new List<object>();
        if (!string.IsNullOrWhiteSpace(package.PriceHistoryJson))
        {
            try
            {
                history = JsonSerializer.Deserialize<List<object>>(package.PriceHistoryJson) ?? history;
            }
            catch
            {
                // ignore bad history
            }
        }

        history.Add(new { at = DateTime.UtcNow, from = package.SellingPrice, to = rounded });
        package.PriceHistoryJson = JsonSerializer.Serialize(history);
        package.SellingPrice = rounded;
        await _unitOfWork.SaveChangesAsync(ct);
        return MapPackage(package);
    }

    public async Task<PackageDto> QuarantinePackageAsync(Guid packageId, CancellationToken ct = default)
    {
        var tenantId = RequireTenant();
        var package = await _repository.GetPackageByIdAsync(tenantId, packageId, ct)
            ?? throw new BusinessException("PACKAGE_NOT_FOUND", "Không tìm thấy khay.");
        BranchScopeGuard.EnsureCanAccessBranch(_tenantContext, package.BranchId);

        if (package.Status != "AVAILABLE")
        {
            throw new BusinessException("PACKAGE_STATUS_INVALID", "Chỉ cách ly khay đang bán được.");
        }

        package.Status = "QUARANTINED";
        await _unitOfWork.SaveChangesAsync(ct);
        return MapPackage(package);
    }

    public async Task<PackageDto> ReleasePackageAsync(Guid packageId, CancellationToken ct = default)
    {
        var tenantId = RequireTenant();
        var package = await _repository.GetPackageByIdAsync(tenantId, packageId, ct)
            ?? throw new BusinessException("PACKAGE_NOT_FOUND", "Không tìm thấy khay.");
        BranchScopeGuard.EnsureCanAccessBranch(_tenantContext, package.BranchId);

        if (package.Status != "QUARANTINED")
        {
            throw new BusinessException("PACKAGE_STATUS_INVALID", "Khay không đang cách ly.");
        }

        package.Status = "AVAILABLE";
        await _unitOfWork.SaveChangesAsync(ct);
        return MapPackage(package);
    }

    public async Task<PackageDto> VoidPackageAsync(
        Guid packageId,
        VoidPackageRequest request,
        CancellationToken ct = default)
    {
        var tenantId = RequireTenant();
        var package = await _repository.GetPackageByIdAsync(tenantId, packageId, ct)
            ?? throw new BusinessException("PACKAGE_NOT_FOUND", "Không tìm thấy khay.");
        BranchScopeGuard.EnsureCanAccessBranch(_tenantContext, package.BranchId);

        if (package.Status is not ("AVAILABLE" or "QUARANTINED"))
        {
            throw new BusinessException("PACKAGE_STATUS_INVALID", "Chỉ hủy khay chưa bán.");
        }

        var mode = (request.Mode ?? string.Empty).Trim().ToUpperInvariant();
        if (mode is not ("UNPACK_TO_RAW" or "WRITE_OFF"))
        {
            throw new BusinessException("VOID_MODE_INVALID", "Chế độ hủy phải là UNPACK_TO_RAW hoặc WRITE_OFF.");
        }

        if (package.RepackType == "PROCESSING" && mode == "UNPACK_TO_RAW")
        {
            throw new BusinessException(
                "PROCESSING_NO_UNPACK",
                "Khay đã sơ chế không được trả về nguyên quả — chỉ ghi hủy.");
        }

        var stock = await _repository.GetBranchStockAsync(tenantId, package.BranchStockId, ct)
            ?? throw new BusinessException("BRANCH_STOCK_NOT_FOUND", "Không tìm thấy lô tồn kho.");

        if (mode == "WRITE_OFF")
        {
            stock.QuantityOnHand -= package.NetWeight;
            var remainingActive = await _repository.GetActivePackageWeightAsync(tenantId, stock.Id, ct) - package.NetWeight;
            if (!BranchStockAvailability.MeetsPackageInvariant(stock.QuantityOnHand, Math.Max(0m, remainingActive)))
            {
                throw new BusinessException("PACKAGE_INVARIANT", "Ghi hủy làm tồn không đủ phủ các khay còn lại.");
            }
        }
        // UNPACK_TO_RAW: QOH giữ nguyên, chỉ bỏ khay khỏi ActivePackageWeight → nguyên quả tăng.

        package.Status = "VOID";
        package.VoidMode = mode;
        package.VoidReason = request.Reason?.Trim();
        await _unitOfWork.SaveChangesAsync(ct);
        return MapPackage(package);
    }

    private string RequireTenant()
    {
        var tenantId = _tenantContext.CurrentTenantId ?? string.Empty;
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new BusinessException("TENANT_REQUIRED", "Thiếu thông tin cửa hàng.");
        }

        return tenantId;
    }

    /// <summary>Tên khay tự gen: "{Tên SP} {khối lượng} KG|G".</summary>
    private static string BuildPackageName(string? productName, decimal netWeightKg)
    {
        var baseName = string.IsNullOrWhiteSpace(productName) ? "Khay" : productName.Trim();
        string weightPart;
        if (netWeightKg > 0m && netWeightKg < 1m)
        {
            var grams = Math.Round(netWeightKg * 1000m, 0, MidpointRounding.AwayFromZero);
            weightPart = $"{grams:0} G";
        }
        else
        {
            weightPart = $"{netWeightKg.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)} KG";
        }

        var full = $"{baseName} {weightPart}";
        return full.Length <= 200 ? full : full[..200];
    }

    private static PackageDto MapPackage(Package p) => new(
        Id: p.Id,
        BranchStockId: p.BranchStockId,
        ProductId: p.ProductId,
        BranchId: p.BranchId,
        InternalCode: p.InternalCode,
        Name: p.Name,
        Barcode: p.Barcode,
        NetWeight: p.NetWeight,
        SellingPrice: p.SellingPrice,
        ManufactureDate: p.ManufactureDate ?? p.CreatedAt.Date,
        ExpiryDate: p.ExpiryDate,
        RepackType: p.RepackType,
        Status: p.Status,
        RepackOrderId: p.RepackOrderId,
        RepackOrderCode: p.RepackOrder?.Code,
        SoldAt: p.SoldAt,
        CreatedAt: p.CreatedAt
    );

    /// <summary>Giá/kg đóng khay: PROCESSED nếu có, không thì RAW (SellingPrice / RetailPrice).</summary>
    private static decimal ResolveProcessedPricePerKg(Domain.Entities.Catalog.Product product, BranchStock stock)
    {
        if (product.ProcessedSellingPricePerKg is > 0)
        {
            return product.ProcessedSellingPricePerKg.Value;
        }

        if (stock.RetailPrice is > 0)
        {
            return stock.RetailPrice.Value;
        }

        return product.SellingPrice;
    }
}
