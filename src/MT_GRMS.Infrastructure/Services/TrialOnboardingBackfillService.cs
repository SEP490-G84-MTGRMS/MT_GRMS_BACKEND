using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.SaaS.Services;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.Infrastructure.Services;

public sealed class TrialOnboardingBackfillService(AppDbContext db) : ITrialOnboardingBackfillService
{
    public Task<TrialBackfillReport> DryRunAsync(CancellationToken cancellationToken = default)
        => RunAsync(applyChanges: false, cancellationToken);

    public Task<TrialBackfillReport> ApplyAsync(CancellationToken cancellationToken = default)
        => RunAsync(applyChanges: true, cancellationToken);

    private async Task<TrialBackfillReport> RunAsync(bool applyChanges, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var tenants = await db.Tenants
            .IgnoreQueryFilters()
            .OrderBy(t => t.CreatedAt)
            .ThenBy(t => t.Id)
            .ToListAsync(cancellationToken);

        var paidByTenant = await db.WalletTransactions
            .IgnoreQueryFilters()
            .Where(w => w.Type == "PURCHASE_PLAN" && w.Status == "SUCCESS" && !w.IsDeleted)
            .GroupBy(w => w.TenantId)
            .Select(g => new { TenantId = g.Key, EarliestPaidAt = g.Min(x => x.CreatedAt) })
            .ToDictionaryAsync(x => x.TenantId, x => x.EarliestPaidAt, cancellationToken);

        var ownerLinks = await db.TenantUsers
            .IgnoreQueryFilters()
            .Where(tu => tu.IsActive &&
                         (tu.Role == UserRoleNames.StoreOwner || tu.Role == "StoreOwner"))
            .ToListAsync(cancellationToken);

        var ownersByTenant = ownerLinks
            .GroupBy(x => x.TenantId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.UserId).Distinct().OrderBy(id => id).ToList());

        var allOwnerIds = ownerLinks.Select(x => x.UserId).Distinct().ToList();
        var usersById = await db.Users
            .IgnoreQueryFilters()
            .Where(u => allOwnerIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, cancellationToken);

        await using var transaction = applyChanges && db.Database.IsRelational() && db.Database.CurrentTransaction == null
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        int trialCount = 0, paidCount = 0, bothCount = 0, reviewCount = 0, noPlanCount = 0;
        var items = new List<TrialBackfillItem>();

        foreach (var tenant in tenants)
        {
            var ownerIds = ownersByTenant.TryGetValue(tenant.Id, out var ids) ? ids : new List<string>();
            var maskedOwners = ownerIds.Select(MaskIdentifier).ToList();

            var hasTrialPlanOrStatus =
                string.Equals(tenant.CurrentPlan, "TRIAL", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(tenant.SubscriptionStatus, "TRIAL", StringComparison.OrdinalIgnoreCase);
            var trialExpiry = tenant.TrialExpiresAt ?? (hasTrialPlanOrStatus ? tenant.SubscriptionExpiresAt : null);
            var hasReliableTrial = tenant.TrialStartedAt.HasValue || trialExpiry.HasValue;

            var hasPaidTx = paidByTenant.TryGetValue(tenant.Id, out var earliestPaidTx);
            var hasPaidPlan =
                string.Equals(tenant.CurrentPlan, "STANDARD", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(tenant.CurrentPlan, "PRO", StringComparison.OrdinalIgnoreCase);
            var hasReliablePaid = tenant.FirstPaidAt.HasValue || hasPaidTx || hasPaidPlan;

            if (hasReliableTrial && hasReliablePaid)
            {
                bothCount++;
                var startedAt = tenant.TrialStartedAt ?? trialExpiry!.Value.AddHours(-72);
                var paidAt = tenant.FirstPaidAt ?? (hasPaidTx ? earliestPaidTx : tenant.CreatedAt);
                if (applyChanges)
                {
                    tenant.TrialStartedAt ??= startedAt;
                    tenant.TrialExpiresAt ??= trialExpiry;
                    tenant.FirstPaidAt ??= paidAt;
                    foreach (var ownerId in ownerIds)
                    {
                        if (usersById.TryGetValue(ownerId, out var owner) &&
                            (!owner.TrialConsumedAt.HasValue || owner.TrialConsumedAt > startedAt))
                        {
                            owner.TrialConsumedAt = startedAt;
                        }
                    }
                }
                items.Add(new TrialBackfillItem(tenant.Id, "TRIAL_AND_PAID", maskedOwners, "Preserved independent Trial and Paid histories."));
                continue;
            }

            if (hasReliableTrial)
            {
                trialCount++;
                var startedAt = tenant.TrialStartedAt ?? trialExpiry!.Value.AddHours(-72);
                if (applyChanges)
                {
                    tenant.TrialStartedAt ??= startedAt;
                    tenant.TrialExpiresAt ??= trialExpiry;
                    foreach (var ownerId in ownerIds)
                    {
                        if (usersById.TryGetValue(ownerId, out var owner) &&
                            (!owner.TrialConsumedAt.HasValue || owner.TrialConsumedAt > startedAt))
                        {
                            owner.TrialConsumedAt = startedAt;
                        }
                    }
                }
                items.Add(new TrialBackfillItem(tenant.Id, "TRIAL", maskedOwners, "Backfilled Trial timestamps and owner TrialConsumedAt."));
                continue;
            }

            if (hasReliablePaid)
            {
                paidCount++;
                var paidAt = tenant.FirstPaidAt ?? (hasPaidTx ? earliestPaidTx : tenant.CreatedAt);
                if (applyChanges)
                {
                    tenant.FirstPaidAt ??= paidAt;
                }
                items.Add(new TrialBackfillItem(tenant.Id, "PAID", maskedOwners, "Backfilled FirstPaidAt without fabricating Trial history."));
                continue;
            }

            if (string.Equals(tenant.SubscriptionStatus, "NO_PLAN", StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(tenant.CurrentPlan))
            {
                noPlanCount++;
                items.Add(new TrialBackfillItem(tenant.Id, "NO_PLAN", maskedOwners, "Clean NO_PLAN tenant."));
                continue;
            }

            reviewCount++;
            if (applyChanges)
            {
                tenant.SubscriptionStatus = "REVIEW_REQUIRED";
            }
            items.Add(new TrialBackfillItem(tenant.Id, "REVIEW_REQUIRED", maskedOwners, "Missing or conflicting Trial/Paid evidence; blocked from automatic Trial eligibility."));
        }

        if (applyChanges)
        {
            await db.SaveChangesAsync(cancellationToken);
            if (transaction != null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }

        return new TrialBackfillReport(
            IsDryRun: !applyChanges,
            ExecutedAtUtc: now,
            TotalTenants: tenants.Count,
            TrialBackfilledCount: trialCount,
            PaidBackfilledCount: paidCount,
            BothHistoryCount: bothCount,
            ReviewRequiredCount: reviewCount,
            NoPlanCount: noPlanCount,
            Items: items);
    }

    private static string MaskIdentifier(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return "***";
        if (id.Length <= 6) return $"{id[0]}***";
        return $"{id[..4]}***{id[^3..]}";
    }
}
