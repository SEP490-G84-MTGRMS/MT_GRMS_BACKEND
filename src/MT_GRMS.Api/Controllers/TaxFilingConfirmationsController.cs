using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Constants;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Enums;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.Api.Controllers;

[ApiController]
[Authorize(Roles = SystemRoles.StoreOwner)]
[Route("api/tax/household/snapshots/{snapshotId:guid}/filing-confirmation")]
public sealed class TaxFilingConfirmationsController(AppDbContext db, ITenantContext tenant) : BaseApiController
{
    public sealed record FilingConfirmationResponse(DateTime? ConfirmedAtUtc);

    [HttpGet]
    public async Task<IActionResult> Get(Guid snapshotId, CancellationToken ct)
    {
        if (!await Snapshots(snapshotId).AnyAsync(ct)) return NotFound();
        return Ok(ApiResponse<FilingConfirmationResponse>.Ok(await ReadAsync(snapshotId, ct)));
    }

    [HttpPut]
    public async Task<IActionResult> Confirm(Guid snapshotId, CancellationToken ct)
    {
        var tenantId = tenant.CurrentTenantId;
        var userId = tenant.CurrentRawUserId;
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(userId)) return Forbid();
        if (!await Snapshots(snapshotId).AnyAsync(ct)) return NotFound();

        var now = DateTime.UtcNow;
        if (db.Database.IsNpgsql())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "TaxFilingConfirmations" ("SnapshotId", "TenantId", "ConfirmedBy", "ConfirmedAtUtc")
                SELECT "Id", "TenantId", {userId}, {now} FROM "HouseholdTaxSnapshots"
                    WHERE "Id" = {snapshotId} AND "TenantId" = {tenantId} AND NOT "IsDeleted" AND "Status" = {(int)TaxSnapshotStatus.Finalized}
                ON CONFLICT ("SnapshotId") DO NOTHING
                """, ct);
        }
        else if (!(await ReadAsync(snapshotId, ct)).ConfirmedAtUtc.HasValue)
        {
            db.TaxFilingConfirmations.Add(new TaxFilingConfirmation
            {
                SnapshotId = snapshotId, TenantId = tenantId, ConfirmedAtUtc = now, ConfirmedBy = userId
            });
            await db.SaveChangesAsync(ct);
        }

        var result = await ReadAsync(snapshotId, ct);
        return result.ConfirmedAtUtc.HasValue ? Ok(ApiResponse<FilingConfirmationResponse>.Ok(result)) : NotFound();
    }

    private IQueryable<HouseholdTaxSnapshot> Snapshots(Guid id) => db.HouseholdTaxSnapshots.AsNoTracking()
        .Where(row => row.Id == id && row.TenantId == tenant.CurrentTenantId && row.Status == TaxSnapshotStatus.Finalized);

    private async Task<FilingConfirmationResponse> ReadAsync(Guid id, CancellationToken ct)
    {
        var timestamp = await db.TaxFilingConfirmations.AsNoTracking()
            .Where(row => row.SnapshotId == id && row.TenantId == tenant.CurrentTenantId)
            .Select(row => (DateTime?)row.ConfirmedAtUtc).SingleOrDefaultAsync(ct);
        return new FilingConfirmationResponse(timestamp.HasValue ? DateTime.SpecifyKind(timestamp.Value, DateTimeKind.Utc) : null);
    }
}
