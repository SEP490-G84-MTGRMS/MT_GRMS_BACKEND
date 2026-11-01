using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using MT_GRMS.Application.Common;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.Api.Filters;

/// <summary>
/// Attribute to enforce idempotency on sensitive financial actions.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class RequireIdempotencyAttribute : TypeFilterAttribute
{
    public RequireIdempotencyAttribute() : base(typeof(IdempotencyFilter))
    {
    }
}

/// <summary>
/// Two-Phase Idempotency filter preventing double-spending and race conditions.
/// Phase 1: Claims key with PENDING status before action execution.
/// Phase 2: Marks COMPLETED on 2xx success, or deletes PENDING on 4xx/5xx/exceptions to allow retry.
/// </summary>
public class IdempotencyFilter : IAsyncActionFilter
{
    private readonly AppDbContext _context;
    private readonly ITenantContext _tenantContext;

    public IdempotencyFilter(AppDbContext context, ITenantContext tenantContext)
    {
        _context = context;
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// Executes the two-phase idempotency pipeline.
    /// </summary>
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var httpContext = context.HttpContext;
        var req = httpContext.Request;

        if (!req.Headers.TryGetValue("Idempotency-Key", out var keyValues) || string.IsNullOrWhiteSpace(keyValues.ToString()))
        {
            context.Result = new BadRequestObjectResult(new ApiResponse<string>
            {
                Success = false,
                Message = "Yêu cầu bắt buộc cung cấp Header 'Idempotency-Key' (UUID) để bảo đảm chống trùng lặp giao dịch tài chính.",
                ErrorCode = "IDEMPOTENCY_KEY_REQUIRED"
            });
            return;
        }

        var key = keyValues.ToString();
        var endpoint = $"{req.Method}:{req.Path}";
        var payloadHash = ComputePayloadHash(context.ActionArguments);

        var tenantId = _tenantContext.CurrentTenantId ?? string.Empty;
        var userGuid = _tenantContext.CurrentUserId;
        var userIdStr = userGuid != Guid.Empty ? userGuid.ToString() : "anonymous";
        var scopedKey = $"{userIdStr}:{key}";

        // Phase 1: Check existing record or claim lock
        var existingRecord = await _context.IdempotencyRecords
            .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Endpoint == endpoint && r.Key == scopedKey);

        if (existingRecord != null)
        {
            if (HandleExistingRecord(context, existingRecord, payloadHash))
            {
                return;
            }

            // Stale PENDING lock cleaned up -> proceed to re-claim
            _context.IdempotencyRecords.Remove(existingRecord);
            await _context.SaveChangesAsync();
        }

        var pendingRecord = new IdempotencyRecord
        {
            TenantId = tenantId,
            UserId = userGuid != Guid.Empty ? userGuid : null,
            Key = scopedKey,
            Endpoint = endpoint,
            PayloadHash = payloadHash,
            Status = "PENDING",
            StatusCode = 0,
            ResponseJson = string.Empty,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(5)
        };

        if (!await TryClaimPendingLockAsync(context, pendingRecord, tenantId, endpoint, scopedKey, payloadHash))
        {
            return;
        }

        // Phase 2: Execute action and finalize or rollback lock
        await ExecuteAndFinalizeAsync(context, next, pendingRecord);
    }

    private static string ComputePayloadHash(IDictionary<string, object?> actionArguments)
    {
        var serializableArgs = actionArguments
            .Where(kvp => !(kvp.Value is CancellationToken))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        var payloadString = JsonSerializer.Serialize(serializableArgs);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payloadString))).ToLowerInvariant();
    }

    private static bool HandleExistingRecord(ActionExecutingContext context, IdempotencyRecord record, string payloadHash)
    {
        if (record.Status == "PENDING")
        {
            if (record.ExpiresAtUtc >= DateTime.UtcNow)
            {
                context.Result = new ConflictObjectResult(new ApiResponse<string>
                {
                    Success = false,
                    Message = "Giao dịch đang được xử lý bởi một yêu cầu song song. Vui lòng không gửi lại yêu cầu lúc này.",
                    ErrorCode = "IDEMPOTENCY_IN_PROGRESS"
                });
                return true;
            }

            // Lock is stale / expired due to server crash -> allow cleanup and reclaim
            return false;
        }

        // Status is COMPLETED
        if (record.PayloadHash != payloadHash)
        {
            context.Result = new ConflictObjectResult(new ApiResponse<string>
            {
                Success = false,
                Message = "Xung đột Idempotency-Key: Khóa này đã được sử dụng với nội dung payload khác.",
                ErrorCode = "IDEMPOTENCY_PAYLOAD_MISMATCH"
            });
            return true;
        }

        context.Result = new ContentResult
        {
            Content = record.ResponseJson,
            ContentType = "application/json",
            StatusCode = record.StatusCode
        };
        return true;
    }

    private async Task<bool> TryClaimPendingLockAsync(
        ActionExecutingContext context,
        IdempotencyRecord pendingRecord,
        string tenantId,
        string endpoint,
        string scopedKey,
        string payloadHash)
    {
        try
        {
            _context.IdempotencyRecords.Add(pendingRecord);
            await _context.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateException)
        {
            // Concurrent race condition caught by Unique Constraint
            _context.Entry(pendingRecord).State = EntityState.Detached;
            var racedRecord = await _context.IdempotencyRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Endpoint == endpoint && r.Key == scopedKey);

            if (racedRecord != null && racedRecord.Status == "COMPLETED")
            {
                if (racedRecord.PayloadHash != payloadHash)
                {
                    context.Result = new ConflictObjectResult(new ApiResponse<string>
                    {
                        Success = false,
                        Message = "Xung đột Idempotency-Key: Khóa này đã được sử dụng với nội dung payload khác.",
                        ErrorCode = "IDEMPOTENCY_PAYLOAD_MISMATCH"
                    });
                    return false;
                }

                context.Result = new ContentResult
                {
                    Content = racedRecord.ResponseJson,
                    ContentType = "application/json",
                    StatusCode = racedRecord.StatusCode
                };
                return false;
            }

            context.Result = new ConflictObjectResult(new ApiResponse<string>
            {
                Success = false,
                Message = "Giao dịch đang được xử lý bởi một yêu cầu song song. Vui lòng không gửi lại yêu cầu lúc này.",
                ErrorCode = "IDEMPOTENCY_IN_PROGRESS"
            });
            return false;
        }
    }

    private async Task ExecuteAndFinalizeAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next,
        IdempotencyRecord pendingRecord)
    {
        ActionExecutedContext executedContext;
        try
        {
            executedContext = await next();
        }
        catch (Exception)
        {
            await SafeRemovePendingRecordAsync(pendingRecord);
            throw;
        }

        if (executedContext.Exception != null && !executedContext.ExceptionHandled)
        {
            await SafeRemovePendingRecordAsync(pendingRecord);
            return;
        }

        if (executedContext.Result is ObjectResult objectResult && (objectResult.StatusCode is null or (>= 200 and < 300)))
        {
            pendingRecord.Status = "COMPLETED";
            pendingRecord.StatusCode = objectResult.StatusCode ?? 200;
            pendingRecord.ResponseJson = JsonSerializer.Serialize(objectResult.Value);
            pendingRecord.ExpiresAtUtc = DateTime.UtcNow.AddHours(24);
            await _context.SaveChangesAsync();
        }
        else
        {
            await SafeRemovePendingRecordAsync(pendingRecord);
        }
    }

    private async Task SafeRemovePendingRecordAsync(IdempotencyRecord pendingRecord)
    {
        try
        {
            _context.IdempotencyRecords.Remove(pendingRecord);
            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to remove pending idempotency record: {ex.Message}");
        }
    }
}
