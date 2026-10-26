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

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class RequireIdempotencyAttribute : TypeFilterAttribute
{
    public RequireIdempotencyAttribute() : base(typeof(IdempotencyFilter))
    {
    }
}

public class IdempotencyFilter : IAsyncActionFilter
{
    private readonly AppDbContext _context;
    private readonly ITenantContext _tenantContext;

    public IdempotencyFilter(AppDbContext context, ITenantContext tenantContext)
    {
        _context = context;
        _tenantContext = tenantContext;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var httpContext = context.HttpContext;
        var req = httpContext.Request;

        // Check for Idempotency-Key header
        if (!req.Headers.TryGetValue("Idempotency-Key", out var keyValues) || string.IsNullOrWhiteSpace(keyValues.ToString()))
        {
            // If required attribute is present, reject
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

        // Read and hash payload
        req.EnableBuffering();
        using var reader = new StreamReader(req.Body, Encoding.UTF8, leaveOpen: true);
        var bodyStr = await reader.ReadToEndAsync();
        req.Body.Position = 0;

        var payloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(bodyStr))).ToLowerInvariant();
        var tenantId = _tenantContext.CurrentTenantId ?? string.Empty;

        var existingRecord = await _context.IdempotencyRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Endpoint == endpoint && r.Key == key);

        if (existingRecord != null)
        {
            if (existingRecord.PayloadHash != payloadHash)
            {
                context.Result = new ConflictObjectResult(new ApiResponse<string>
                {
                    Success = false,
                    Message = "Xung đột Idempotency-Key: Khóa này đã được sử dụng với nội dung payload khác.",
                    ErrorCode = "IDEMPOTENCY_PAYLOAD_MISMATCH"
                });
                return;
            }

            // Return cached response
            var jsonDoc = JsonDocument.Parse(existingRecord.ResponseJson);
            context.Result = new ContentResult
            {
                Content = existingRecord.ResponseJson,
                ContentType = "application/json",
                StatusCode = existingRecord.StatusCode
            };
            return;
        }

        var executedContext = await next();

        // If execution succeeded, save response
        if (executedContext.Result is ObjectResult objectResult && objectResult.StatusCode is null or >= 200 and < 300)
        {
            var responseJson = JsonSerializer.Serialize(objectResult.Value);
            var record = new IdempotencyRecord
            {
                TenantId = tenantId,
                Key = key,
                Endpoint = endpoint,
                PayloadHash = payloadHash,
                ResponseJson = responseJson,
                StatusCode = objectResult.StatusCode ?? 200,
                ExpiresAtUtc = DateTime.UtcNow.AddHours(24)
            };

            _context.IdempotencyRecords.Add(record);
            await _context.SaveChangesAsync();
        }
    }
}