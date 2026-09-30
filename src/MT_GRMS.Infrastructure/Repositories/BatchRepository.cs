using MT_GRMS.Application.Batches;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.Infrastructure.Repositories;

public class BatchRepository : GenericRepository<Batch>, IBatchRepository
{
    public BatchRepository(AppDbContext context) : base(context)
    {
    }
}
