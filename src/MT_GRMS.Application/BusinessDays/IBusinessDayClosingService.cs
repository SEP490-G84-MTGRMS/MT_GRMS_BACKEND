using MT_GRMS.Application.BusinessDays.Dtos;

namespace MT_GRMS.Application.BusinessDays;

public interface IBusinessDayClosingService
{
    Task<BusinessDayClosingResponse> CloseAsync(CloseBusinessDayRequest request, CancellationToken cancellationToken = default);
}
