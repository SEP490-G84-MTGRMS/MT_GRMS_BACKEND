using MT_GRMS.Application.Common;
using MT_GRMS.Application.Suppliers.Dtos;
using MT_GRMS.Application.Suppliers.Validators;
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
            CategoryId = s.CategoryId,
            CategoryName = s.Category?.CategoryName,
            Status = s.Status,
            PaymentTermDays = s.PaymentTermDays,
            CreditLimit = s.CreditLimit,
            CurrentDebt = canSeeDebt ? s.CurrentDebt : 0,
            LinkedCustomerId = Guid.TryParse(s.LinkedCustomerId, out var lcid) ? lcid : null,
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
        }

        // Tính toán EarliestDeliveryDate nếu có DeliveryConfig
        if (dto.DeliveryConfig != null)
        {
            dto.DeliveryConfig.EarliestDeliveryDate = DeliveryScheduleCalculator.CalculateEarliestDeliveryDate(
                DateTime.UtcNow,
                dto.DeliveryConfig.LeadTimeHours,
                dto.DeliveryConfig.CutoffTime,
                dto.DeliveryConfig.DeliveryWeekdaysMask,
                supplier.BlackoutDates ?? supplier.DeliveryConfig?.BlackoutDates);
        }

        return dto;
    }

    public async Task<SupplierDetailDto> CreateSupplierAsync(CreateSupplierCommand command, CancellationToken cancellationToken = default)
    {
        SupplierAccess.EnsureStoreOwner(_tenantContext);

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
            CategoryId = command.CategoryId,
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
            Status = SupplierStatus.Active,
            BankName = command.BankName?.Trim(),
            BankAccountNumber = command.BankAccountNumber?.Trim(),
            BankAccountHolder = command.BankAccountHolder?.Trim(),
            Notes = command.Notes?.Trim(),
            LinkedCustomerId = command.LinkedCustomerId?.ToString()
        };

        // Mặc định tạo DeliveryConfig cơ bản
        var deliveryConfig = new SupplierDeliveryConfig
        {
            Supplier = supplier,
            LeadTimeHours = 24,
            LeadTimeDays = 1,
            CutoffDayOffset = 1,
            CutoffTime = "17:00",
            OrderCutoffTime = "17:00",
            DeliveryWeekdaysMask = 63, // Mon-Sat
            DeliveryDaysJson = "[\"Mon\",\"Tue\",\"Wed\",\"Thu\",\"Fri\",\"Sat\"]",
            MinOrderQuantity = 0,
            AllowUrgent = false
        };
        supplier.DeliveryConfig = deliveryConfig;

        await _supplierRepository.AddAsync(supplier, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return MapToDetailDto(supplier);
    }

    public async Task<SupplierDetailDto> UpdateSupplierAsync(Guid id, UpdateSupplierCommand command, CancellationToken cancellationToken = default)
    {
        SupplierAccess.EnsureStoreOwner(_tenantContext);

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
        supplier.CategoryId = command.CategoryId;
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
        supplier.BankAccountNumber = command.BankAccountNumber?.Trim();
        supplier.BankAccountHolder = command.BankAccountHolder?.Trim();
        supplier.Notes = command.Notes?.Trim();
        supplier.LinkedCustomerId = command.LinkedCustomerId?.ToString();

        _supplierRepository.Update(supplier);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return MapToDetailDto(supplier);
    }

    public async Task<SupplierDetailDto> UpdateStatusAsync(Guid id, UpdateSupplierStatusRequest request, CancellationToken cancellationToken = default)
    {
        SupplierAccess.EnsureStoreOwner(_tenantContext);

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
        var config = await _supplierRepository.GetDeliveryConfigAsync(supplierId, cancellationToken);

        if (config == null)
        {
            var exists = await _supplierRepository.GetByIdAsync(supplierId, cancellationToken);
            if (exists == null) throw new NotFoundException("Nhà cung cấp", supplierId);

            // Tạo config mặc định nếu chưa có
            config = new SupplierDeliveryConfig
            {
                SupplierId = supplierId,
                LeadTimeHours = 24,
                LeadTimeDays = 1,
                CutoffDayOffset = 1,
                CutoffTime = "17:00",
                OrderCutoffTime = "17:00",
                DeliveryWeekdaysMask = 63,
                DeliveryDaysJson = "[\"Mon\",\"Tue\",\"Wed\",\"Thu\",\"Fri\",\"Sat\"]"
            };
            await _supplierRepository.AddDeliveryConfigAsync(config, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        var blackoutDates = await _supplierRepository.GetBlackoutDatesAsync(supplierId, cancellationToken);

        var dto = MapToDeliveryConfigDto(config, blackoutDates);
        dto.EarliestDeliveryDate = DeliveryScheduleCalculator.CalculateEarliestDeliveryDate(
            DateTime.UtcNow,
            dto.LeadTimeHours,
            dto.CutoffTime,
            dto.DeliveryWeekdaysMask,
            blackoutDates);

        return dto;
    }

    public async Task<SupplierDeliveryConfigDto> UpdateDeliveryConfigAsync(Guid supplierId, UpdateSupplierDeliveryConfigCommand command, CancellationToken cancellationToken = default)
    {
        SupplierAccess.EnsureStoreOwner(_tenantContext);

        var config = await _supplierRepository.GetDeliveryConfigAsync(supplierId, cancellationToken);

        if (config == null)
        {
            var exists = await _supplierRepository.GetByIdAsync(supplierId, cancellationToken);
            if (exists == null) throw new NotFoundException("Nhà cung cấp", supplierId);

            config = new SupplierDeliveryConfig { SupplierId = supplierId };
            await _supplierRepository.AddDeliveryConfigAsync(config, cancellationToken);
        }

        config.LeadTimeHours = command.LeadTimeHours;
        config.LeadTimeDays = command.LeadTimeDays > 0 ? command.LeadTimeDays : 1;
        config.CutoffDayOffset = command.CutoffDayOffset >= 0 ? command.CutoffDayOffset : 1;
        config.CutoffTime = string.IsNullOrWhiteSpace(command.CutoffTime) ? "17:00" : command.CutoffTime.Trim();
        config.OrderCutoffTime = string.IsNullOrWhiteSpace(command.OrderCutoffTime) ? config.CutoffTime : command.OrderCutoffTime.Trim();
        config.DeliveryWeekdaysMask = DeliveryScheduleCalculator.DayNamesToMask(command.DeliveryDays);
        config.DeliveryDaysJson = System.Text.Json.JsonSerializer.Serialize(command.DeliveryDays ?? []);
        config.MinOrderQuantity = command.MinOrderQuantity;
        config.AllowUrgent = command.AllowUrgent;
        config.UrgentLeadTimeHours = command.UrgentLeadTimeHours;
        config.UrgentFeePercent = command.UrgentFeePercent;

        // Xử lý Blackout Dates nếu có
        if (command.BlackoutDates != null)
        {
            var newBlackouts = command.BlackoutDates.Select(boDto => new SupplierBlackoutDate
            {
                SupplierId = supplierId,
                SupplierDeliveryConfigId = config.Id,
                StartDate = boDto.StartDate,
                EndDate = boDto.EndDate,
                Reason = boDto.Reason,
                IsRecurringAnnual = boDto.IsRecurringAnnual
            }).ToList();

            await _supplierRepository.ReplaceBlackoutDatesAsync(supplierId, config.Id, newBlackouts, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var updatedBlackouts = await _supplierRepository.GetBlackoutDatesAsync(supplierId, cancellationToken);

        var resDto = MapToDeliveryConfigDto(config, updatedBlackouts);
        resDto.EarliestDeliveryDate = DeliveryScheduleCalculator.CalculateEarliestDeliveryDate(
            DateTime.UtcNow,
            resDto.LeadTimeHours,
            resDto.CutoffTime,
            resDto.DeliveryWeekdaysMask,
            updatedBlackouts);

        return resDto;
    }

    public async Task<PagedResult<SupplierAPLedgerDto>> GetAPLedgerAsync(Guid supplierId, DateTime? fromDate, DateTime? toDate, int pageIndex = 1, int pageSize = 20, CancellationToken cancellationToken = default)
    {
        SupplierAccess.EnsureStoreOwner(_tenantContext);

        var (items, totalCount) = await _supplierRepository.GetPagedLedgerAsync(supplierId, fromDate, toDate, pageIndex, pageSize, cancellationToken);

        var dtos = items.Select(l => new SupplierAPLedgerDto
        {
            Id = l.Id,
            SupplierId = l.SupplierId,
            TransactionDate = l.TransactionDate,
            DocType = l.DocType,
            DocNo = l.DocNo,
            Description = l.Description,
            Amount = l.Amount,
            Direction = l.Direction,
            BalanceAfter = l.BalanceAfter,
            CreatedByName = l.CreatedByName,
            IdempotencyKey = l.IdempotencyKey,
            CreatedAt = l.CreatedAt
        }).ToList();

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

        // 1. Kiểm tra IdempotencyKey (BR-S08)
        if (!string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            var existingLedger = await _supplierRepository.GetLedgerByIdempotencyKeyAsync(command.IdempotencyKey, cancellationToken);

            if (existingLedger != null)
            {
                // Đã tồn tại giao dịch này trước đó -> Idempotent replay, không cộng dồn công nợ lần nữa
                return new SupplierAPLedgerDto
                {
                    Id = existingLedger.Id,
                    SupplierId = existingLedger.SupplierId,
                    TransactionDate = existingLedger.TransactionDate,
                    DocType = existingLedger.DocType,
                    DocNo = existingLedger.DocNo,
                    Description = existingLedger.Description,
                    Amount = existingLedger.Amount,
                    Direction = existingLedger.Direction,
                    BalanceAfter = existingLedger.BalanceAfter,
                    CreatedByName = existingLedger.CreatedByName,
                    IdempotencyKey = existingLedger.IdempotencyKey,
                    CreatedAt = existingLedger.CreatedAt
                };
            }
        }

        var supplier = await _supplierRepository.GetByIdAsync(command.SupplierId, cancellationToken);
        if (supplier == null)
        {
            throw new NotFoundException("Nhà cung cấp", command.SupplierId);
        }

        // Tính toán biến động công nợ (+1: Tăng nợ, -1: Giảm nợ)
        decimal delta = command.Direction == 1 ? command.Amount : -command.Amount;
        decimal newDebt = supplier.CurrentDebt + delta;

        var ledger = new SupplierAPLedger
        {
            SupplierId = command.SupplierId,
            TransactionDate = command.TransactionDate ?? DateTime.UtcNow,
            DocType = command.DocType,
            DocNo = command.DocNo,
            Description = command.Description,
            Amount = command.Amount,
            Direction = command.Direction,
            BalanceAfter = newDebt,
            CreatedByName = string.IsNullOrWhiteSpace(command.CreatedByName) ? "Hệ thống" : command.CreatedByName,
            IdempotencyKey = command.IdempotencyKey
        };

        supplier.CurrentDebt = newDebt;

        await _supplierRepository.AddLedgerAsync(ledger, cancellationToken);
        _supplierRepository.Update(supplier);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new SupplierAPLedgerDto
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
            CreatedAt = ledger.CreatedAt
        };
    }

    public async Task<SupplierApSummaryDto> GetApSummaryAsync(Guid supplierId, CancellationToken cancellationToken = default)
    {
        SupplierAccess.EnsureStoreOwner(_tenantContext);

        var supplier = await _supplierRepository.GetByIdAsync(supplierId, cancellationToken);
        if (supplier == null)
        {
            throw new NotFoundException("Nhà cung cấp", supplierId);
        }

        return new SupplierApSummaryDto
        {
            SupplierId = supplier.Id,
            CurrentDebt = supplier.CurrentDebt,
            OverdueDebt = supplier.OverdueDebt,
            CreditLimit = supplier.CreditLimit,
            OpenPurchaseOrderCount = 0
        };
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

        var (items, totalCount) = await _supplierRepository.GetPagedBranchAssignmentsAsync(
            filter.SupplierSearch,
            filter.BranchSearch,
            filter.SupplierCodes,
            filter.SupplierNames,
            filter.BranchCodes,
            filter.BranchNames,
            filter.SupplierId,
            filter.BranchId,
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

        var uniqueBranchIds = (command.BranchIds ?? [])
            .Where(bId => bId != Guid.Empty)
            .Distinct()
            .ToList();

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

        var uniqueSupplierIds = (command.SupplierIds ?? [])
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
            var sup = await _supplierRepository.GetByIdAsync(supId, cancellationToken);
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

        var resultAssignments = new List<SupplierBranchAssignmentDetailDto>();
        foreach (var sup in suppliers)
        {
            var existing = await _supplierRepository.GetBranchAssignmentAsync(sup.Id, command.BranchId, cancellationToken);
            if (existing != null)
            {
                // Tái kích hoạt quan hệ nếu đã từng bị Inactive
                if (existing.Status == SupplierStatus.Inactive)
                {
                    existing.Status = SupplierStatus.Active;
                    existing.InactiveReason = null;
                    existing.CreatedAt = DateTime.UtcNow;
                }
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
                    Status = SupplierStatus.Active
                };
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
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return MapToBranchAssignmentDetailDto(assignment);
    }

    private static SupplierBranchAssignmentDetailDto MapToBranchAssignmentDetailDto(SupplierBranchAssignment assignment)
    {
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
            InactiveReason = assignment.InactiveReason
        };
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
        var blackoutList = s.BlackoutDates ?? s.DeliveryConfig?.BlackoutDates ?? new List<SupplierBlackoutDate>();

        var dto = new SupplierDetailDto
        {
            Id = s.Id,
            Code = s.Code,
            Name = s.Name,
            SupplierType = s.SupplierType,
            Phone = s.Phone,
            Email = s.Email,
            CategoryId = s.CategoryId,
            CategoryName = s.Category?.CategoryName,
            Status = s.Status,
            PaymentTermDays = s.PaymentTermDays,
            CreditLimit = s.CreditLimit,
            CurrentDebt = s.CurrentDebt,
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
            BankAccountNumber = s.BankAccountNumber,
            BankAccountHolder = s.BankAccountHolder,
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

        if (s.DeliveryConfig != null)
        {
            dto.DeliveryConfig = MapToDeliveryConfigDto(s.DeliveryConfig, blackoutList);
        }

        return dto;
    }

    private static SupplierDeliveryConfigDto MapToDeliveryConfigDto(SupplierDeliveryConfig config, IEnumerable<SupplierBlackoutDate>? blackoutDates)
    {
        var days = new List<string>();
        if (!string.IsNullOrWhiteSpace(config.DeliveryDaysJson))
        {
            try
            {
                days = System.Text.Json.JsonSerializer.Deserialize<List<string>>(config.DeliveryDaysJson) ?? [];
            }
            catch
            {
                days = DeliveryScheduleCalculator.MaskToDayNames(config.DeliveryWeekdaysMask);
            }
        }
        else
        {
            days = DeliveryScheduleCalculator.MaskToDayNames(config.DeliveryWeekdaysMask);
        }

        return new SupplierDeliveryConfigDto
        {
            Id = config.Id,
            SupplierId = config.SupplierId,
            LeadTimeHours = config.LeadTimeHours,
            LeadTimeDays = config.LeadTimeDays,
            CutoffDayOffset = config.CutoffDayOffset,
            CutoffTime = config.CutoffTime,
            OrderCutoffTime = string.IsNullOrWhiteSpace(config.OrderCutoffTime) ? config.CutoffTime : config.OrderCutoffTime,
            DeliveryWeekdaysMask = config.DeliveryWeekdaysMask,
            DeliveryDays = days,
            MinOrderQuantity = config.MinOrderQuantity,
            AllowUrgent = config.AllowUrgent,
            UrgentLeadTimeHours = config.UrgentLeadTimeHours,
            UrgentFeePercent = config.UrgentFeePercent,
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
            DeliveryUoM = command.DeliveryUoM,
            AgreedWholesalePrice = command.AgreedWholesalePrice,
            MinimumOrderQuantity = command.MinimumOrderQuantity,
            IsPrimarySupplier = command.IsPrimarySupplier,
            Status = SupplierProductStatus.Active
        };

        await _supplierRepository.AddSupplierProductAsync(entity, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return MapToSupplierProductDto(entity);
    }

    public async Task<SupplierProductDto> UpdateSupplierProductAsync(
        Guid supplierId,
        Guid productId,
        UpdateSupplierProductCommand command,
        CancellationToken cancellationToken = default)
    {
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

        entity.DeliveryUoM = command.DeliveryUoM;
        entity.AgreedWholesalePrice = command.AgreedWholesalePrice;
        entity.MinimumOrderQuantity = command.MinimumOrderQuantity;
        entity.IsPrimarySupplier = command.IsPrimarySupplier;
        entity.SupplierProductCode = command.SupplierProductCode;

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return MapToSupplierProductDto(entity);
    }

    public async Task<SupplierProductDto> ToggleSupplierProductStatusAsync(
        Guid supplierId,
        Guid productId,
        ToggleSupplierProductStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        var entity = await _supplierRepository.GetSupplierProductAsync(supplierId, productId, cancellationToken);
        if (entity == null)
        {
            throw new NotFoundException($"Không tìm thấy liên kết sản phẩm - nhà cung cấp với Product ID: {productId}");
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

        // Transactions history (currently 0 items, ready for PO/GRN linkage, no mock)
        return new PagedResult<SupplierTransactionDto>
        {
            Items = new List<SupplierTransactionDto>(),
            Page = pageIndex,
            PageSize = pageSize,
            TotalItems = 0
        };
    }

    private static SupplierProductDto MapToSupplierProductDto(SupplierProduct sp)
    {
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
            BaseUoM = sp.Product?.UnitName ?? "Cái",
            SupplierProductCode = sp.SupplierProductCode,
            DeliveryUoM = sp.DeliveryUoM,
            AgreedWholesalePrice = sp.AgreedWholesalePrice,
            MinimumOrderQuantity = sp.MinimumOrderQuantity,
            IsPrimarySupplier = sp.IsPrimarySupplier,
            Status = sp.Status.ToString(),
            CreatedAt = sp.CreatedAt,
            UpdatedAt = sp.UpdatedAt
        };
    }
}

