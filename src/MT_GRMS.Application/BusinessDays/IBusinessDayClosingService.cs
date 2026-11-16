using MT_GRMS.Application.BusinessDays.Dtos;

namespace MT_GRMS.Application.BusinessDays;

public interface IBusinessDayClosingService
{
    Task<BusinessDateResponse> GetCurrentBusinessDateAsync(Guid branchId, CancellationToken cancellationToken = default);
    Task<BusinessDayClosingPreview> PreviewAsync(Guid branchId, DateOnly businessDate, CancellationToken cancellationToken = default);
    Task<BusinessDayClosingResponse> CloseAsync(CloseBusinessDayRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BusinessDayClosingVersionResponse>> GetHistoryAsync(Guid branchId, DateOnly businessDate, CancellationToken cancellationToken = default);
    Task<BusinessDayClosingVersionResponse> ReopenAsync(Guid closingId, ReopenBusinessDayRequest request, CancellationToken cancellationToken = default);
}
