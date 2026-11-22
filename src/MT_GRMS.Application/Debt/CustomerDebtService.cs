using MT_GRMS.Application.Cashbook;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Debt.Dtos;
using MT_GRMS.Application.Notifications;
using MT_GRMS.Domain.Exceptions;

namespace MT_GRMS.Application.Debt;

public class CustomerDebtService : ICustomerDebtService
{
    private readonly ICustomerDebtRepository _debtRepo;
    private readonly ICashbookService _cashbookService;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly NotificationOutbox? _notificationOutbox;

    public CustomerDebtService(ICustomerDebtRepository debtRepo, ICashbookService cashbookService,
        ITenantContext tenantContext, IUnitOfWork unitOfWork, NotificationOutbox? notificationOutbox = null)
    {
        _debtRepo = debtRepo;
        _cashbookService = cashbookService;
        _tenantContext = tenantContext;
        _unitOfWork = unitOfWork;
        _notificationOutbox = notificationOutbox;
    }

    public async Task<RepaymentResultResponse> RepayDebtAsync(RepayDebtRequest request, CancellationToken ct = default)
    {
        TenantRoleGuard.RequireStoreOwner(_tenantContext, "thu nợ khách hàng");
        if (request.Amount <= 0) throw new BusinessException("INVALID_AMOUNT", "Số tiền thu nợ phải lớn hơn 0.");

        var managesTransaction = !_unitOfWork.HasActiveTransaction;
        if (managesTransaction) await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            var customer = await _debtRepo.GetCustomerWithLockAsync(request.CustomerId, ct)
                ?? throw new NotFoundException($"Không tìm thấy khách hàng với Id: {request.CustomerId}");
            if (request.Amount > customer.TotalDebt)
                throw new BusinessException("REPAYMENT_EXCEEDS_DEBT", "Số tiền thu không được vượt quá tổng dư nợ hiện tại.");

            var balanceBefore = customer.TotalDebt;
            customer.TotalDebt -= request.Amount;
            customer.LastRepaymentAtUtc = DateTime.UtcNow;
            var referenceCode = string.IsNullOrWhiteSpace(request.ReferenceCode)
                ? $"PAY-{Guid.NewGuid():N}" : request.ReferenceCode.Trim();
            var cashbookEntry = await _cashbookService.ReceiveCashAsync(request.PaymentSourceAccountId,
                request.PaymentSourceType, request.Amount, "DEBT_REPAYMENT", customer.Id, referenceCode,
                request.Notes?.Trim() ?? $"Thu nợ khách hàng {customer.Name}", ct);
            if (customer.TotalDebt == 0 && _notificationOutbox != null)
            {
                await _notificationOutbox.EnqueueAsync(_tenantContext.CurrentTenantId, cashbookEntry.Id, new BusinessNotification(
                    BusinessNotificationTypes.CustomerDebtSettled, "Khách hàng đã thanh toán hết nợ",
                    $"Khách hàng {customer.CustomerCode} đã hết dư nợ.",
                    $"/store-owner/customers?search={Uri.EscapeDataString(customer.CustomerCode)}"), ct);
            }
            await _unitOfWork.SaveChangesAsync(ct);
            if (managesTransaction) await _unitOfWork.CommitTransactionAsync(ct);

            return new RepaymentResultResponse(cashbookEntry.Id, cashbookEntry.EntryCode,
                request.Amount, balanceBefore, customer.TotalDebt, cashbookEntry.Id);
        }
        catch
        {
            if (managesTransaction) await _unitOfWork.RollbackTransactionAsync(ct);
            throw;
        }
    }

    public async Task<CustomerDebtProfileResponse> GetProfileAsync(Guid customerId, CancellationToken ct = default)
    {
        TenantRoleGuard.RequireStoreOwner(_tenantContext, "công nợ khách hàng");
        var customer = await _debtRepo.GetCustomerWithLockAsync(customerId, ct)
            ?? throw new NotFoundException($"Không tìm thấy khách hàng với Id: {customerId}");
        return new CustomerDebtProfileResponse(customer.Id, customer.TotalDebt,
            customer.LastDebtIncurredAtUtc, customer.LastRepaymentAtUtc);
    }

    public Task<List<CustomerDebtLedgerItemResponse>> GetLedgerAsync(Guid customerId, int page = 1,
        int pageSize = 50, CancellationToken ct = default)
    {
        TenantRoleGuard.RequireStoreOwner(_tenantContext, "công nợ khách hàng");
        return _debtRepo.GetLedgerAsync(customerId, page, pageSize, ct);
    }
}
