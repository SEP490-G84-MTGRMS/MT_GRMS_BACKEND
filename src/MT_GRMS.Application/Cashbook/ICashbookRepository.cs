using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Entities.Finance;

namespace MT_GRMS.Application.Cashbook;

public interface ICashbookRepository : IRepository<CashbookTransaction>
{
}
