using MT_GRMS.Application.Cashbook;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.Infrastructure.Repositories;

public class CashbookRepository : GenericRepository<CashbookTransaction>, ICashbookRepository
{
    public CashbookRepository(AppDbContext context) : base(context)
    {
    }
}
