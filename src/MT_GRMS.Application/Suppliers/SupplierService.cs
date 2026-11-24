using MT_GRMS.Application.Common;
using MT_GRMS.Application.Suppliers.Dtos;
using MT_GRMS.Application.Suppliers.Validators;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Entities.Supplier;

using MT_GRMS.Domain.Enums;
using MT_GRMS.Domain.Exceptions;

namespace MT_GRMS.Application.Suppliers;

public class SupplierService : ISupplierService
{
    private readonly ISupplierRepository _supplierRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITenantContext _tenantContext;

    public SupplierService(
        ISupplierRepository supplierRepository,
        IUnitOfWork unitOfWork,
        ITenantContext tenantContext)
    {
        _supplierRepository = supplierRepository;
        _unitOfWork = unitOfWork;
        _tenantContext = tenantContext;
    }

    public async Task<PagedResult<SupplierSummaryDto>> GetSuppliersAsync(SupplierQueryFilter filter, CancellationToken cancellationToken = default)
    {
        var (items, totalCount) = await _supplierRepository.GetPagedListAsync(
            filter.SearchTerm,
            filter.SupplierType,
            filter.Status,
            filter.CategoryId,
            filter.PageIndex,
            filter.PageSize,
            filter.Code,
            filter.Name,
            filter.ContactPerson,
            filter.Phone,
            filter.SupplierIds,
            filter.ProductIds,
            filter.ContactPersons,
            filter.Phones,
            filter.Codes,
            filter.Names,
            filter.Category,
            cancellationToken);

        var canSeeDebt = SupplierAccess.IsStoreOwner(_tenantContext);
        var dtos = items.Select(s => new SupplierSummaryDto
        {
            Id = s.Id,
            Code = s.Code,
            Name = s.Name,
            SupplierType = s.SupplierType,
            Phone = s.Phone,
            ContactPerson = s.ContactPerson,
            TaxCode = s.TaxCode,
            Email = s.Email,
            Status = s.Status,
            PaymentTermDays = s.PaymentTermDays,
            CreditLimit = s.CreditLimit,
            CurrentDebt = canSeeDebt ? s.CurrentDebt : 0,
            OverdueDebt = canSeeDebt ? s.OverdueDebt : 0,
            LinkedCustomerId = Guid.TryParse(s.LinkedCustomerId, out var lcid) ? lcid : null,
            Address = s.Address,
            Notes = s.Notes,
            CompanyName = s.CompanyName,
            InvoiceAddress = s.InvoiceAddress,
            BankName = s.BankName,
            BankAccountNumber = s.BankAccount,
            BankAccountHolder = s.AccountHolder,
            CreatedAt = s.CreatedAt
        }).ToList();

        return new PagedResult<SupplierSummaryDto>
        {
            Items = dtos,
            Page = filter.PageIndex,
            PageSize = filter.PageSize,
            TotalItems = totalCount
        };
    }

    public async Task<SupplierDetailDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var supplier = await _supplierRepository.GetByIdWithDetailsAsync(id, cancellationToken);
        if (supplier == null) return null;
        if (!SupplierAccess.CanViewSupplier(_tenantContext, supplier)) return null;

        var dto = MapToDetailDto(supplier);
        if (!SupplierAccess.IsStoreOwner(_tenantContext))
        {
            dto.CurrentDebt = 0;
            dto.OverdueDebt = 0;
        }

        // Tính toán EarliestDeliveryDate nếu có DeliveryConfig
        if (dto.DeliveryConfig != null)
        {
            dto.DeliveryConfig.EarliestDeliveryDate = DeliveryScheduleCalculator.CalculateEarliestDeliveryDate(
                DateTime.UtcNow,
                dto.DeliveryConfig.CutoffTime,
                dto.DeliveryConfig.DeliveryWeekdaysMask,
                supplier.BlackoutDates);
        }

        return dto;
    }

    public async Task<SupplierDetailDto> CreateSupplierAsync(CreateSupplierCommand command, CancellationToken cancellationToken = default)
    {
        SupplierAccess.EnsureStoreOwner(_tenantContext);

        await ValidateCommandAsync(new CreateSupplierCommandValidator(), command, cancellationToken);

        // 1. Tự động sinh mã nếu trống (REQ-01)
        var code = command.Code?.Trim();
        if (string.IsNullOrWhiteSpace(code))
        {
            var datePart = DateTime.UtcNow.ToString("yyyyMMdd");
            var countToday = await _supplierRepository.CountTodayAsync(cancellationToken);
            code = $"SUP-{datePart}-{(countToday + 1):D4}";
        }

        // 2. Kiểm tra trùng mã trong cùng Tenant (BR-S01)
        var exists = await _supplierRepository.ExistsByCodeAsync(code, cancellationToken);
        if (exists)
        {
            throw new BusinessException("DUPLICATE_SUPPLIER_CODE", $"Mã nhà cung cấp '{code}' đã tồn tại trong hệ thống.");
        }

        var supplier = new Supplier
        {
            Code = code,
            Name = command.Name.Trim(),
            SupplierType = command.SupplierType,
            TaxCode = command.TaxCode?.Trim(),
            Phone = command.Phone.Trim(),
            Email = command.Email?.Trim(),
            Address = command.Address?.Trim(),
            Ward = command.Ward?.Trim(),
            District = command.District?.Trim(),
            Province = command.Province?.Trim(),
            ContactPerson = command.ContactPerson?.Trim() ?? string.Empty,
            ContactPhone = command.ContactPhone?.Trim(),
            PaymentTermDays = command.PaymentTermDays,
            CreditLimit = command.CreditLimit,
            CurrentDebt = 0,
            Status = command.Status,
            BankName = command.BankName?.Trim(),
            BankAccount = command.BankAccountNumber?.Trim(),
            AccountHolder = command.BankAccountHolder?.Trim(),
            Notes = command.Notes?.Trim(),
            LinkedCustomerId = command.LinkedCustomerId?.ToString()
        };

        // Mặc định cấu hình DeliveryConfig cơ bản trên Supplier
        supplier.CutoffDayOffset = 1;
        supplier.CutoffTime = "17:00";
        supplier.DeliveryWeekdaysMask = 63; // Mon-Sat
        supplier.DeliveryDaysJson = "[\"Mon\",\"Tue\",\"Wed\",\"Thu\",\"Fri\",\"Sat\"]";
        supplier.MinOrderQuantity = 0;
        supplier.AllowUrgent = false;
        supplier.UrgentLeadTimeHours = 4;
        supplier.UrgentFeePercent = 0;

        await _supplierRepository.AddAsync(supplier, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return MapToDetailDto(supplier);
    }

    public async Task<SupplierDetailDto> UpdateSupplierAsync(Guid id, UpdateSupplierCommand command, CancellationToken cancellationToken = default)
    {
        SupplierAccess.EnsureStoreOwner(_tenantContext);

        await ValidateCommandAsync(new UpdateSupplierCommandValidator(), command, cancellationToken);

        var supplier = await _supplierRepository.GetByIdWithDetailsAsync(id, cancellationToken);
        if (supplier == null)
        {
            throw new NotFoundException("Nhà cung cấp", id);
        }

        // BR-S07: Kiểm tra quy tắc thu hẹp SupplierType
        if (supplier.SupplierType != command.SupplierType)
        {
            // Thu hẹp về Goods (bỏ Service)
            if ((supplier.SupplierType == SupplierType.Both || supplier.SupplierType == SupplierType.Service) && command.SupplierType == SupplierType.Goods)
            {
                var hasServiceInvoice = await _supplierRepository.HasLedgerTransactionsAsync(id, APDocType.ServiceInvoice, cancellationToken);
                if (hasServiceInvoice)
                {
                    throw new BusinessException("INVALID_SUPPLIER_TYPE_NARROWING", "BR-S07: Cannot narrow supplier type when historical service transactions exist.");
                }
            }

            // Thu hẹp về Service (bỏ Goods)
            if ((supplier.SupplierType == SupplierType.Both || supplier.SupplierType == SupplierType.Goods) && command.SupplierType == SupplierType.Service)
            {
                var hasPurchaseInvoice = await _supplierRepository.HasLedgerTransactionsAsync(id, APDocType.PurchaseInvoice, cancellationToken);
                if (hasPurchaseInvoice)
                {
                    throw new BusinessException("INVALID_SUPPLIER_TYPE_NARROWING", "BR-S07: Cannot narrow supplier type when historical purchase invoice transactions exist.");
                }
            }
        }

        supplier.Name = command.Name.Trim();
        supplier.SupplierType = command.SupplierType;
        supplier.TaxCode = command.TaxCode?.Trim();
        supplier.Phone = command.Phone.Trim();
        supplier.Email = command.Email?.Trim();
        supplier.Address = command.Address?.Trim();
        supplier.Ward = command.Ward?.Trim();
        supplier.District = command.District?.Trim();
        supplier.Province = command.Province?.Trim();
        supplier.ContactPerson = command.ContactPerson?.Trim() ?? string.Empty;
        supplier.ContactPhone = command.ContactPhone?.Trim();
        supplier.PaymentTermDays = command.PaymentTermDays;
        supplier.CreditLimit = command.CreditLimit;
        supplier.BankName = command.BankName?.Trim();
        supplier.BankAccount = command.BankAccountNumber?.Trim();
        supplier.AccountHolder = command.BankAccountHolder?.Trim();
        supplier.CompanyName = command.CompanyName?.Trim();
        supplier.InvoiceAddress = command.InvoiceAddress?.Trim();
        supplier.Notes = command.Notes?.Trim();
        supplier.LinkedCustomerId = command.LinkedCustomerId?.ToString();
        if (command.Status.HasValue)
        {
            supplier.Status = command.Status.Value;
        }

        _supplierRepository.Update(supplier);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return MapToDetailDto(supplier);
    }

    public async Task<SupplierDetailDto> UpdateBasicSupplierAsync(Guid id, UpdateBasicSupplierCommand command, CancellationToken cancellationToken = default)
    {
        SupplierAccess.EnsureStoreOwner(_tenantContext);

        await ValidateCommandAsync(new UpdateBasicSupplierCommandValidator(), command, cancellationToken);

        var supplier = await _supplierRepository.GetByIdWithDetailsAsync(id, cancellationToken);
        if (supplier == null)
        {
            throw new NotFoundException("Nhà cung cấp", id);
        }

        supplier.Name = command.Name.Trim();
        supplier.ContactPerson = command.ContactPerson?.Trim() ?? string.Empty;
        supplier.Phone = command.Phone.Trim();
        supplier.Status = command.Status;
        if (command.Email != null)
        {
            supplier.Email = string.IsNullOrWhiteSpace(command.Email) ? null : command.Email.Trim();
        }
        if (command.TaxCode != null)
        {
            supplier.TaxCode = command.TaxCode.Trim();
        }
        if (command.Address != null)
        {
            supplier.Address = command.Address.Trim();
        }
        if (command.Notes != null)
        {
            supplier.Notes = command.Notes.Trim();
        }

        _supplierRepository.Update(supplier);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return MapToDetailDto(supplier);
    }

    public async Task<SupplierDetailDto> UpdateBankAsync(Guid id, UpdateSupplierBankCommand command, CancellationToken cancellationToken = default)
    {
        SupplierAccess.EnsureStoreOwner(_tenantContext);

        await ValidateCommandAsync(new UpdateSupplierBankCommandValidator(), command, cancellationToken);

        var supplier = await _supplierRepository.GetByIdWithDetailsAsync(id, cancellationToken);
        if (supplier == null)
        {
            throw new NotFoundException("Nhà cung cấp", id);
        }

        supplier.BankName = string.IsNullOrWhiteSpace(command.BankName) ? null : command.BankName.Trim();
        supplier.BankAccount = string.IsNullOrWhiteSpace(command.BankAccountNumber) ? null : command.BankAccountNumber.Trim();
        supplier.AccountHolder = string.IsNullOrWhiteSpace(command.BankAccountHolder) ? null : command.BankAccountHolder.Trim().ToUpperInvariant();

        _supplierRepository.Update(supplier);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return MapToDetailDto(supplier);
    }

    public async Task<SupplierDetailDto> UpdateVatAsync(Guid id, UpdateSupplierVatCommand command, CancellationToken cancellationToken = default)
    {
        SupplierAccess.EnsureStoreOwner(_tenantContext);

        await ValidateCommandAsync(new UpdateSupplierVatCommandValidator(), command, cancellationToken);

        var supplier = await _supplierRepository.GetByIdWithDetailsAsync(id, cancellationToken);
        if (supplier == null)
        {
            throw new NotFoundException("Nhà cung cấp", id);
        }

        supplier.CompanyName = string.IsNullOrWhiteSpace(command.CompanyName) ? null : command.CompanyName.Trim();
        if (command.TaxCode != null)
        {
            supplier.TaxCode = string.IsNullOrWhiteSpace(command.TaxCode) ? null : command.TaxCode.Trim();
        }
        supplier.InvoiceAddress = string.IsNullOrWhiteSpace(command.InvoiceAddress) ? null : command.InvoiceAddress.Trim();

        _supplierRepository.Update(supplier);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return MapToDetailDto(supplier);
    }

    public async Task<SupplierDetailDto> UpdateStatusAsync(Guid id, UpdateSupplierStatusRequest request, CancellationToken cancellationToken = default)
    {
        SupplierAccess.EnsureStoreOwner(_tenantContext);

        if (!Enum.IsDefined(typeof(SupplierStatus), request.Status))
        {
            throw new BusinessException("VALIDATION_ERROR", "Trạng thái nhà cung cấp không hợp lệ.");
        }

        var supplier = await _supplierRepository.GetByIdWithDetailsAsync(id, cancellationToken);
        if (supplier == null)
        {
            throw new NotFoundException("Nhà cung cấp", id);
        }

        supplier.Status = request.Status;
        _supplierRepository.Update(supplier);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var dto = MapToDetailDto(supplier);
        dto.OpenPurchaseOrderCount = 0;
        return dto;
    }

    public async Task DeleteSupplierAsync(Guid id, CancellationToken cancellationToken = default)
    {
        SupplierAccess.EnsureStoreOwner(_tenantContext);

        var supplier = await _supplierRepository.GetByIdAsync(id, cancellationToken);
        if (supplier == null)
        {
            throw new NotFoundException("Nhà cung cấp", id);
        }

        var (_, ledgerCount) = await _supplierRepository.GetPagedLedgerAsync(id, null, null, 1, 1, cancellationToken);
        if (supplier.CurrentDebt > 0 || ledgerCount > 0)
        {
            throw new BusinessException("SUPPLIER_HAS_HISTORY", "Không thể xóa nhà cung cấp đã có lịch sử giao dịch hoặc công nợ.");
        }

        _supplierRepository.Delete(supplier);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<SupplierDeliveryConfigDto> GetDeliveryConfigAsync(Guid supplierId, CancellationToken cancellationToken = default)
    {
        var supplier = await _supplierRepository.GetByIdAsync(supplierId, cancellationToken);
        if (supplier == null) throw new NotFoundException("Nhà cung cấp", supplierId);

        var blackoutDates = await _supplierRepository.GetBlackoutDatesAsync(supplierId, cancellationToken);

        var dto = MapToDeliveryConfigDto(supplier, blackoutDates);
        dto.EarliestDeliveryDate = DeliveryScheduleCalculator.CalculateEarliestDeliveryDate(
            DateTime.UtcNow,
            dto.CutoffTime,
            dto.DeliveryWeekdaysMask,
            blackoutDates);

        return dto;
    }

    public async Task<SupplierDeliveryConfigDto> UpdateDeliveryConfigAsync(Guid supplierId, UpdateSupplierDeliveryConfigCommand command, CancellationToken cancellationToken = default)
    {
        SupplierAccess.EnsureStoreOwner(_tenantContext);

        await ValidateCommandAsync(new UpdateSupplierDeliveryConfigCommandValidator(), command, cancellationToken);

        if (command.DeliveryDays == null || command.DeliveryDays.Count == 0)
        {
            throw new BusinessException("INVALID_DELIVERY_DAYS", "Phải chọn ít nhất một ngày giao hàng hoạt động trong tuần (Active Delivery Days).");
        }

        if (command.MinOrderQuantity < 0)
        {
            throw new BusinessException("INVALID_MOQ", "Số lượng đặt tối thiểu (MOQ) phải lớn hơn hoặc bằng 0.");
        }

        if (!string.IsNullOrWhiteSpace(command.CutoffTime) && !System.Text.RegularExpressions.Regex.IsMatch(command.CutoffTime.Trim(), @"^(?:[01]\d|2[0-3]):[0-5]\d$"))
        {
            throw new BusinessException("INVALID_CUTOFF_TIME", "Giờ chốt đơn (Cutoff Time) không đúng định dạng HH:mm (từ 00:00 đến 23:59).");
        }

        var supplier = await _supplierRepository.GetByIdAsync(supplierId, cancellationToken);
        if (supplier == null) throw new NotFoundException("Nhà cung cấp", supplierId);

        supplier.CutoffDayOffset = command.CutoffDayOffset >= 0 ? command.CutoffDayOffset : 1;
        supplier.CutoffTime = string.IsNullOrWhiteSpace(command.CutoffTime) ? "17:00" : command.CutoffTime.Trim();
        supplier.DeliveryWeekdaysMask = DeliveryScheduleCalculator.DayNamesToMask(command.DeliveryDays);
        supplier.DeliveryDaysJson = System.Text.Json.JsonSerializer.Serialize(command.DeliveryDays ?? []);
        supplier.MinOrderQuantity = command.MinOrderQuantity;
        supplier.AllowUrgent = command.AllowUrgent;
        supplier.UrgentLeadTimeHours = command.UrgentLeadTimeHours;
        supplier.UrgentFeePercent = command.UrgentFeePercent;
        if (command.AllowStoreEdit.HasValue)
        {
            supplier.AllowStoreEdit = command.AllowStoreEdit.Value;
        }
        if (!string.IsNullOrWhiteSpace(command.EditWindowType))
        {
            supplier.EditWindowType = command.EditWindowType.Trim();
        }
        if (command.HoursBeforeDelivery.HasValue && command.HoursBeforeDelivery.Value >= 0)
        {
            supplier.HoursBeforeDelivery = command.HoursBeforeDelivery.Value;
        }
        if (command.HoursAfterOrder.HasValue && command.HoursAfterOrder.Value >= 0)
        {
            supplier.HoursAfterOrder = command.HoursAfterOrder.Value;
        }

        // Xử lý Blackout Dates nếu có
        if (command.BlackoutDates != null)
        {
            var newBlackouts = command.BlackoutDates.Select(boDto => new SupplierBlackoutDate
            {
                SupplierId = supplierId,
                StartDate = boDto.StartDate,
                EndDate = boDto.EndDate,
                Reason = boDto.Reason,
                IsRecurringAnnual = boDto.IsRecurringAnnual
            }).ToList();

            await _supplierRepository.ReplaceBlackoutDatesAsync(supplierId, newBlackouts, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var updatedBlackouts = await _supplierRepository.GetBlackoutDatesAsync(supplierId, cancellationToken);

        var resDto = MapToDeliveryConfigDto(supplier, updatedBlackouts);
        resDto.EarliestDeliveryDate = DeliveryScheduleCalculator.CalculateEarliestDeliveryDate(
            DateTime.UtcNow,
            resDto.CutoffTime,
            resDto.DeliveryWeekdaysMask,
            updatedBlackouts);

        return resDto;
    }

    public async Task<SupplierReturnPolicyDto> GetReturnPolicyAsync(Guid supplierId, CancellationToken cancellationToken = default)
    {
        var supplier = await _supplierRepository.GetByIdAsync(supplierId, cancellationToken);
        if (supplier == null) throw new NotFoundException("Nhà cung cấp", supplierId);

        var policy = await _supplierRepository.GetActiveReturnPolicyAsync(supplierId, cancellationToken);
        if (policy == null)
        {
            return CreateDefaultReturnPolicyDto(supplierId);
        }

        return MapReturnPolicyDto(policy);
    }

    public async Task<SupplierReturnPolicyDto> UpsertReturnPolicyAsync(
        Guid supplierId,
        UpdateSupplierReturnPolicyCommand command,
        CancellationToken cancellationToken = default)
    {
        SupplierAccess.EnsureStoreOwner(_tenantContext);

        var supplier = await _supplierRepository.GetByIdAsync(supplierId, cancellationToken);
        if (supplier == null) throw new NotFoundException("Nhà cung cấp", supplierId);

        ValidateReturnPolicyCommand(command);

        var policy = await _supplierRepository.GetActiveReturnPolicyAsync(supplierId, cancellationToken);
        if (policy == null)
        {
            policy = new SupplierReturnPolicy
            {
                SupplierId = supplierId,
                TenantId = _tenantContext.CurrentTenantId ?? string.Empty,
                IsActive = true
            };
            await _supplierRepository.AddReturnPolicyAsync(policy, cancellationToken);
        }

        policy.AllowReturn = command.AllowReturn;
        policy.AllowedReturnTypesJson = System.Text.Json.JsonSerializer.Serialize(command.AllowedReturnTypes ?? []);
        policy.AllowedResolutionTypesJson = System.Text.Json.JsonSerializer.Serialize(command.AllowedResolutionTypes ?? []);
        policy.DefaultResolutionType = command.DefaultResolutionType?.Trim() ?? "REDUCE_PAYABLE";
        policy.NearExpiryMinDaysLeft = command.NearExpiryMinDaysLeft;
        policy.RequireEvidenceImage = command.RequireEvidenceImage;
        policy.MaxReturnDaysAfterReceipt = command.MaxReturnDaysAfterReceipt;
        policy.CostShareEnabled = command.CostShareEnabled;
        policy.CostShareByCategoryEnabled = command.CostShareEnabled && command.CostShareByCategoryEnabled;
        policy.StoreCostSharePercent = command.CostShareEnabled ? command.StoreCostSharePercent : null;
        policy.SupplierCostSharePercent = command.CostShareEnabled
            ? (command.SupplierCostSharePercent ?? (100m - (command.StoreCostSharePercent ?? 0m)))
            : null;
        policy.CostShareBase = string.IsNullOrWhiteSpace(command.CostShareBase) ? "REPLACEMENT_VALUE" : command.CostShareBase.Trim();
        policy.RequireStorePaymentBeforeReplacement = command.RequireStorePaymentBeforeReplacement;
        policy.Notes = command.Notes;
        policy.IsActive = true;

        await ReplaceCategoryCostSharesAsync(policy, command, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return MapReturnPolicyDto(policy);
    }

    private async Task ReplaceCategoryCostSharesAsync(
        SupplierReturnPolicy policy,
        UpdateSupplierReturnPolicyCommand command,
        CancellationToken cancellationToken)
    {
        var existing = policy.CategoryCostShares?.ToList() ?? [];
        foreach (var row in existing)
        {
            row.IsDeleted = true;
            row.UpdatedAt = DateTime.UtcNow;
        }

        if (!command.CostShareEnabled || !command.CostShareByCategoryEnabled)
            return;

        var tenantId = _tenantContext.CurrentTenantId ?? string.Empty;
        var incoming = command.CategoryCostShares ?? [];
        foreach (var item in incoming)
        {
            if (item.CategoryId == Guid.Empty) continue;
            var storePct = item.StoreCostSharePercent;
            var supplierPct = item.SupplierCostSharePercent;
            if (Math.Abs(storePct + supplierPct - 100m) > 0.01m)
                supplierPct = 100m - storePct;

            // Không revive: global filter không load soft-delete; unique index đã lọc IsDeleted=FALSE.
            (policy.CategoryCostShares ??= new List<SupplierReturnPolicyCategoryCostShare>()).Add(
                new SupplierReturnPolicyCategoryCostShare
                {
                    TenantId = tenantId,
                    PolicyId = policy.Id,
                    CategoryId = item.CategoryId,
                    StoreCostSharePercent = storePct,
                    SupplierCostSharePercent = supplierPct,
                });
        }

        await Task.CompletedTask;
    }

    private static void ValidateReturnPolicyCommand(UpdateSupplierReturnPolicyCommand command)
    {
        if (command.AllowedReturnTypes == null || command.AllowedReturnTypes.Count == 0)
            throw new BusinessException("INVALID_RETURN_TYPES", "Phải chọn ít nhất một loại lý do trả/đổi hàng.");

        if (command.AllowedResolutionTypes == null || command.AllowedResolutionTypes.Count == 0)
            throw new BusinessException("INVALID_RESOLUTION_TYPES", "Phải chọn ít nhất một hình thức bồi hoàn.");

        var defaultRes = command.DefaultResolutionType?.Trim() ?? string.Empty;
        if (!command.AllowedResolutionTypes.Any(x => string.Equals(x, defaultRes, StringComparison.OrdinalIgnoreCase)))
            throw new BusinessException("INVALID_DEFAULT_RESOLUTION", "Hình thức mặc định phải nằm trong danh sách hình thức được phép.");

        if (command.MaxReturnDaysAfterReceipt.HasValue && command.MaxReturnDaysAfterReceipt.Value < 0)
            throw new BusinessException("INVALID_RETURN_WINDOW", "Số ngày tối đa được trả hàng sau nhập không được âm.");

        if (command.NearExpiryMinDaysLeft.HasValue && command.NearExpiryMinDaysLeft.Value < 0)
            throw new BusinessException("INVALID_NEAR_EXPIRY_DAYS", "Số ngày cận hạn tối thiểu không được âm.");

        if (command.StoreCostSharePercent.HasValue || command.SupplierCostSharePercent.HasValue || command.CostShareEnabled)
        {
            if (command.CostShareEnabled && !command.StoreCostSharePercent.HasValue)
                throw new BusinessException("INVALID_COST_SHARE", "Khi bật chia sẻ chi phí phải nhập % cửa hàng chịu (mặc định NCC).");

            var storePct = command.StoreCostSharePercent ?? 0m;
            if (storePct < 0 || storePct > 100)
                throw new BusinessException("INVALID_COST_SHARE", "% cửa hàng chịu phải trong khoảng 0–100.");

            var supplierPct = command.SupplierCostSharePercent ?? (100m - storePct);
            if (supplierPct < 0 || supplierPct > 100 || Math.Abs(storePct + supplierPct - 100m) > 0.01m)
                throw new BusinessException("INVALID_COST_SHARE", "Tổng % cửa hàng và % NCC phải bằng 100.");

            if (command.CostShareByCategoryEnabled)
            {
                var rows = command.CategoryCostShares ?? [];
                var ids = rows.Select(r => r.CategoryId).Where(id => id != Guid.Empty).ToList();
                if (ids.Count != ids.Distinct().Count())
                    throw new BusinessException("INVALID_COST_SHARE_CATEGORY", "Mỗi nhóm sản phẩm chỉ được cấu hình một lần.");

                foreach (var row in rows)
                {
                    if (row.CategoryId == Guid.Empty)
                        throw new BusinessException("INVALID_COST_SHARE_CATEGORY", "Vui lòng chọn nhóm sản phẩm.");
                    if (row.StoreCostSharePercent < 0 || row.StoreCostSharePercent > 100)
                        throw new BusinessException("INVALID_COST_SHARE_CATEGORY", "% cửa hàng theo nhóm phải trong khoảng 0–100.");
                    var rowSupplier = row.SupplierCostSharePercent;
                    if (Math.Abs(row.StoreCostSharePercent + rowSupplier - 100m) > 0.01m)
                        throw new BusinessException("INVALID_COST_SHARE_CATEGORY", "Tổng % NCC và cửa hàng theo nhóm phải bằng 100.");
                }
            }
        }
    }

    private static SupplierReturnPolicyDto CreateDefaultReturnPolicyDto(Guid supplierId) => new()
    {
        Id = Guid.Empty,
        SupplierId = supplierId,
        AllowReturn = true,
        AllowedReturnTypes = ["QUALITY_DEFECT", "NEAR_EXPIRY", "EXPIRED", "WRONG_ITEM"],
        AllowedResolutionTypes = ["REDUCE_PAYABLE", "EXCHANGE", "REFUND", "SUPPLIER_CREDIT"],
        DefaultResolutionType = "REDUCE_PAYABLE",
        RequireEvidenceImage = true,
        CostShareEnabled = false,
        CostShareByCategoryEnabled = false,
        CostShareBase = "REPLACEMENT_VALUE",
        RequireStorePaymentBeforeReplacement = true,
        IsActive = true,
        CategoryCostShares = []
    };

    private static SupplierReturnPolicyDto MapReturnPolicyDto(SupplierReturnPolicy policy)
    {
        static List<string> ParseList(string? json, List<string> fallback)
        {
            if (string.IsNullOrWhiteSpace(json)) return fallback;
            try
            {
                return System.Text.Json.JsonSerializer.Deserialize<List<string>>(json) ?? fallback;
            }
            catch
            {
                return fallback;
            }
        }

        var categoryShares = (policy.CategoryCostShares ?? [])
            .Where(c => !c.IsDeleted)
            .Select(c => new SupplierReturnPolicyCategoryCostShareDto
            {
                Id = c.Id,
                CategoryId = c.CategoryId,
                CategoryName = c.Category?.Name ?? string.Empty,
                StoreCostSharePercent = c.StoreCostSharePercent,
                SupplierCostSharePercent = c.SupplierCostSharePercent
            })
            .OrderBy(c => c.CategoryName)
            .ToList();

        return new SupplierReturnPolicyDto
        {
            Id = policy.Id,
            SupplierId = policy.SupplierId,
            AllowReturn = policy.AllowReturn,
            AllowedReturnTypes = ParseList(policy.AllowedReturnTypesJson, ["QUALITY_DEFECT", "NEAR_EXPIRY", "WRONG_ITEM"]),
            AllowedResolutionTypes = ParseList(policy.AllowedResolutionTypesJson, ["REDUCE_PAYABLE", "EXCHANGE", "REFUND"]),
            DefaultResolutionType = policy.DefaultResolutionType,
            NearExpiryMinDaysLeft = policy.NearExpiryMinDaysLeft,
            RequireEvidenceImage = policy.RequireEvidenceImage,
            MaxReturnDaysAfterReceipt = policy.MaxReturnDaysAfterReceipt,
            CostShareEnabled = policy.CostShareEnabled,
            CostShareByCategoryEnabled = policy.CostShareByCategoryEnabled,
            StoreCostSharePercent = policy.StoreCostSharePercent,
            SupplierCostSharePercent = policy.SupplierCostSharePercent,
            CostShareBase = policy.CostShareBase,
            RequireStorePaymentBeforeReplacement = policy.RequireStorePaymentBeforeReplacement,
            Notes = policy.Notes,
            IsActive = policy.IsActive,
            CategoryCostShares = categoryShares
        };
    }

    public async Task<PagedResult<SupplierAPLedgerDto>> GetAPLedgerAsync(
        Guid supplierId,
        DateTime? fromDate,
        DateTime? toDate,
        int pageIndex = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default,
        APDocType? docType = null)
    {
        SupplierAccess.EnsureStoreOwner(_tenantContext);

        var (items, totalCount) = await _supplierRepository.GetPagedLedgerAsync(
            supplierId, fromDate, toDate, pageIndex, pageSize, cancellationToken, docType);

        var dtos = items.Select(MapLedgerDto).ToList();

        return new PagedResult<SupplierAPLedgerDto>
        {
            Items = dtos,
            Page = pageIndex,
            PageSize = pageSize,
            TotalItems = totalCount
        };
    }

    public async Task<SupplierAPLedgerDto> RecordAPTransactionAsync(RecordAPTransactionCommand command, CancellationToken cancellationToken = default)
    {
        SupplierAccess.EnsureStoreOwner(_tenantContext);

        await ValidateCommandAsync(new RecordAPTransactionCommandValidator(), command, cancellationToken);

        // 1. Kiểm tra IdempotencyKey (BR-S08)
        if (!string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            var existingLedger = await _supplierRepository.GetLedgerByIdempotencyKeyAsync(command.IdempotencyKey, cancellationToken);

            if (existingLedger != null)
            {
                // Đã tồn tại giao dịch này trước đó -> Idempotent replay, không cộng dồn công nợ lần nữa
                return MapLedgerDto(existingLedger);
            }
        }

        var supplier = await _supplierRepository.GetByIdAsync(command.SupplierId, cancellationToken);
        if (supplier == null)
        {
            throw new NotFoundException("Nhà cung cấp", command.SupplierId);
        }

        // v1: không cho trả vượt dư nợ (PaymentVoucher giảm nợ)
        if (command.Direction == -1 && command.DocType == APDocType.PaymentVoucher)
        {
            if (supplier.CurrentDebt <= 0)
            {
                throw new BusinessException("AP_NO_DEBT", "Nhà cung cấp không còn dư nợ để thanh toán.");
            }

            if (command.Amount > supplier.CurrentDebt)
            {
                throw new BusinessException(
                    "AP_OVERPAY",
                    $"Số tiền thanh toán ({command.Amount:N0}) vượt dư nợ hiện tại ({supplier.CurrentDebt:N0}).");
            }
        }

        var docNo = command.DocNo?.Trim() ?? string.Empty;
        if (command.DocType == APDocType.PaymentVoucher && string.IsNullOrWhiteSpace(docNo))
        {
            docNo = await GeneratePaymentDocNoAsync(cancellationToken);
        }

        var paymentMethod = string.IsNullOrWhiteSpace(command.PaymentMethod)
            ? null
            : command.PaymentMethod.Trim().ToUpperInvariant();

        // Tính toán biến động công nợ (+1: Tăng nợ, -1: Giảm nợ)
        decimal delta = command.Direction == 1 ? command.Amount : -command.Amount;
        decimal newDebt = supplier.CurrentDebt + delta;

        var ledger = new SupplierAPLedger
        {
            SupplierId = command.SupplierId,
            TransactionDate = command.TransactionDate ?? DateTime.UtcNow,
            DocType = command.DocType,
            DocNo = docNo,
            Description = command.Description,
            Amount = command.Amount,
            Direction = command.Direction,
            BalanceAfter = newDebt,
            CreatedByName = string.IsNullOrWhiteSpace(command.CreatedByName) ? "Hệ thống" : command.CreatedByName,
            IdempotencyKey = command.IdempotencyKey,
            PaymentMethod = paymentMethod,
            ReferenceNo = string.IsNullOrWhiteSpace(command.ReferenceNo) ? null : command.ReferenceNo.Trim()
        };

        supplier.CurrentDebt = newDebt;

        await _supplierRepository.AddLedgerAsync(ledger, cancellationToken);
        var updatedAging = await ComputeAgingForSupplierAsync(supplier, ledger, cancellationToken);
        supplier.OverdueDebt = updatedAging.TotalOverdue;
        _supplierRepository.Update(supplier);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return MapLedgerDto(ledger);
    }

    private async Task<string> GeneratePaymentDocNoAsync(CancellationToken cancellationToken)
    {
        var prefix = $"PAY-{DateTime.UtcNow:yyyyMMdd}-";
        var count = await _supplierRepository.CountLedgersByDocNoPrefixAsync(prefix, cancellationToken);
        return $"{prefix}{(count + 1):D4}";
    }

    private static SupplierAPLedgerDto MapLedgerDto(SupplierAPLedger ledger) => new()
    {
        Id = ledger.Id,
        SupplierId = ledger.SupplierId,
        TransactionDate = ledger.TransactionDate,
        DocType = ledger.DocType,
        DocNo = ledger.DocNo,
        Description = ledger.Description,
        Amount = ledger.Amount,
        Direction = ledger.Direction,
        BalanceAfter = ledger.BalanceAfter,
        CreatedByName = ledger.CreatedByName,
        IdempotencyKey = ledger.IdempotencyKey,
        PaymentMethod = ledger.PaymentMethod,
        ReferenceNo = ledger.ReferenceNo,
        CreatedAt = ledger.CreatedAt
    };

    public async Task<SupplierApSummaryDto> GetApSummaryAsync(Guid supplierId, CancellationToken cancellationToken = default)
    {
        SupplierAccess.EnsureStoreOwner(_tenantContext);

        var supplier = await _supplierRepository.GetByIdAsync(supplierId, cancellationToken);
        if (supplier == null)
        {
            throw new NotFoundException("Nhà cung cấp", supplierId);
        }

        var aging = await ComputeAgingForSupplierAsync(supplier, pendingLedger: null, cancellationToken);
        if (supplier.OverdueDebt != aging.TotalOverdue)
        {
            supplier.OverdueDebt = aging.TotalOverdue;
            _supplierRepository.Update(supplier);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return new SupplierApSummaryDto
        {
            SupplierId = supplier.Id,
            CurrentDebt = supplier.CurrentDebt,
            OverdueDebt = aging.TotalOverdue,
            CreditLimit = supplier.CreditLimit,
            PaymentTermDays = supplier.PaymentTermDays,
            OpenPurchaseOrderCount = 0,
            Aging = aging
        };
    }

    private async Task<SupplierApAgingDto> ComputeAgingForSupplierAsync(
        Supplier supplier,
        SupplierAPLedger? pendingLedger,
        CancellationToken cancellationToken)
    {
        if (supplier.CurrentDebt <= 0)
        {
            return new SupplierApAgingDto();
        }

        var persistedLedgers = await _supplierRepository.GetAllLedgersForSupplierAsync(supplier.Id, cancellationToken);
        var allLedgers = persistedLedgers.ToList();
        if (pendingLedger != null && allLedgers.All(l => l.Id != pendingLedger.Id))
        {
            allLedgers.Add(pendingLedger);
        }

        // Legacy/seeded opening debt without any AP ledger entries: preserve existing OverdueDebt snapshot
        if (allLedgers.Count == 0)
        {
            var clampedOverdue = Math.Clamp(supplier.OverdueDebt, 0m, supplier.CurrentDebt);
            return new SupplierApAgingDto
            {
                Current = supplier.CurrentDebt - clampedOverdue,
                Days1To30 = clampedOverdue
            };
        }

        var grnCodes = allLedgers
            .Where(l => l.Direction == 1 && l.DocType == APDocType.PurchaseInvoice && !string.IsNullOrWhiteSpace(l.DocNo))
            .Select(l => l.DocNo)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var dueDatesByCode = grnCodes.Count > 0
            ? await _supplierRepository.GetConfirmedGrnDueDatesByCodesAsync(supplier.Id, grnCodes, cancellationToken)
            : new Dictionary<string, DateTime?>();

        return CalculateAgingBuckets(
            supplier.CurrentDebt,
            supplier.PaymentTermDays,
            allLedgers,
            dueDatesByCode,
            DateTime.UtcNow.Date);
    }

    internal static SupplierApAgingDto CalculateAgingBuckets(
        decimal currentDebt,
        int paymentTermDays,
        IEnumerable<SupplierAPLedger> ledgers,
        IReadOnlyDictionary<string, DateTime?> grnDueDates,
        DateTime asOfDateUtc)
    {
        if (currentDebt <= 0)
        {
            return new SupplierApAgingDto();
        }

        var ordered = ledgers
            .OrderBy(l => l.TransactionDate)
            .ThenBy(l => l.CreatedAt)
            .ToList();

        var openLiabilities = new List<(DateTime DueDate, decimal Remaining)>();
        var safeTermDays = Math.Max(0, paymentTermDays);

        foreach (var entry in ordered)
        {
            if (entry.Amount <= 0) continue;

            if (entry.Direction == 1)
            {
                DateTime dueDate;
                if (entry.DocType == APDocType.PurchaseInvoice &&
                    grnDueDates.TryGetValue(entry.DocNo, out var grnDue) &&
                    grnDue.HasValue)
                {
                    dueDate = grnDue.Value.Date;
                }
                else
                {
                    dueDate = entry.TransactionDate.Date.AddDays(safeTermDays);
                }

                openLiabilities.Add((dueDate, entry.Amount));
            }
            else if (entry.Direction == -1)
            {
                var toSettle = entry.Amount;
                for (var i = 0; i < openLiabilities.Count && toSettle > 0; i++)
                {
                    var item = openLiabilities[i];
                    if (item.Remaining <= 0) continue;

                    var applied = Math.Min(item.Remaining, toSettle);
                    openLiabilities[i] = (item.DueDate, item.Remaining - applied);
                    toSettle -= applied;
                }
            }
        }

        var aging = new SupplierApAgingDto();
        var allocated = 0m;
        var today = asOfDateUtc.Date;

        foreach (var (dueDate, remaining) in openLiabilities)
        {
            if (remaining <= 0) continue;

            var daysOverdue = (today - dueDate).Days;
            if (daysOverdue <= 0) aging.Current += remaining;
            else if (daysOverdue <= 30) aging.Days1To30 += remaining;
            else if (daysOverdue <= 60) aging.Days31To60 += remaining;
            else if (daysOverdue <= 90) aging.Days61To90 += remaining;
            else aging.Over90Days += remaining;

            allocated += remaining;
        }

        // Reconcile any residual (e.g. legacy opening debt without ledger rows) so TotalDebt == currentDebt
        var residual = currentDebt - allocated;
        if (residual > 0)
        {
            aging.Current += residual;
        }

        return aging;
    }

    public async Task<IReadOnlyList<SupplierBranchAssignmentDto>> GetAssignedBranchesAsync(Guid supplierId, CancellationToken cancellationToken = default)
    {
        var supplier = await _supplierRepository.GetByIdWithDetailsAsync(supplierId, cancellationToken);
        if (supplier == null || !SupplierAccess.CanViewSupplier(_tenantContext, supplier))
        {
            throw new NotFoundException("Nhà cung cấp", supplierId);
        }

        return (supplier.BranchAssignments ?? [])
            .Select(a => new SupplierBranchAssignmentDto { BranchId = a.BranchId })
            .ToList();
    }

    public async Task<IReadOnlyList<SupplierBranchAssignmentDto>> ReplaceAssignedBranchesAsync(Guid supplierId, ReplaceSupplierBranchesCommand command, CancellationToken cancellationToken = default)
    {
        SupplierAccess.EnsureStoreOwner(_tenantContext);

        var supplier = await _supplierRepository.GetByIdWithDetailsAsync(supplierId, cancellationToken);
        if (supplier == null)
        {
            throw new NotFoundException("Nhà cung cấp", supplierId);
        }

        supplier.BranchAssignments ??= new List<SupplierBranchAssignment>();
        supplier.BranchAssignments.Clear();

        var uniqueIds = (command.BranchIds ?? [])
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        foreach (var branchId in uniqueIds)
        {
            supplier.BranchAssignments.Add(new SupplierBranchAssignment
            {
                SupplierId = supplier.Id,
                BranchId = branchId
            });
        }

        _supplierRepository.Update(supplier);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return uniqueIds.Select(id => new SupplierBranchAssignmentDto { BranchId = id }).ToList();
    }

    public async Task<PagedResult<SupplierBranchAssignmentDetailDto>> GetBranchAssignmentsAsync(SupplierBranchAssignmentQueryFilter filter, CancellationToken cancellationToken = default)
    {
        SupplierStatus? statusFilter = filter.Status switch
        {
            "Active" => SupplierStatus.Active,
            "Inactive" => SupplierStatus.Inactive,
            _ => null
        };

        var effectiveBranchId = BranchScopeGuard.ResolveEffectiveBranchId(_tenantContext, filter.BranchId);

        var (items, totalCount) = await _supplierRepository.GetPagedBranchAssignmentsAsync(
            filter.SupplierSearch,
            filter.BranchSearch,
            filter.SupplierCodes,
            filter.SupplierNames,
            filter.BranchCodes,
            filter.BranchNames,
            filter.SupplierId,
            effectiveBranchId,
            statusFilter,
            filter.PageIndex,
            filter.PageSize,
            cancellationToken);

        var dtos = items.Select(MapToBranchAssignmentDetailDto).ToList();

        return new PagedResult<SupplierBranchAssignmentDetailDto>
        {
            Items = dtos,
            TotalItems = totalCount,
            Page = filter.PageIndex,
            PageSize = filter.PageSize
        };
    }

    public async Task<IReadOnlyList<SupplierBranchAssignmentDetailDto>> AssignSupplierToBranchesAsync(AssignSupplierToBranchesCommand command, CancellationToken cancellationToken = default)
    {
        SupplierAccess.EnsureStoreOwner(_tenantContext);

        var supplier = await _supplierRepository.GetByIdAsync(command.SupplierId, cancellationToken);
        if (supplier == null)
        {
            throw new NotFoundException("Nhà cung cấp", command.SupplierId);
        }
        if (supplier.Status != SupplierStatus.Active)
        {
            throw new BusinessException("SUPPLIER_INACTIVE", "Không thể gán chi nhánh cho nhà cung cấp đang tạm ngưng hoạt động.");
        }

        var uniqueBranchIds = (command.BranchIds ?? [])
            .Where(bId => bId != Guid.Empty)
            .Distinct()
            .ToList();
        if (uniqueBranchIds.Count == 0)
        {
            throw new BusinessException("BRANCH_IDS_REQUIRED", "Vui lòng chọn ít nhất một chi nhánh để gán nhà cung cấp.");
        }

        var addedAssignments = new List<SupplierBranchAssignment>();
        foreach (var branchId in uniqueBranchIds)
        {
            var exists = await _supplierRepository.BranchAssignmentExistsAsync(command.SupplierId, branchId, cancellationToken);
            if (!exists)
            {
                var assignment = new SupplierBranchAssignment
                {
                    SupplierId = command.SupplierId,
                    BranchId = branchId,
                    TenantId = _tenantContext.CurrentTenantId,
                    CreatedAt = DateTime.UtcNow,
                    Status = SupplierStatus.Active
                };
                await _supplierRepository.AddBranchAssignmentAsync(assignment, cancellationToken);
                addedAssignments.Add(assignment);
            }
        }

        // Chiều đồng bộ B: catalog Active của NCC → BranchProducts các chi nhánh vừa gán
        foreach (var branchId in uniqueBranchIds)
        {
            await SyncActiveSupplierProductsToBranchAsync(command.SupplierId, branchId, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return addedAssignments.Select(a =>
        {
            var dto = MapToBranchAssignmentDetailDto(a);
            dto.SupplierCode = supplier.Code;
            dto.SupplierName = supplier.Name;
            dto.SupplierType = supplier.SupplierType;
            return dto;
        }).ToList();
    }

    public async Task<IReadOnlyList<SupplierBranchAssignmentDetailDto>> AssignSuppliersToBranchAsync(AssignSuppliersToBranchCommand command, CancellationToken cancellationToken = default)
    {
        SupplierAccess.EnsureStoreOwner(_tenantContext);

        if (command.BranchId == Guid.Empty)
        {
            throw new BusinessException("Vui lòng chọn Cửa hàng / Chi nhánh cần phân bổ.");
        }

        var branch = await _supplierRepository.GetBranchByIdAsync(command.BranchId, cancellationToken);
        if (branch == null)
        {
            throw new NotFoundException("Cửa hàng / Chi nhánh", command.BranchId);
        }

        if (!branch.IsActive)
        {
            throw new BusinessException("Cửa hàng / Chi nhánh này hiện đang tạm ngưng hoạt động, không thể phân bổ.");
        }

        var itemsBySupplier = (command.Items ?? [])
            .Where(i => i.SupplierId != Guid.Empty)
            .GroupBy(i => i.SupplierId)
            .ToDictionary(g => g.Key, g => g.Last().DeliveryDays ?? []);

        var uniqueSupplierIds = itemsBySupplier.Count > 0
            ? itemsBySupplier.Keys.ToList()
            : (command.SupplierIds ?? [])
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList();

        if (uniqueSupplierIds.Count == 0)
        {
            throw new BusinessException("Vui lòng chọn ít nhất 1 nhà cung cấp để phân bổ.");
        }

        // All-or-nothing: Kiểm tra tất cả NCC hợp lệ và đang Active trước khi xử lý
        var suppliers = new List<Supplier>();
        foreach (var supId in uniqueSupplierIds)
        {
            var sup = await _supplierRepository.GetByIdWithDetailsAsync(supId, cancellationToken);
            if (sup == null)
            {
                throw new NotFoundException("Nhà cung cấp", supId);
            }
            if (sup.Status != SupplierStatus.Active)
            {
                throw new BusinessException($"Nhà cung cấp '{sup.Name}' đang tạm ngưng hoạt động, không thể phân bổ.");
            }
            suppliers.Add(sup);
        }

        var targetStatus = command.Status ?? SupplierStatus.Active;
        var inactiveReason = command.InactiveReason?.Trim();
        if (targetStatus == SupplierStatus.Inactive && string.IsNullOrWhiteSpace(inactiveReason))
        {
            throw new BusinessException("REASON_REQUIRED", "Vui lòng nhập lý do tạm ngưng.");
        }

        var resultAssignments = new List<SupplierBranchAssignmentDetailDto>();
        foreach (var sup in suppliers)
        {
            itemsBySupplier.TryGetValue(sup.Id, out var requestedDays);
            var deliveryDays = NormalizeAssignmentDeliveryDays(requestedDays, sup);

            var existing = await _supplierRepository.GetBranchAssignmentAsync(sup.Id, command.BranchId, cancellationToken);
            if (existing != null)
            {
                existing.Status = targetStatus;
                existing.InactiveReason = targetStatus == SupplierStatus.Inactive ? inactiveReason : null;
                if (targetStatus == SupplierStatus.Active)
                {
                    existing.CreatedAt = DateTime.UtcNow;
                }

                ApplyAssignmentDeliveryDays(existing, deliveryDays);

                var dto = MapToBranchAssignmentDetailDto(existing);
                dto.SupplierCode = sup.Code;
                dto.SupplierName = sup.Name;
                dto.SupplierType = sup.SupplierType;
                dto.BranchCode = branch.BranchCode;
                dto.BranchName = branch.Name;
                dto.BranchAddress = branch.Address ?? string.Empty;
                resultAssignments.Add(dto);
            }
            else
            {
                var newAssignment = new SupplierBranchAssignment
                {
                    SupplierId = sup.Id,
                    BranchId = command.BranchId,
                    TenantId = _tenantContext.CurrentTenantId,
                    CreatedAt = DateTime.UtcNow,
                    Status = targetStatus,
                    InactiveReason = targetStatus == SupplierStatus.Inactive ? inactiveReason : null
                };
                ApplyAssignmentDeliveryDays(newAssignment, deliveryDays);
                await _supplierRepository.AddBranchAssignmentAsync(newAssignment, cancellationToken);
                var dto = MapToBranchAssignmentDetailDto(newAssignment);
                dto.SupplierCode = sup.Code;
                dto.SupplierName = sup.Name;
                dto.SupplierType = sup.SupplierType;
                dto.BranchCode = branch.BranchCode;
                dto.BranchName = branch.Name;
                dto.BranchAddress = branch.Address ?? string.Empty;
                resultAssignments.Add(dto);
            }
        }

        // Chỉ đồng bộ catalog Active sang BranchProducts khi phân bổ được tạo ở trạng thái Active (SBA-02)
        if (targetStatus == SupplierStatus.Active)
        {
            foreach (var sup in suppliers)
            {
                await SyncActiveSupplierProductsToBranchAsync(sup.Id, command.BranchId, cancellationToken);
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return resultAssignments;
    }

    public async Task UnassignSupplierBranchAsync(Guid supplierId, Guid branchId, CancellationToken cancellationToken = default)
    {
        SupplierAccess.EnsureStoreOwner(_tenantContext);

        await _supplierRepository.RemoveBranchAssignmentAsync(supplierId, branchId, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<SupplierBranchAssignmentDetailDto> UpdateBranchAssignmentStatusAsync(
        Guid supplierId,
        Guid branchId,
        UpdateSupplierBranchAssignmentStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        SupplierAccess.EnsureStoreOwner(_tenantContext);

        var assignment = await _supplierRepository.GetBranchAssignmentAsync(supplierId, branchId, cancellationToken);
        if (assignment == null)
        {
            throw new NotFoundException("Phân bổ nhà cung cấp với cửa hàng", branchId);
        }

        if (request.Status == SupplierStatus.Inactive)
        {
            var reason = request.Reason?.Trim();
            if (string.IsNullOrWhiteSpace(reason))
            {
                throw new BusinessException("INACTIVE_REASON_REQUIRED", "Vui lòng nhập lý do tạm ngưng.");
            }

            assignment.Status = SupplierStatus.Inactive;
            assignment.InactiveReason = reason;
        }
        else
        {
            assignment.Status = SupplierStatus.Active;
            assignment.InactiveReason = null;
            // Kích hoạt lại quan hệ → backfill catalog Active của NCC vào chi nhánh (tránh miss khi gán lúc API chưa có sync)
            await SyncActiveSupplierProductsToBranchAsync(supplierId, branchId, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return MapToBranchAssignmentDetailDto(assignment);
    }

    public async Task<SupplierBranchAssignmentDetailDto> UpdateBranchAssignmentDeliveryDaysAsync(
        Guid supplierId,
        Guid branchId,
        UpdateSupplierBranchAssignmentDeliveryDaysRequest request,
        CancellationToken cancellationToken = default)
    {
        SupplierAccess.EnsureStoreOwner(_tenantContext);

        var assignment = await _supplierRepository.GetBranchAssignmentAsync(supplierId, branchId, cancellationToken);
        if (assignment == null)
        {
            throw new NotFoundException("Phân bổ nhà cung cấp với cửa hàng", branchId);
        }

        var supplier = await _supplierRepository.GetByIdWithDetailsAsync(supplierId, cancellationToken);
        var days = NormalizeAssignmentDeliveryDays(request.DeliveryDays, supplier);
        ApplyAssignmentDeliveryDays(assignment, days);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var dto = MapToBranchAssignmentDetailDto(assignment);
        if (supplier != null)
        {
            dto.SupplierCode = supplier.Code;
            dto.SupplierName = supplier.Name;
            dto.SupplierType = supplier.SupplierType;
        }

        return dto;
    }

    /// <summary>
    /// Chiều đồng bộ B: copy các SupplierProduct Active của NCC sang catalog BranchProduct của chi nhánh (idempotent).
    /// </summary>
    private async Task SyncActiveSupplierProductsToBranchAsync(
        Guid supplierId,
        Guid branchId,
        CancellationToken cancellationToken)
    {
        var activeSupplierProducts = await _supplierRepository.GetActiveSupplierProductsForSupplierAsync(supplierId, cancellationToken);
        foreach (var sp in activeSupplierProducts)
        {
            var existingBp = await _supplierRepository.GetBranchProductAsync(branchId, sp.ProductId, cancellationToken);
            if (existingBp != null)
            {
                if (existingBp.AllowedSupplierIdsJson != null)
                {
                    existingBp.AddAllowedSupplierId(supplierId);
                }
                continue;
            }

            var newBp = new BranchProduct
            {
                Id = Guid.NewGuid(),
                TenantId = _tenantContext.CurrentTenantId,
                BranchId = branchId,
                ProductId = sp.ProductId,
                SellingPriceOverride = null,
                Status = "Active",
                ShelfLocation = "Kệ hàng",
                CreatedAt = DateTime.UtcNow
            };
            newBp.SetAllowedSupplierIds(new[] { supplierId });
            await _supplierRepository.AddBranchProductAsync(newBp, cancellationToken);
        }
    }

    private static SupplierBranchAssignmentDetailDto MapToBranchAssignmentDetailDto(SupplierBranchAssignment assignment)
    {
        var days = ResolveAssignmentDeliveryDayNames(assignment, assignment.Supplier);
        return new SupplierBranchAssignmentDetailDto
        {
            Id = assignment.Id,
            SupplierId = assignment.SupplierId,
            SupplierCode = assignment.Supplier?.Code ?? string.Empty,
            SupplierName = assignment.Supplier?.Name ?? string.Empty,
            SupplierType = assignment.Supplier?.SupplierType ?? SupplierType.Goods,
            BranchId = assignment.BranchId,
            BranchCode = assignment.Branch?.Code ?? string.Empty,
            BranchName = assignment.Branch?.Name ?? string.Empty,
            BranchAddress = assignment.Branch?.Address ?? string.Empty,
            CreatedAt = assignment.CreatedAt,
            Status = assignment.Status == SupplierStatus.Active ? "Active" : "Inactive",
            InactiveReason = assignment.InactiveReason,
            DeliveryDays = days,
            DeliveryDaysDisplay = FormatDeliveryDaysAbbrev(days)
        };
    }

    private static List<string> NormalizeAssignmentDeliveryDays(
        IReadOnlyList<string>? requested,
        Supplier? supplier)
    {
        if (requested != null && requested.Count > 0)
        {
            var normalized = DeliveryScheduleCalculator.MaskToDayNames(
                DeliveryScheduleCalculator.DayNamesToMask(requested));
            if (normalized.Count == 0)
            {
                throw new BusinessException("Vui lòng chọn ít nhất 1 ngày giao hàng trong tuần.");
            }

            return normalized;
        }

        return ResolveSupplierBaseDeliveryDays(supplier);
    }

    private static List<string> ResolveSupplierBaseDeliveryDays(Supplier? supplier)
    {
        if (supplier == null)
        {
            return new List<string> { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };
        }

        if (!string.IsNullOrWhiteSpace(supplier.DeliveryDaysJson))
        {
            try
            {
                var days = System.Text.Json.JsonSerializer.Deserialize<List<string>>(supplier.DeliveryDaysJson);
                if (days != null && days.Count > 0)
                {
                    return DeliveryScheduleCalculator.MaskToDayNames(
                        DeliveryScheduleCalculator.DayNamesToMask(days));
                }
            }
            catch
            {
                // fall through
            }
        }

        var fromMask = DeliveryScheduleCalculator.MaskToDayNames(supplier.DeliveryWeekdaysMask);
        return fromMask.Count > 0
            ? fromMask
            : new List<string> { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };
    }

    private static List<string> ResolveAssignmentDeliveryDayNames(
        SupplierBranchAssignment assignment,
        Supplier? supplier)
    {
        if (!string.IsNullOrWhiteSpace(assignment.DeliveryDaysJson))
        {
            try
            {
                var days = System.Text.Json.JsonSerializer.Deserialize<List<string>>(assignment.DeliveryDaysJson);
                if (days != null && days.Count > 0)
                {
                    return DeliveryScheduleCalculator.MaskToDayNames(
                        DeliveryScheduleCalculator.DayNamesToMask(days));
                }
            }
            catch
            {
                // fall through
            }
        }

        if (assignment.DeliveryWeekdaysMask is > 0)
        {
            var fromMask = DeliveryScheduleCalculator.MaskToDayNames(assignment.DeliveryWeekdaysMask.Value);
            if (fromMask.Count > 0) return fromMask;
        }

        return ResolveSupplierBaseDeliveryDays(supplier);
    }

    private static void ApplyAssignmentDeliveryDays(SupplierBranchAssignment assignment, List<string> deliveryDays)
    {
        var mask = DeliveryScheduleCalculator.DayNamesToMask(deliveryDays);
        assignment.DeliveryWeekdaysMask = mask;
        assignment.DeliveryDaysJson = System.Text.Json.JsonSerializer.Serialize(
            DeliveryScheduleCalculator.MaskToDayNames(mask));
    }

    private static string FormatDeliveryDaysAbbrev(IReadOnlyList<string> days)
    {
        if (days == null || days.Count == 0) return "—";
        var order = new[] { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Mon"] = "T2",
            ["Tue"] = "T3",
            ["Wed"] = "T4",
            ["Thu"] = "T5",
            ["Fri"] = "T6",
            ["Sat"] = "T7",
            ["Sun"] = "CN"
        };

        return string.Join(", ",
            order.Where(d => days.Any(x => string.Equals(x, d, StringComparison.OrdinalIgnoreCase)))
                .Select(d => map[d]));
    }

    public async Task<SupplierDetailDto> LinkCustomerAsync(Guid supplierId, LinkCustomerRequest request, CancellationToken cancellationToken = default)
    {
        SupplierAccess.EnsureStoreOwner(_tenantContext);

        var supplier = await _supplierRepository.GetByIdWithDetailsAsync(supplierId, cancellationToken);
        if (supplier == null)
        {
            throw new NotFoundException("Nhà cung cấp", supplierId);
        }

        // BR-S11: Gán liên kết Customer mà không can thiệp hay cấn trừ công nợ AP và AR
        supplier.LinkedCustomerId = request.CustomerId.ToString();
        _supplierRepository.Update(supplier);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return MapToDetailDto(supplier);
    }

    private static SupplierDetailDto MapToDetailDto(Supplier s)
    {
        var blackoutList = s.BlackoutDates ?? new List<SupplierBlackoutDate>();

        var dto = new SupplierDetailDto
        {
            Id = s.Id,
            Code = s.Code,
            Name = s.Name,
            SupplierType = s.SupplierType,
            Phone = s.Phone,
            Email = s.Email,
            Status = s.Status,
            PaymentTermDays = s.PaymentTermDays,
            CreditLimit = s.CreditLimit,
            CurrentDebt = s.CurrentDebt,
            OverdueDebt = s.OverdueDebt,
            LinkedCustomerId = Guid.TryParse(s.LinkedCustomerId, out var cid) ? cid : null,
            CreatedAt = s.CreatedAt,
            TaxCode = s.TaxCode,
            Address = s.Address,
            Ward = s.Ward,
            District = s.District,
            Province = s.Province,
            ContactPerson = s.ContactPerson,
            ContactPhone = s.ContactPhone,
            BankName = s.BankName,
            BankAccountNumber = s.BankAccount ?? s.BankAccountNumber,
            BankAccountHolder = s.AccountHolder ?? s.BankAccountHolder,
            CompanyName = s.CompanyName,
            InvoiceAddress = s.InvoiceAddress,
            Notes = s.Notes,
            BlackoutDates = blackoutList.Select(b => new SupplierBlackoutDateDto
            {
                Id = b.Id,
                SupplierId = b.SupplierId,
                StartDate = b.StartDate,
                EndDate = b.EndDate,
                Reason = b.Reason,
                IsRecurringAnnual = b.IsRecurringAnnual
            }).ToList()
        };

        dto.DeliveryConfig = MapToDeliveryConfigDto(s, blackoutList);

        return dto;
    }

    private static SupplierDeliveryConfigDto MapToDeliveryConfigDto(Supplier supplier, IEnumerable<SupplierBlackoutDate>? blackoutDates)
    {
        var days = new List<string>();
        if (!string.IsNullOrWhiteSpace(supplier.DeliveryDaysJson))
        {
            try
            {
                days = System.Text.Json.JsonSerializer.Deserialize<List<string>>(supplier.DeliveryDaysJson) ?? [];
            }
            catch
            {
                days = DeliveryScheduleCalculator.MaskToDayNames(supplier.DeliveryWeekdaysMask);
            }
        }
        else
        {
            days = DeliveryScheduleCalculator.MaskToDayNames(supplier.DeliveryWeekdaysMask);
        }

        return new SupplierDeliveryConfigDto
        {
            Id = supplier.Id,
            SupplierId = supplier.Id,
            CutoffDayOffset = supplier.CutoffDayOffset,
            CutoffTime = supplier.CutoffTime,
            OrderCutoffTime = supplier.CutoffTime,
            DeliveryWeekdaysMask = supplier.DeliveryWeekdaysMask,
            DeliveryDays = days,
            MinOrderQuantity = supplier.MinOrderQuantity,
            AllowUrgent = supplier.AllowUrgent,
            UrgentLeadTimeHours = supplier.UrgentLeadTimeHours,
            UrgentFeePercent = supplier.UrgentFeePercent,
            AllowStoreEdit = supplier.AllowStoreEdit,
            EditWindowType = string.IsNullOrWhiteSpace(supplier.EditWindowType) ? "BEFORE_CUTOFF" : supplier.EditWindowType,
            HoursBeforeDelivery = supplier.HoursBeforeDelivery,
            HoursAfterOrder = supplier.HoursAfterOrder,
            BlackoutDates = blackoutDates?.Select(b => new SupplierBlackoutDateDto
            {
                Id = b.Id,
                SupplierId = b.SupplierId,
                StartDate = b.StartDate,
                EndDate = b.EndDate,
                Reason = b.Reason,
                IsRecurringAnnual = b.IsRecurringAnnual
            }).ToList() ?? []
        };
    }

    // ==========================================
    // SUPPLIER PRODUCT MANAGEMENT (REQ-SP01 - REQ-SP06)
    // ==========================================

    public async Task<PagedResult<SupplierProductDto>> GetSupplierProductsAsync(
        Guid supplierId,
        SupplierProductQueryFilter filter,
        CancellationToken cancellationToken = default)
    {
        if (supplierId != Guid.Empty)
        {
            var supplier = await _supplierRepository.GetByIdAsync(supplierId, cancellationToken);
            if (supplier == null)
            {
                throw new NotFoundException($"Không tìm thấy nhà cung cấp với ID: {supplierId}");
            }
        }

        var (items, totalCount) = await _supplierRepository.GetPagedSupplierProductsAsync(
            supplierId,
            filter.SearchTerm,
            filter.CategoryId,
            filter.Status,
            filter.PageIndex,
            filter.PageSize,
            filter.SupplierSearch,
            filter.CategoryName,
            filter.Skus,
            filter.Names,
            filter.SupplierCodes,
            filter.SupplierNames,
            cancellationToken);

        var dtos = items.Select(MapToSupplierProductDto).ToList();

        return new PagedResult<SupplierProductDto>
        {
            Items = dtos,
            Page = filter.PageIndex,
            PageSize = filter.PageSize,
            TotalItems = totalCount
        };
    }

    public async Task<SupplierProductDto> AssignSupplierProductAsync(
        Guid supplierId,
        AssignSupplierProductCommand command,
        CancellationToken cancellationToken = default)
    {
        SupplierAccess.EnsureStoreOwner(_tenantContext);

        var validator = new AssignSupplierProductValidator();
        var validationResult = await validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
        {
            throw new BusinessException("VALIDATION_ERROR", validationResult.Errors.First().ErrorMessage);
        }

        var supplier = await _supplierRepository.GetByIdAsync(supplierId, cancellationToken);
        if (supplier == null)
        {
            throw new NotFoundException($"Không tìm thấy nhà cung cấp với ID: {supplierId}");
        }
        if (supplier.Status != SupplierStatus.Active)
        {
            throw new BusinessException("SUPPLIER_INACTIVE", "Không thể gán sản phẩm cho nhà cung cấp đang tạm ngưng hoạt động.");
        }

        var product = await _supplierRepository.GetProductByIdAsync(command.ProductId, cancellationToken);
        if (product == null)
        {
            throw new NotFoundException($"Không tìm thấy sản phẩm với ID: {command.ProductId}");
        }

        // BR-SP01: Chặn trùng lặp gán cùng 1 sản phẩm cho cùng 1 nhà cung cấp trong cùng Tenant
        var existing = await _supplierRepository.GetSupplierProductAsync(supplierId, command.ProductId, cancellationToken);
        if (existing != null)
        {
            throw new BusinessException("PRODUCT_ALREADY_ASSIGNED_TO_SUPPLIER", "Sản phẩm này đã được gán cho nhà cung cấp này trong hệ thống.");
        }

        // NULL/0 = chưa thỏa thuận — không copy Master vào Agreed
        var purchasePrice = SupplierProductPricing.NormalizeAgreedPriceToPersist(command.AgreedWholesalePrice);

        var (deliveryUomId, deliveryUoM, conversionRate) = ResolvePurchaseUnitBinding(product, command.DeliveryUoM);

        // BR-SP02: Độc quyền Primary Supplier — nếu đặt làm Primary thì tự động gỡ cờ Primary của NCC cũ
        if (command.IsPrimarySupplier)
        {
            await _supplierRepository.ClearPrimarySupplierFlagAsync(command.ProductId, null, cancellationToken);
        }

        var entity = new SupplierProduct
        {
            Id = Guid.NewGuid(),
            SupplierId = supplierId,
            Supplier = supplier,
            ProductId = command.ProductId,
            Product = product,
            SupplierProductCode = command.SupplierProductCode,
            DeliveryUomId = deliveryUomId,
            DeliveryUoM = deliveryUoM,
            ConversionRate = conversionRate,
            AgreedWholesalePrice = purchasePrice,
            MinimumOrderQuantity = command.MinimumOrderQuantity,
            AllowedShortageQuantity = Math.Max(0, command.AllowedShortageQuantity),
            IsPrimarySupplier = command.IsPrimarySupplier,
            Status = SupplierProductStatus.Active
        };

        await _supplierRepository.AddSupplierProductAsync(entity, cancellationToken);

        // Tự động phân bổ catalog sản phẩm sang tất cả các chi nhánh đang liên kết Active với NCC này (Chiều đồng bộ A)
        var activeBranchIds = await _supplierRepository.GetActiveBranchIdsForSupplierAsync(supplierId, cancellationToken);
        foreach (var branchId in activeBranchIds)
        {
            var existingBp = await _supplierRepository.GetBranchProductAsync(branchId, command.ProductId, cancellationToken);
            if (existingBp != null)
            {
                if (existingBp.AllowedSupplierIdsJson != null)
                {
                    existingBp.AddAllowedSupplierId(supplierId);
                }
            }
            else
            {
                var newBranchProduct = new BranchProduct
                {
                    Id = Guid.NewGuid(),
                    TenantId = _tenantContext.CurrentTenantId,
                    BranchId = branchId,
                    ProductId = command.ProductId,
                    SellingPriceOverride = null,
                    Status = "Active",
                    ShelfLocation = "Kệ hàng",
                    CreatedAt = DateTime.UtcNow
                };
                newBranchProduct.SetAllowedSupplierIds(new[] { supplierId });
                await _supplierRepository.AddBranchProductAsync(newBranchProduct, cancellationToken);
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return MapToSupplierProductDto(entity, activeBranchIds.Count);
    }

    public async Task<BatchAssignSupplierProductsResultDto> BatchAssignSupplierProductsAsync(
        Guid supplierId,
        BatchAssignSupplierProductsCommand command,
        CancellationToken cancellationToken = default)
    {
        SupplierAccess.EnsureStoreOwner(_tenantContext);
        var workItems = ResolveBatchWorkItems(command);
        if (workItems.Count == 0)
        {
            throw new BusinessException("VALIDATION_ERROR", "Vui lòng chọn ít nhất một sản phẩm để gán cho nhà cung cấp.");
        }

        if (workItems.Any(x => x.MinimumOrderQuantity <= 0 || x.MinimumOrderQuantity > 1_000_000))
        {
            throw new BusinessException("VALIDATION_ERROR", "Số lượng đặt hàng tối thiểu (MOQ) phải từ 1 đến 1.000.000.");
        }

        if (workItems.Any(x => x.AllowedShortageQuantity < 0 || x.AllowedShortageQuantity > 1_000_000))
        {
            throw new BusinessException("VALIDATION_ERROR", "Số lượng thiếu hàng cho phép phải từ 0 đến 1.000.000.");
        }

        if (workItems.Any(x => x.AgreedWholesalePrice.HasValue && (x.AgreedWholesalePrice.Value < 0 || x.AgreedWholesalePrice.Value > 1_000_000_000m)))
        {
            throw new BusinessException("VALIDATION_ERROR", "Giá thỏa thuận phải từ 0 đến 1.000.000.000 đ.");
        }

        var supplier = await _supplierRepository.GetByIdAsync(supplierId, cancellationToken);
        if (supplier == null)
        {
            throw new NotFoundException($"Không tìm thấy nhà cung cấp với ID: {supplierId}");
        }
        if (supplier.Status != SupplierStatus.Active)
        {
            throw new BusinessException("SUPPLIER_INACTIVE", "Không thể gán sản phẩm cho nhà cung cấp đang tạm ngưng hoạt động.");
        }

        var prepared = new List<(Domain.Entities.Catalog.Product Product, decimal? PurchasePrice, int Moq, int AllowedShortage)>();
        foreach (var item in workItems)
        {
            var product = await _supplierRepository.GetProductByIdAsync(item.ProductId, cancellationToken);
            if (product == null)
            {
                throw new NotFoundException($"Không tìm thấy sản phẩm với ID: {item.ProductId}");
            }

            var existing = await _supplierRepository.GetSupplierProductAsync(supplierId, item.ProductId, cancellationToken);
            if (existing != null)
            {
                continue;
            }

            var purchasePrice = SupplierProductPricing.NormalizeAgreedPriceToPersist(item.AgreedWholesalePrice);
            prepared.Add((product, purchasePrice, item.MinimumOrderQuantity, Math.Max(0, item.AllowedShortageQuantity)));
        }

        var activeBranchIds = await _supplierRepository.GetActiveBranchIdsForSupplierAsync(supplierId, cancellationToken);
        var resultItems = new List<SupplierProductDto>();

        await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            foreach (var (product, purchasePrice, moq, allowedShortage) in prepared)
            {
                var requestedUom = !string.IsNullOrWhiteSpace(command.DeliveryUoM) && command.DeliveryUoM != "Theo ĐVT cơ sở"
                    ? command.DeliveryUoM
                    : null;
                var (deliveryUomId, deliveryUoM, conversionRate) = ResolvePurchaseUnitBinding(product, requestedUom);

                var entity = new SupplierProduct
                {
                    Id = Guid.NewGuid(),
                    SupplierId = supplierId,
                    Supplier = supplier,
                    ProductId = product.Id,
                    Product = product,
                    SupplierProductCode = command.SupplierProductCode,
                    DeliveryUomId = deliveryUomId,
                    DeliveryUoM = deliveryUoM,
                    ConversionRate = conversionRate,
                    AgreedWholesalePrice = purchasePrice,
                    MinimumOrderQuantity = moq,
                    AllowedShortageQuantity = allowedShortage,
                    IsPrimarySupplier = false,
                    Status = SupplierProductStatus.Active
                };

                await _supplierRepository.AddSupplierProductAsync(entity, ct);

                foreach (var branchId in activeBranchIds)
                {
                    var existingBp = await _supplierRepository.GetBranchProductAsync(branchId, product.Id, ct);
                    if (existingBp != null)
                    {
                        if (existingBp.AllowedSupplierIdsJson != null)
                        {
                            existingBp.AddAllowedSupplierId(supplierId);
                        }
                    }
                    else
                    {
                        var newBranchProduct = new BranchProduct
                        {
                            Id = Guid.NewGuid(),
                            TenantId = _tenantContext.CurrentTenantId,
                            BranchId = branchId,
                            ProductId = product.Id,
                            SellingPriceOverride = null,
                            Status = "Active",
                            ShelfLocation = "Kệ hàng",
                            CreatedAt = DateTime.UtcNow
                        };
                        newBranchProduct.SetAllowedSupplierIds(new[] { supplierId });
                        await _supplierRepository.AddBranchProductAsync(newBranchProduct, ct);
                    }
                }

                resultItems.Add(MapToSupplierProductDto(entity, activeBranchIds.Count));
            }

            await _unitOfWork.SaveChangesAsync(ct);
        }, cancellationToken);

        return new BatchAssignSupplierProductsResultDto
        {
            AssignedCount = resultItems.Count,
            SyncedBranchesCount = activeBranchIds.Count,
            Items = resultItems
        };
    }

    public async Task<SupplierProductDto> UpdateSupplierProductAsync(
        Guid supplierId,
        Guid productId,
        UpdateSupplierProductCommand command,
        CancellationToken cancellationToken = default)
    {
        SupplierAccess.EnsureStoreOwner(_tenantContext);

        var validator = new UpdateSupplierProductValidator();
        var validationResult = await validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
        {
            throw new BusinessException("VALIDATION_ERROR", validationResult.Errors.First().ErrorMessage);
        }

        var entity = await _supplierRepository.GetSupplierProductAsync(supplierId, productId, cancellationToken);
        if (entity == null)
        {
            throw new NotFoundException($"Không tìm thấy liên kết sản phẩm - nhà cung cấp với Product ID: {productId}");
        }

        // BR-SP02: Độc quyền Primary Supplier
        if (command.IsPrimarySupplier && !entity.IsPrimarySupplier)
        {
            await _supplierRepository.ClearPrimarySupplierFlagAsync(productId, supplierId, cancellationToken);
        }

        var product = entity.Product ?? await _supplierRepository.GetProductByIdAsync(productId, cancellationToken);
        var purchasePrice = SupplierProductPricing.NormalizeAgreedPriceToPersist(command.AgreedWholesalePrice);

        if (product != null)
        {
            var (deliveryUomId, deliveryUoM, conversionRate) = ResolvePurchaseUnitBinding(product, command.DeliveryUoM);
            entity.DeliveryUomId = deliveryUomId;
            entity.DeliveryUoM = deliveryUoM;
            entity.ConversionRate = conversionRate;
        }
        else if (!string.IsNullOrWhiteSpace(command.DeliveryUoM))
        {
            entity.DeliveryUoM = command.DeliveryUoM.Trim();
        }

        entity.AgreedWholesalePrice = purchasePrice;
        entity.MinimumOrderQuantity = command.MinimumOrderQuantity;
        entity.AllowedShortageQuantity = Math.Max(0, command.AllowedShortageQuantity);
        entity.IsPrimarySupplier = command.IsPrimarySupplier;
        entity.SupplierProductCode = command.SupplierProductCode;
        if (product != null)
        {
            entity.Product = product;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return MapToSupplierProductDto(entity);
    }

    public async Task<SupplierProductDto> ToggleSupplierProductStatusAsync(
        Guid supplierId,
        Guid productId,
        ToggleSupplierProductStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        SupplierAccess.EnsureStoreOwner(_tenantContext);

        var entity = await _supplierRepository.GetSupplierProductAsync(supplierId, productId, cancellationToken);
        if (entity == null)
        {
            throw new NotFoundException($"Không tìm thấy liên kết sản phẩm - nhà cung cấp với Product ID: {productId}");
        }

        if (request.Status == SupplierProductStatus.Active)
        {
            var supplier = await _supplierRepository.GetByIdAsync(supplierId, cancellationToken);
            if (supplier == null || supplier.Status != SupplierStatus.Active)
            {
                throw new BusinessException(
                    "SUPPLIER_INACTIVE",
                    "Không thể kích hoạt sản phẩm khi nhà cung cấp đang ngưng hoạt động.");
            }
        }

        entity.Status = request.Status;
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return MapToSupplierProductDto(entity);
    }

    public async Task<SupplierProductDto> SetPrimarySupplierAsync(
        Guid supplierId,
        Guid productId,
        SetPrimarySupplierRequest request,
        CancellationToken cancellationToken = default)
    {
        SupplierAccess.EnsureStoreOwner(_tenantContext);

        var entity = await _supplierRepository.GetSupplierProductAsync(supplierId, productId, cancellationToken);
        if (entity == null)
        {
            throw new NotFoundException($"Không tìm thấy liên kết sản phẩm - nhà cung cấp với Product ID: {productId}");
        }

        // BR-SP02: Khi đặt làm Primary thì gỡ cờ của tất cả các NCC khác cung ứng sản phẩm này
        if (request.IsPrimarySupplier)
        {
            await _supplierRepository.ClearPrimarySupplierFlagAsync(productId, supplierId, cancellationToken);
        }

        entity.IsPrimarySupplier = request.IsPrimarySupplier;
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return MapToSupplierProductDto(entity);
    }

    public async Task<IReadOnlyList<ProductSupplierMappingDto>> GetSuppliersForProductAsync(
        Guid productId,
        bool onlyActive = false,
        CancellationToken cancellationToken = default)
    {
        var productExists = await _supplierRepository.ProductExistsAsync(productId, cancellationToken);
        if (!productExists)
        {
            throw new NotFoundException($"Không tìm thấy sản phẩm với ID: {productId}");
        }

        var list = await _supplierRepository.GetSuppliersForProductAsync(productId, onlyActive, cancellationToken);

        return list.Select(sp => new ProductSupplierMappingDto
        {
            SupplierId = sp.SupplierId,
            SupplierCode = sp.Supplier?.Code ?? string.Empty,
            SupplierName = sp.Supplier?.Name ?? string.Empty,
            SupplierProductCode = sp.SupplierProductCode,
            DeliveryUoM = sp.DeliveryUoM,
            AgreedWholesalePrice = sp.AgreedWholesalePrice,
            MinimumOrderQuantity = sp.MinimumOrderQuantity,
            AllowedShortageQuantity = sp.AllowedShortageQuantity,
            IsPrimarySupplier = sp.IsPrimarySupplier,
            Status = sp.Status.ToString()
        }).ToList();
    }

    public async Task<SupplierSearchSuggestionsDto> GetSearchSuggestionsAsync(string query, int limit = 10, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new SupplierSearchSuggestionsDto();
        }

        return await _supplierRepository.GetSearchSuggestionsAsync(query.Trim(), limit, cancellationToken);
    }

    public async Task<PagedResult<SupplierTransactionDto>> GetTransactionsAsync(Guid supplierId, int pageIndex = 1, int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var supplier = await _supplierRepository.GetByIdWithDetailsAsync(supplierId, cancellationToken);
        if (supplier == null || !SupplierAccess.CanViewSupplier(_tenantContext, supplier))
        {
            throw new NotFoundException("Nhà cung cấp", supplierId);
        }

        var effectiveBranchId = BranchScopeGuard.IsBranchScopedEmployee(_tenantContext)
            ? BranchScopeGuard.ResolveRequiredBranchId(_tenantContext, Guid.Empty)
            : (Guid?)null;

        var (items, totalCount) = await _supplierRepository.GetPagedTransactionsAsync(
            supplierId,
            effectiveBranchId,
            pageIndex,
            pageSize,
            cancellationToken);

        return new PagedResult<SupplierTransactionDto>
        {
            Items = items,
            Page = Math.Max(1, pageIndex),
            PageSize = Math.Clamp(pageSize, 1, 100),
            TotalItems = totalCount
        };
    }

    private static List<BatchAssignSupplierProductItemCommand> ResolveBatchWorkItems(
        BatchAssignSupplierProductsCommand command)
    {
        if (command.Items != null && command.Items.Count > 0)
        {
            return command.Items
                .Where(x => x.ProductId != Guid.Empty)
                .GroupBy(x => x.ProductId)
                .Select(g => g.Last())
                .ToList();
        }

        return (command.ProductIds ?? new List<Guid>())
            .Where(id => id != Guid.Empty)
            .Distinct()
            .Select(id => new BatchAssignSupplierProductItemCommand
            {
                ProductId = id,
                AgreedWholesalePrice = command.AgreedWholesalePrice,
                MinimumOrderQuantity = command.MinimumOrderQuantity,
                AllowedShortageQuantity = command.AllowedShortageQuantity
            })
            .ToList();
    }

    private static async Task ValidateCommandAsync<T>(
        FluentValidation.IValidator<T> validator,
        T command,
        CancellationToken cancellationToken)
    {
        var result = await validator.ValidateAsync(command, cancellationToken);
        if (!result.IsValid)
        {
            throw new BusinessException("VALIDATION_ERROR", result.Errors.First().ErrorMessage);
        }
    }

    private static string ResolveDefaultDeliveryUoM(Product product)
    {
        var purchaseFromUnits = product.Units?
            .Where(u => !u.IsDeleted && u.IsPurchaseUnit)
            .Select(u => u.UnitName)
            .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));

        if (!string.IsNullOrWhiteSpace(purchaseFromUnits))
            return purchaseFromUnits.Trim();

        return string.IsNullOrWhiteSpace(product.BaseUnit) ? "Cái" : product.BaseUnit.Trim();
    }

    private static string ResolvePurchaseUnitName(SupplierProduct sp)
    {
        var purchaseFromUnits = sp.Product?.Units?
            .Where(u => !u.IsDeleted && u.IsPurchaseUnit)
            .Select(u => u.UnitName)
            .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));

        if (!string.IsNullOrWhiteSpace(purchaseFromUnits))
            return purchaseFromUnits.Trim();

        if (!string.IsNullOrWhiteSpace(sp.DeliveryUoM))
            return sp.DeliveryUoM.Trim();

        return sp.Product?.BaseUnit?.Trim() ?? "Cái";
    }

    private static (Guid? DeliveryUomId, string DeliveryUoM, decimal ConversionRate) ResolvePurchaseUnitBinding(
        Product product,
        string? requestedUoM)
    {
        var activeUnits = product.Units?.Where(u => !u.IsDeleted).ToList();
        if (activeUnits is { Count: > 0 })
        {
            if (!string.IsNullOrWhiteSpace(requestedUoM))
            {
                var trimmed = requestedUoM.Trim();
                var matched = activeUnits.FirstOrDefault(u =>
                    string.Equals(u.UnitName?.Trim(), trimmed, StringComparison.OrdinalIgnoreCase));
                if (matched != null)
                {
                    return (matched.Id, matched.UnitName.Trim(), matched.ConversionRate > 0 ? matched.ConversionRate : 1m);
                }
            }

            var purchaseUnit = activeUnits.FirstOrDefault(u => u.IsPurchaseUnit && !string.IsNullOrWhiteSpace(u.UnitName));
            if (purchaseUnit != null)
            {
                return (purchaseUnit.Id, purchaseUnit.UnitName.Trim(), purchaseUnit.ConversionRate > 0 ? purchaseUnit.ConversionRate : 1m);
            }

            var baseUnit = activeUnits.FirstOrDefault(u => u.IsBaseUnit && !string.IsNullOrWhiteSpace(u.UnitName));
            if (baseUnit != null)
            {
                return (baseUnit.Id, baseUnit.UnitName.Trim(), 1m);
            }
        }

        var fallbackName = !string.IsNullOrWhiteSpace(requestedUoM)
            ? requestedUoM.Trim()
            : (string.IsNullOrWhiteSpace(product.BaseUnit) ? "Cái" : product.BaseUnit.Trim());
        return (null, fallbackName, 1m);
    }

    private static decimal ResolvePurchaseConversionRate(SupplierProduct sp)
    {
        var purchaseUnit = sp.Product?.Units?
            .FirstOrDefault(u => !u.IsDeleted && u.IsPurchaseUnit);
        if (purchaseUnit != null && purchaseUnit.ConversionRate > 0)
            return purchaseUnit.ConversionRate;

        if (!string.IsNullOrWhiteSpace(sp.DeliveryUoM) && sp.Product?.Units != null)
        {
            var matched = sp.Product.Units.FirstOrDefault(u =>
                !u.IsDeleted &&
                string.Equals(u.UnitName?.Trim(), sp.DeliveryUoM.Trim(), StringComparison.OrdinalIgnoreCase));
            if (matched != null && matched.ConversionRate > 0)
                return matched.ConversionRate;
        }

        return sp.ConversionRate > 0 ? sp.ConversionRate : 1m;
    }

    private static SupplierProductDto MapToSupplierProductDto(SupplierProduct sp, int syncedBranchesCount = 0)
    {
        var baseCostPrice = sp.Product?.CostPrice;
        var purchaseUnit = ResolvePurchaseUnitName(sp);
        var conversionRate = ResolvePurchaseConversionRate(sp);
        var purchaseUnitCost = baseCostPrice.HasValue
            ? Math.Round(baseCostPrice.Value * conversionRate, 2)
            : (decimal?)null;
        return new SupplierProductDto
        {
            Id = sp.Id,
            SupplierId = sp.SupplierId,
            SupplierCode = sp.Supplier?.Code ?? string.Empty,
            SupplierName = sp.Supplier?.Name ?? string.Empty,
            ProductId = sp.ProductId,
            Barcode = sp.Product?.Barcode ?? string.Empty,
            Sku = sp.Product?.SKU ?? string.Empty,
            ProductName = sp.Product?.ProductName ?? string.Empty,
            CategoryName = sp.Product?.Category?.CategoryName,
            BaseUoM = sp.Product?.BaseUnit?.Trim() ?? purchaseUnit,
            SupplierProductCode = sp.SupplierProductCode,
            DeliveryUoM = purchaseUnit,
            AgreedWholesalePrice = sp.AgreedWholesalePrice,
            CostPrice = purchaseUnitCost,
            EffectivePurchasePrice = SupplierProductPricing.GetEffectivePurchasePriceOrZero(
                sp.AgreedWholesalePrice,
                purchaseUnitCost),
            MinimumOrderQuantity = sp.MinimumOrderQuantity,
            AllowedShortageQuantity = sp.AllowedShortageQuantity,
            ImageUrl = sp.Product?.ImageUrl,
            IsPrimarySupplier = sp.IsPrimarySupplier,
            Status = sp.Status.ToString(),
            MasterProductStatus = sp.Product?.Status.ToString() ?? "Active",
            SyncedBranchesCount = syncedBranchesCount,
            CreatedAt = sp.CreatedAt,
            UpdatedAt = sp.UpdatedAt
        };
    }
}

