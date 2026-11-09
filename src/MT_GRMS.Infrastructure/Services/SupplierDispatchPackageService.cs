using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Common.Interfaces;
using MT_GRMS.Application.PurchaseOrders.Dispatch;
using MT_GRMS.Application.PurchaseOrders.Dtos;
using MT_GRMS.Domain.Entities.Supplier;
using MT_GRMS.Domain.Entities.System;
using MT_GRMS.Domain.Exceptions;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.Infrastructure.Services;

public class SupplierDispatchPackageService : ISupplierDispatchPackageService
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IFileStorage _fileStorage;
    private readonly ISupplierOrderExcelGenerator _excelGenerator;
    private readonly ISupplierOrderEmailSender _emailSender;
    private readonly ILogger<SupplierDispatchPackageService> _logger;

    public SupplierDispatchPackageService(
        AppDbContext db,
        ITenantContext tenantContext,
        IFileStorage fileStorage,
        ISupplierOrderExcelGenerator excelGenerator,
        ISupplierOrderEmailSender emailSender,
        ILogger<SupplierDispatchPackageService> logger)
    {
        _db = db;
        _tenantContext = tenantContext;
        _fileStorage = fileStorage;
        _excelGenerator = excelGenerator;
        _emailSender = emailSender;
        _logger = logger;
    }

    public async Task<SupplierDispatchPackageDto?> CreatePackageForDispatchAsync(
        SplitMatrixPoCommand command,
        SplitMatrixBatchResultDto batchResult,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.EmailSendMode)) return null;
        // Cho phép tạo package khi tạo mới / amend / no-changes (gửi lại email cùng PO).
        if ((batchResult.SuccessCount <= 0
                && batchResult.AmendedCount <= 0
                && batchResult.NoChangesCount <= 0)
            || batchResult.CreatedPurchaseOrders.Count == 0)
        {
            return null;
        }

        var mode = command.EmailSendMode.Trim().ToUpperInvariant();
        if (mode is not (EmailSendModes.SendNow or EmailSendModes.ScheduledSend))
        {
            throw new BusinessException("EMAIL_MODE_INVALID", "EmailSendMode phải là SEND_NOW hoặc SCHEDULED_SEND.");
        }

        var supplierId = command.SupplierId
            ?? batchResult.CreatedPurchaseOrders.FirstOrDefault()?.SupplierId
            ?? Guid.Empty;
        if (supplierId == Guid.Empty)
        {
            throw new BusinessException("SUPPLIER_REQUIRED", "Thiếu SupplierId để tạo package.");
        }

        var supplier = await _db.Suppliers.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == supplierId && !s.IsDeleted, cancellationToken);
        if (supplier == null)
        {
            throw new NotFoundException("Nhà cung cấp", supplierId);
        }

        if (string.IsNullOrWhiteSpace(supplier.Email) || !EmailCcHelper.IsValidEmail(supplier.Email))
        {
            throw new BusinessException("SUPPLIER_EMAIL_INVALID", "NCC chưa có email hợp lệ để gửi đơn hàng.");
        }

        var cc = EmailCcHelper.NormalizeCc(command.CcEmails, supplier.Email);
        DateTime? scheduledUtc = null;
        if (mode == EmailSendModes.ScheduledSend)
        {
            if (!command.ScheduledSendAt.HasValue)
            {
                throw new BusinessException("SCHEDULE_REQUIRED", "Thiếu thời điểm hẹn giờ gửi email.");
            }

            // Interpret as VN local if Kind unspecified
            var local = command.ScheduledSendAt.Value;
            if (local.Kind == DateTimeKind.Unspecified)
            {
                scheduledUtc = DateTime.SpecifyKind(local, DateTimeKind.Unspecified).AddHours(-7);
            }
            else
            {
                scheduledUtc = local.ToUniversalTime();
            }

            if (scheduledUtc <= DateTime.UtcNow.AddMinutes(-1))
            {
                throw new BusinessException("SCHEDULE_PAST", "Thời điểm hẹn giờ phải ở tương lai (Asia/Ho_Chi_Minh).");
            }
        }

        var dispatchBatchId = Guid.NewGuid();
        var poIds = batchResult.CreatedPurchaseOrders.Select(p => p.Id).Distinct().ToList();
        var pos = await _db.PurchaseOrders
            .Where(p => poIds.Contains(p.Id) && !p.IsDeleted)
            .ToListAsync(cancellationToken);

        var maxPackageRev = await _db.SupplierDispatchPackages
            .Where(p => p.SupplierId == supplierId && !p.IsDeleted)
            .Select(p => (int?)p.PackageRevisionNumber)
            .MaxAsync(cancellationToken) ?? 0;
        var nextPackageRev = Math.Max(1, maxPackageRev + 1);

        // Soft-delete package items cũ trước, rồi SaveChanges riêng —
        // Postgres check unique partial index ngay (không deferred); insert cùng batch dễ 409.
        var oldItems = await _db.SupplierDispatchPackageItems
            .Where(i => poIds.Contains(i.PurchaseOrderId) && !i.IsDeleted)
            .ToListAsync(cancellationToken);
        if (oldItems.Count > 0)
        {
            foreach (var item in oldItems)
            {
                item.IsDeleted = true;
            }

            await _db.SaveChangesAsync(cancellationToken);
        }

        // Supersede package chưa gửi / chưa xong của cùng NCC (tránh worker gửi revision cũ).
        var pendingPkgs = await _db.SupplierDispatchPackages
            .Where(p => p.SupplierId == supplierId
                        && !p.IsDeleted
                        && p.EmailStatus != PackageEmailStatuses.Sent
                        && p.EmailStatus != PackageEmailStatuses.Sending
                        && p.EmailStatus != PackageEmailStatuses.Cancelled
                        && p.EmailStatus != PackageEmailStatuses.Superseded)
            .ToListAsync(cancellationToken);

        var package = new SupplierDispatchPackage
        {
            TenantId = _tenantContext.CurrentTenantId,
            DispatchBatchId = dispatchBatchId,
            SupplierId = supplierId,
            SupplierCodeSnapshot = supplier.Code,
            SupplierNameSnapshot = supplier.Name,
            EmailTo = supplier.Email!.Trim(),
            EmailCcJson = EmailCcHelper.ToJson(cc),
            EmailSendMode = mode,
            ScheduledSendAtUtc = scheduledUtc,
            EmailStatus = mode == EmailSendModes.ScheduledSend
                ? PackageEmailStatuses.Scheduled
                : PackageEmailStatuses.Pending,
            GenerationStatus = PackageGenerationStatuses.Pending,
            PackageRevisionNumber = nextPackageRev,
            Note = command.Note,
            IdempotencyKey = $"pkg-{dispatchBatchId:N}-{supplierId:N}-r{nextPackageRev}"
        };

        foreach (var old in pendingPkgs)
        {
            old.EmailStatus = PackageEmailStatuses.Superseded;
            old.SupersededByPackageId = package.Id;
            old.LastError = $"Superseded by package revision {nextPackageRev}.";
        }

        foreach (var po in pos)
        {
            po.DispatchPackageId = package.Id;
            po.DispatchBatchId = dispatchBatchId;
            package.Items.Add(new SupplierDispatchPackageItem
            {
                TenantId = _tenantContext.CurrentTenantId,
                PackageId = package.Id,
                PurchaseOrderId = po.Id
            });
        }

        _db.SupplierDispatchPackages.Add(package);
        _db.OutboxMessages.Add(new OutboxMessage
        {
            TenantId = _tenantContext.CurrentTenantId,
            Type = OutboxMessageTypes.SupplierOrderPackageProcess,
            PayloadJson = JsonSerializer.Serialize(new { PackageId = package.Id }),
            IdempotencyKey = $"outbox-pkg-{package.Id:N}",
            Status = OutboxStatuses.Pending,
            NextAttemptAtUtc = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(cancellationToken);
        return MapDto(package, pos.Select(p => p.Code).ToList());
    }

    public async Task<SupplierDispatchPackageDto?> GetByIdAsync(Guid packageId, CancellationToken cancellationToken = default)
    {
        var pkg = await _db.SupplierDispatchPackages
            .Include(p => p.Items)
            .ThenInclude(i => i.PurchaseOrder)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == packageId && !p.IsDeleted, cancellationToken);
        if (pkg == null) return null;

        var codes = pkg.Items
            .Where(i => i.PurchaseOrder != null)
            .Select(i => i.PurchaseOrder!.Code)
            .ToList();
        return MapDto(pkg, codes);
    }

    public async Task<IReadOnlyList<SupplierDispatchPackageDto>> ListAsync(
        Guid? supplierId,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        var q = _db.SupplierDispatchPackages.AsNoTracking()
            .Include(p => p.Items).ThenInclude(i => i.PurchaseOrder)
            .Where(p => !p.IsDeleted);
        if (supplierId.HasValue) q = q.Where(p => p.SupplierId == supplierId.Value);

        var list = await q.OrderByDescending(p => p.CreatedAt).Take(Math.Clamp(take, 1, 200)).ToListAsync(cancellationToken);
        return list.Select(p => MapDto(
            p,
            p.Items.Where(i => i.PurchaseOrder != null).Select(i => i.PurchaseOrder!.Code).ToList())).ToList();
    }

    public async Task RetryGenerationAsync(Guid packageId, CancellationToken cancellationToken = default)
    {
        var pkg = await RequirePackageAsync(packageId, cancellationToken);
        if (pkg.EmailStatus is PackageEmailStatuses.Sending or PackageEmailStatuses.Sent)
        {
            throw new BusinessException("PACKAGE_LOCKED", "Không thể tạo lại file khi email đang/đã gửi.");
        }

        pkg.GenerationStatus = PackageGenerationStatuses.Pending;
        pkg.FileName = null;
        pkg.FileStorageKey = null;
        pkg.FileSize = null;
        pkg.FileChecksum = null;
        pkg.GeneratedAtUtc = null;
        pkg.LastError = null;
        EnqueueProcess(pkg.Id);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task RetryEmailAsync(Guid packageId, CancellationToken cancellationToken = default)
    {
        var pkg = await RequirePackageAsync(packageId, cancellationToken);
        if (pkg.EmailStatus == PackageEmailStatuses.Superseded)
        {
            throw new BusinessException("PACKAGE_SUPERSEDED", "Package đã bị thay thế bởi revision mới hơn — không gửi lại.");
        }
        if (pkg.GenerationStatus != PackageGenerationStatuses.Generated || string.IsNullOrWhiteSpace(pkg.FileStorageKey))
        {
            throw new BusinessException("FILE_REQUIRED", "Chưa có file Excel hợp lệ để gửi lại email.");
        }

        if (pkg.EmailStatus == PackageEmailStatuses.Sending)
        {
            throw new BusinessException("EMAIL_SENDING", "Email đang được gửi, vui lòng đợi.");
        }

        if (pkg.EmailStatus == PackageEmailStatuses.Cancelled)
        {
            pkg.EmailStatus = pkg.EmailSendMode == EmailSendModes.ScheduledSend
                && pkg.ScheduledSendAtUtc.HasValue
                && pkg.ScheduledSendAtUtc > DateTime.UtcNow
                    ? PackageEmailStatuses.Scheduled
                    : PackageEmailStatuses.Pending;
        }
        else if (pkg.EmailStatus is PackageEmailStatuses.Failed or PackageEmailStatuses.Sent)
        {
            pkg.EmailStatus = PackageEmailStatuses.Pending;
        }

        pkg.LastError = null;
        EnqueueProcess(pkg.Id);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task CancelScheduledAsync(Guid packageId, CancellationToken cancellationToken = default)
    {
        var pkg = await RequirePackageAsync(packageId, cancellationToken);
        if (pkg.EmailStatus is PackageEmailStatuses.Sending or PackageEmailStatuses.Sent)
        {
            throw new BusinessException("CANCEL_FORBIDDEN", "Không thể hủy khi email đang gửi hoặc đã gửi.");
        }

        if (pkg.EmailStatus != PackageEmailStatuses.Scheduled && pkg.EmailSendMode != EmailSendModes.ScheduledSend)
        {
            throw new BusinessException("NOT_SCHEDULED", "Package không ở trạng thái lịch gửi.");
        }

        pkg.EmailStatus = PackageEmailStatuses.Cancelled;
        pkg.LastError = null;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<(byte[] Content, string FileName, string ContentType)> DownloadAsync(
        Guid packageId,
        CancellationToken cancellationToken = default)
    {
        var pkg = await RequirePackageAsync(packageId, cancellationToken);
        if (pkg.GenerationStatus != PackageGenerationStatuses.Generated || string.IsNullOrWhiteSpace(pkg.FileStorageKey))
        {
            throw new BusinessException("FILE_NOT_READY", "File Excel chưa sẵn sàng để tải.");
        }

        await using var stream = await _fileStorage.OpenReadAsync(pkg.FileStorageKey, cancellationToken);
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, cancellationToken);
        return (
            ms.ToArray(),
            pkg.FileName ?? $"PO_{pkg.SupplierCodeSnapshot}.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
    }

    public async Task ProcessPackageAsync(Guid packageId, CancellationToken cancellationToken = default)
    {
        var pkg = await _db.SupplierDispatchPackages
            .Include(p => p.Items)
            .FirstOrDefaultAsync(p => p.Id == packageId && !p.IsDeleted, cancellationToken);
        if (pkg == null) return;
        if (pkg.EmailStatus == PackageEmailStatuses.Superseded) return;

        // Claim lease
        var now = DateTime.UtcNow;
        if (pkg.ProcessingLeaseUntilUtc.HasValue && pkg.ProcessingLeaseUntilUtc > now)
        {
            return;
        }

        pkg.ProcessingLeaseUntilUtc = now.AddMinutes(5);
        pkg.ProcessingLockedBy = Environment.MachineName;
        await _db.SaveChangesAsync(cancellationToken);

        try
        {
            await _db.Entry(pkg).ReloadAsync(cancellationToken);
            if (pkg.EmailStatus == PackageEmailStatuses.Superseded) return;

            // GENERATING = stuck mid-run (e.g. previous crash) — treat as retryable.
            if (pkg.GenerationStatus is PackageGenerationStatuses.Pending
                or PackageGenerationStatuses.Failed
                or PackageGenerationStatuses.Generating)
            {
                await GenerateExcelInternalAsync(pkg, cancellationToken);
            }

            await _db.Entry(pkg).ReloadAsync(cancellationToken);

            if (pkg.EmailStatus is PackageEmailStatuses.Cancelled or PackageEmailStatuses.Superseded) return;
            if (pkg.GenerationStatus != PackageGenerationStatuses.Generated) return;

            if (pkg.EmailStatus == PackageEmailStatuses.Scheduled)
            {
                if (!pkg.ScheduledSendAtUtc.HasValue || pkg.ScheduledSendAtUtc > DateTime.UtcNow)
                {
                    return;
                }
            }

            if (pkg.EmailStatus is PackageEmailStatuses.Pending or PackageEmailStatuses.Scheduled or PackageEmailStatuses.Failed)
            {
                await SendEmailInternalAsync(pkg, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ProcessPackage failed {PackageId}", packageId);
            pkg.LastError = Truncate(ex.Message, 1900);
            pkg.AttemptCount += 1;
            await _db.SaveChangesAsync(cancellationToken);
            throw;
        }
        finally
        {
            pkg.ProcessingLeaseUntilUtc = null;
            pkg.ProcessingLockedBy = null;
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task GenerateExcelInternalAsync(SupplierDispatchPackage pkg, CancellationToken ct)
    {
        pkg.GenerationStatus = PackageGenerationStatuses.Generating;
        pkg.AttemptCount += 1;
        await _db.SaveChangesAsync(ct);

        var poIds = pkg.Items.Select(i => i.PurchaseOrderId).ToList();
        var pos = await _db.PurchaseOrders
            .Include(p => p.Details)
            .Where(p => poIds.Contains(p.Id) && !p.IsDeleted)
            .ToListAsync(ct);

        if (pos.Count == 0)
        {
            pkg.GenerationStatus = PackageGenerationStatuses.Failed;
            pkg.LastError = "Package không có PO để xuất Excel.";
            await _db.SaveChangesAsync(ct);
            return;
        }

        // Chỉ xuất PO đã có snapshot tối thiểu
        var missingSnapshot = pos.Any(p =>
            string.IsNullOrWhiteSpace(p.SupplierCodeSnapshot)
            || string.IsNullOrWhiteSpace(p.StoreCodeSnapshot)
            || p.Details.Any(d => string.IsNullOrWhiteSpace(d.ProductCodeSnapshot)));
        if (missingSnapshot)
        {
            pkg.GenerationStatus = PackageGenerationStatuses.Failed;
            pkg.LastError = "PO thiếu snapshot lịch sử — không xuất Excel.";
            await _db.SaveChangesAsync(ct);
            return;
        }

        var bytes = await _excelGenerator.GenerateAsync(pkg, pos, ct);
        var datePart = DateTime.UtcNow.AddHours(7).ToString("yyyyMMdd");
        var safeCode = SanitizeFilePart(pkg.SupplierCodeSnapshot);
        var rev = Math.Max(1, pkg.PackageRevisionNumber);
        var fileName = $"PO_{safeCode}_{datePart}_R{rev}_{pkg.DispatchBatchId:N}.xlsx";
        var storageKey =
            $"supplier-orders/{pkg.TenantId}/{pkg.DispatchBatchId:N}/{pkg.SupplierId:N}/{fileName}";

        await using var ms = new MemoryStream(bytes);
        var stored = await _fileStorage.SaveAsync(
            storageKey,
            ms,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ct);

        pkg.FileName = fileName;
        pkg.FileStorageKey = stored.StorageKey;
        pkg.FileSize = stored.SizeBytes;
        pkg.FileChecksum = stored.Sha256Hex;
        pkg.GeneratedAtUtc = DateTime.UtcNow;
        pkg.GenerationStatus = PackageGenerationStatuses.Generated;
        pkg.LastError = null;
        await _db.SaveChangesAsync(ct);
    }

    private async Task SendEmailInternalAsync(SupplierDispatchPackage pkg, CancellationToken ct)
    {
        // Re-check cancel / superseded / status right before send
        await _db.Entry(pkg).ReloadAsync(ct);
        if (pkg.EmailStatus is PackageEmailStatuses.Cancelled or PackageEmailStatuses.Superseded) return;
        if (pkg.EmailStatus == PackageEmailStatuses.Sent) return;
        if (pkg.GenerationStatus != PackageGenerationStatuses.Generated || string.IsNullOrWhiteSpace(pkg.FileStorageKey))
        {
            return;
        }

        if (pkg.EmailSendMode == EmailSendModes.ScheduledSend
            && pkg.ScheduledSendAtUtc.HasValue
            && pkg.ScheduledSendAtUtc > DateTime.UtcNow)
        {
            return;
        }

        pkg.EmailStatus = PackageEmailStatuses.Sending;
        await _db.SaveChangesAsync(ct);

        try
        {
            await using var stream = await _fileStorage.OpenReadAsync(pkg.FileStorageKey!, ct);
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, ct);

            var poIds = pkg.Items.Where(i => !i.IsDeleted).Select(i => i.PurchaseOrderId).ToList();
            var pos = await _db.PurchaseOrders.AsNoTracking()
                .Where(p => poIds.Contains(p.Id))
                .ToListAsync(ct);

            var stores = pos.Select(p => p.StoreNameSnapshot ?? p.StoreCodeSnapshot ?? "")
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct()
                .ToList();
            var dates = pos.Where(p => p.DeliveryDate.HasValue)
                .Select(p => p.DeliveryDate!.Value.ToString("dd/MM/yyyy"))
                .Distinct()
                .OrderBy(x => x)
                .ToList();

            var cc = EmailCcHelper.FromJson(pkg.EmailCcJson);
            var dispatchDate = DateTime.UtcNow.AddHours(7).ToString("dd/MM/yyyy");
            var rev = Math.Max(1, pkg.PackageRevisionNumber);
            var isUpdate = rev > 1;
            var body = new StringBuilder();
            body.AppendLine($"Kính gửi {pkg.SupplierNameSnapshot},");
            body.AppendLine();
            body.AppendLine(isUpdate
                ? "MT-GRMS gửi bản CẬP NHẬT đơn đặt hàng (thay thế bản trước)."
                : "MT-GRMS gửi đơn đặt hàng đính kèm.");
            body.AppendLine($"Mã NCC: {pkg.SupplierCodeSnapshot}");
            body.AppendLine($"Revision package: {rev}");
            body.AppendLine($"Số PO trong file: {pos.Count}");
            body.AppendLine($"Cửa hàng: {string.Join(", ", stores)}");
            body.AppendLine($"Ngày giao: {string.Join(", ", dates)}");
            if (!string.IsNullOrWhiteSpace(pkg.Note))
            {
                body.AppendLine();
                body.AppendLine($"Ghi chú: {pkg.Note}");
            }

            body.AppendLine();
            body.AppendLine(isUpdate
                ? "Vui lòng sử dụng file Excel đính kèm mới nhất và bỏ qua bản cũ."
                : "Vui lòng xem file Excel đính kèm.");
            body.AppendLine("Trân trọng,");
            body.AppendLine("MT-GRMS");

            var subject = isUpdate
                ? $"[UPDATED ORDER] {pkg.SupplierNameSnapshot} - {dispatchDate} - Revision {rev}"
                : $"Đơn đặt hàng từ MT-GRMS - {pkg.SupplierCodeSnapshot} - {dispatchDate}";

            await _emailSender.SendSupplierOrderAsync(new SupplierOrderEmailMessage
            {
                To = pkg.EmailTo,
                Cc = cc,
                Subject = subject,
                BodyText = body.ToString(),
                AttachmentFileName = pkg.FileName ?? "don-hang.xlsx",
                AttachmentBytes = ms.ToArray()
            }, ct);

            await _db.Entry(pkg).ReloadAsync(ct);
            if (pkg.EmailStatus is PackageEmailStatuses.Cancelled or PackageEmailStatuses.Superseded)
            {
                // Race: cancelled/superseded while sending — keep that status, do not mark SENT
                return;
            }

            pkg.EmailStatus = PackageEmailStatuses.Sent;
            pkg.SentAtUtc = DateTime.UtcNow;
            pkg.LastError = null;
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            pkg.EmailStatus = PackageEmailStatuses.Failed;
            pkg.LastError = Truncate(ex.Message, 1900);
            pkg.AttemptCount += 1;
            await _db.SaveChangesAsync(ct);
            throw;
        }
    }

    private void EnqueueProcess(Guid packageId)
    {
        var key = $"outbox-pkg-retry-{packageId:N}-{DateTime.UtcNow.Ticks}";
        _db.OutboxMessages.Add(new OutboxMessage
        {
            TenantId = _tenantContext.CurrentTenantId,
            Type = OutboxMessageTypes.SupplierOrderPackageProcess,
            PayloadJson = JsonSerializer.Serialize(new { PackageId = packageId }),
            IdempotencyKey = key,
            Status = OutboxStatuses.Pending,
            NextAttemptAtUtc = DateTime.UtcNow
        });
    }

    private async Task<SupplierDispatchPackage> RequirePackageAsync(Guid packageId, CancellationToken ct)
    {
        var pkg = await _db.SupplierDispatchPackages
            .Include(p => p.Items)
            .FirstOrDefaultAsync(p => p.Id == packageId && !p.IsDeleted, ct);
        if (pkg == null) throw new NotFoundException("Dispatch package", packageId);
        return pkg;
    }

    private static SupplierDispatchPackageDto MapDto(SupplierDispatchPackage pkg, IReadOnlyList<string> codes)
    {
        return new SupplierDispatchPackageDto
        {
            Id = pkg.Id,
            DispatchBatchId = pkg.DispatchBatchId,
            SupplierId = pkg.SupplierId,
            SupplierCode = pkg.SupplierCodeSnapshot,
            SupplierName = pkg.SupplierNameSnapshot,
            EmailTo = pkg.EmailTo,
            EmailCc = EmailCcHelper.FromJson(pkg.EmailCcJson),
            EmailSendMode = pkg.EmailSendMode,
            ScheduledSendAtUtc = pkg.ScheduledSendAtUtc,
            EmailStatus = pkg.EmailStatus,
            GenerationStatus = pkg.GenerationStatus,
            PackageRevisionNumber = pkg.PackageRevisionNumber <= 0 ? 1 : pkg.PackageRevisionNumber,
            FileName = pkg.FileName,
            FileSize = pkg.FileSize,
            AttemptCount = pkg.AttemptCount,
            LastError = pkg.LastError,
            GeneratedAtUtc = pkg.GeneratedAtUtc,
            SentAtUtc = pkg.SentAtUtc,
            CreatedAt = pkg.CreatedAt,
            PurchaseOrderCount = codes.Count,
            PurchaseOrderCodes = codes,
            CanDownload = pkg.GenerationStatus == PackageGenerationStatuses.Generated && !string.IsNullOrWhiteSpace(pkg.FileStorageKey),
            CanCancelSchedule = pkg.EmailStatus == PackageEmailStatuses.Scheduled,
            CanRetryGeneration = pkg.GenerationStatus == PackageGenerationStatuses.Failed
                && pkg.EmailStatus is not (PackageEmailStatuses.Sending or PackageEmailStatuses.Sent),
            CanRetryEmail = pkg.GenerationStatus == PackageGenerationStatuses.Generated
                && pkg.EmailStatus is PackageEmailStatuses.Failed or PackageEmailStatuses.Cancelled or PackageEmailStatuses.Sent
        };
    }

    private static string SanitizeFilePart(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "SUP" : cleaned.Trim();
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
