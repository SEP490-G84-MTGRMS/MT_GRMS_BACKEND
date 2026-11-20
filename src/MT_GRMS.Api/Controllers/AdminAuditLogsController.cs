using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MT_GRMS.Domain.Constants;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.Api.Controllers;

[ApiController]
[Route("api/admin/audit-logs")]
[Authorize(Roles = RoleConstants.AdminOnly)]
public class AdminAuditLogsController(AppDbContext context) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery, StringLength(200)] string? keyword,
        [FromQuery, StringLength(50)] string? actionType,
        [FromQuery, RegularExpression("^(all|today|7d|30d)$")] string dateRange = "all",
        [FromQuery, Range(1, 1000000)] int page = 1,
        [FromQuery, Range(1, 100)] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        // AuditLog is a global entity. Only platform administrators may query across tenants.
        var query = context.AuditLogs.AsNoTracking().Where(x => !x.IsDeleted);
        var actionTypes = await query.Select(x => x.ActionType).Distinct()
            .OrderBy(x => x).ToListAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var term = keyword.Trim().ToLower();
            query = query.Where(x =>
                (x.ActorName != null && x.ActorName.ToLower().Contains(term)) ||
                (x.ActorId != null && x.ActorId.ToLower().Contains(term)) ||
                x.ActionType.ToLower().Contains(term) || x.TargetEntity.ToLower().Contains(term) ||
                (x.TargetId != null && x.TargetId.ToLower().Contains(term)) ||
                (x.IpAddress != null && x.IpAddress.Contains(term)));
        }
        if (!string.IsNullOrWhiteSpace(actionType) && actionType != "ALL")
            query = query.Where(x => x.ActionType == actionType);

        if (dateRange != "all")
        {
            var now = DateTime.UtcNow;
            var localToday = now.AddHours(7).Date;
            var days = dateRange == "7d" ? 6 : dateRange == "30d" ? 29 : 0;
            var start = localToday.AddDays(-days).AddHours(-7);
            query = query.Where(x => x.CreatedAt >= start && x.CreatedAt <= now);
        }

        var total = await query.CountAsync(cancellationToken);
        page = Math.Min(page, Math.Max(1, (int)Math.Ceiling(total / (double)pageSize)));
        var records = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new
            {
                x.Id, x.CreatedAt, x.ActorId, x.ActorName, x.ActorRole,
                x.ActionType, x.TargetEntity, x.TargetId, x.TenantId,
                x.IpAddress, x.UserAgent, x.OldValuesJson, x.NewValuesJson
            }).ToListAsync(cancellationToken);

        return Ok(new
        {
            success = true,
            data = new
            {
                items = records.Select(x => new
                {
                    id = x.Id,
                    timestamp = DateTime.SpecifyKind(x.CreatedAt, DateTimeKind.Utc),
                    actorName = x.ActorName ?? x.ActorId ?? "Hệ thống",
                    actorRole = x.ActorRole ?? "—",
                    actorEmail = "",
                    actionType = x.ActionType,
                    actionTitle = x.ActionType,
                    targetType = x.TargetEntity,
                    targetName = x.TargetEntity,
                    targetId = x.TargetId ?? "",
                    tenantId = x.TenantId,
                    ipAddress = x.IpAddress ?? "—",
                    userAgent = x.UserAgent ?? "",
                    location = "",
                    reason = "",
                    details = new { oldValue = Sanitize(x.OldValuesJson), newValue = Sanitize(x.NewValuesJson) }
                }),
                total, page, pageSize, actionTypes
            }
        });
    }

    private static JsonNode? Sanitize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var node = JsonNode.Parse(json);
            Redact(node);
            return node;
        }
        catch (System.Text.Json.JsonException)
        {
            return JsonValue.Create("Dữ liệu lịch sử không đúng định dạng JSON.");
        }
    }

    private static void Redact(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            foreach (var key in obj.Select(x => x.Key).ToArray())
            {
                var name = key.ToLowerInvariant();
                if (new[] { "password", "token", "secret", "otp", "cookie", "authorization" }.Any(name.Contains))
                    obj[key] = "[Đã ẩn]";
                else
                    Redact(obj[key]);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array) Redact(child);
        }
    }
}
