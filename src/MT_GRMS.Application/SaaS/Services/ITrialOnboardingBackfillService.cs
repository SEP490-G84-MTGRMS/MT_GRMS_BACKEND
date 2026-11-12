namespace MT_GRMS.Application.SaaS.Services;

public sealed record TrialBackfillItem(
    string TenantId,
    string Classification,
    IReadOnlyList<string> MaskedOwnerIds,
    string Reason);

public sealed record TrialBackfillReport(
    bool IsDryRun,
    DateTime ExecutedAtUtc,
    int TotalTenants,
    int TrialBackfilledCount,
    int PaidBackfilledCount,
    int BothHistoryCount,
    int ReviewRequiredCount,
    int NoPlanCount,
    IReadOnlyList<TrialBackfillItem> Items);

public interface ITrialOnboardingBackfillService
{
    Task<TrialBackfillReport> DryRunAsync(CancellationToken cancellationToken = default);
    Task<TrialBackfillReport> ApplyAsync(CancellationToken cancellationToken = default);
}
