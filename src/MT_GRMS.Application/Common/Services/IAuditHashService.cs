using MT_GRMS.Domain.Entities.Finance;

namespace MT_GRMS.Application.Common.Services;

public interface IAuditHashService
{
    string ComputeEntryHash(string prevHash, CashbookEntry entry);
    bool VerifyEntryHash(string prevHash, CashbookEntry entry);
}