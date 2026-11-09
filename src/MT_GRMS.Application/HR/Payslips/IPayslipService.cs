using MT_GRMS.Application.Common;
using MT_GRMS.Application.HR.Payslips.Dtos;

namespace MT_GRMS.Application.HR.Payslips;

public interface IPayslipService
{
    Task<IReadOnlyList<PayslipResponse>> GenerateMonthlyPayrollAsync(
        GeneratePayrollBatchRequest request,
        CancellationToken cancellationToken = default);

    Task<PagedResult<PayslipSummaryDto>> GetPayslipsPagedAsync(
        PayslipQueryFilter filter,
        CancellationToken cancellationToken = default);

    Task<PayslipResponse> GetPayslipByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<PayslipResponse> PayPayslipAsync(
        Guid id,
        PayPayslipRequest request,
        CancellationToken cancellationToken = default);

    Task<BatchPayResultDto> BatchPayPayslipsAsync(
        BatchPayPayslipsRequest request,
        CancellationToken cancellationToken = default);

    Task<bool> DeletePayslipAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}

