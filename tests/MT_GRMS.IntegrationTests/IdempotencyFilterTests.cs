using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using MT_GRMS.Api.Filters;
using MT_GRMS.Application.Common;
using MT_GRMS.Infrastructure.Persistence;
using MT_GRMS.Domain.Entities.Finance;
using Xunit;

namespace MT_GRMS.IntegrationTests;

public class IdempotencyFilterTests
{
    private class FakeTenantContext : ITenantContext
    {
        public string CurrentTenantId { get; set; } = "tenant-test-01";
        public Guid CurrentUserId { get; set; } = Guid.Parse("11111111-1111-1111-1111-111111111111");
        public Guid? CurrentBranchId { get; set; } = Guid.Parse("22222222-2222-2222-2222-222222222222");
        public void SetContext(string tenantId, Guid userId, Guid? branchId)
        {
            CurrentTenantId = tenantId;
            CurrentUserId = userId;
            CurrentBranchId = branchId;
        }
        public string CurrentRole { get; set; } = "ADMIN";
        public void SetRole(string? role) => CurrentRole = role?.Trim() ?? string.Empty;
    }

    private AppDbContext CreateDbContext(ITenantContext tenantContext)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, tenantContext);
    }

    private ActionExecutingContext CreateActionExecutingContext(HttpContext httpContext, Dictionary<string, object?> actionArgs)
    {
        var actionContext = new ActionContext(
            httpContext,
            new RouteData(),
            new ActionDescriptor(),
            new ModelStateDictionary()
        );

        return new ActionExecutingContext(
            actionContext,
            new List<IFilterMetadata>(),
            actionArgs,
            controller: new object()
        );
    }

    [Fact]
    public async Task OnActionExecutionAsync_WhenKeyMissing_ShouldReturn400BadRequest()
    {
        var tenantContext = new FakeTenantContext();
        using var context = CreateDbContext(tenantContext);
        var filter = new IdempotencyFilter(context, tenantContext);

        var httpContext = new DefaultHttpContext();
        var executingContext = CreateActionExecutingContext(httpContext, new Dictionary<string, object?>());

        await filter.OnActionExecutionAsync(executingContext, () => Task.FromResult<ActionExecutedContext>(null!));

        var badRequestResult = Assert.IsType<BadRequestObjectResult>(executingContext.Result);
        Assert.Equal(400, badRequestResult.StatusCode);
        var apiResp = Assert.IsType<ApiResponse<string>>(badRequestResult.Value);
        Assert.Equal("IDEMPOTENCY_KEY_REQUIRED", apiResp.ErrorCode);
    }

    [Fact]
    public async Task OnActionExecutionAsync_WhenSameKeyDifferentPayload_ShouldReturn409Conflict()
    {
        var tenantContext = new FakeTenantContext();
        using var context = CreateDbContext(tenantContext);
        var filter = new IdempotencyFilter(context, tenantContext);

        var idempotencyKey = Guid.NewGuid().ToString();

        // 1. Request đầu tiên: tạo phiếu 100.000đ
        var httpContext1 = new DefaultHttpContext();
        httpContext1.Request.Method = "POST";
        httpContext1.Request.Path = "/api/expenses";
        httpContext1.Request.Headers["Idempotency-Key"] = idempotencyKey;

        var args1 = new Dictionary<string, object?>
        {
            ["amount"] = 100000m,
            ["vendor"] = "Công ty A"
        };
        var executingContext1 = CreateActionExecutingContext(httpContext1, args1);

        var executedContext1 = new ActionExecutedContext(
            executingContext1,
            new List<IFilterMetadata>(),
            executingContext1.Controller
        )
        {
            Result = new ObjectResult(new { Id = 1, Voucher = "EXP-01" }) { StatusCode = 200 }
        };

        await filter.OnActionExecutionAsync(executingContext1, () => Task.FromResult(executedContext1));

        // 2. Request thứ hai: CÙNG Idempotency-Key nhưng SỬA payload (Tamper) thành 500.000đ
        var httpContext2 = new DefaultHttpContext();
        httpContext2.Request.Method = "POST";
        httpContext2.Request.Path = "/api/expenses";
        httpContext2.Request.Headers["Idempotency-Key"] = idempotencyKey;

        var args2 = new Dictionary<string, object?>
        {
            ["amount"] = 500000m,
            ["vendor"] = "Công ty A"
        };
        var executingContext2 = CreateActionExecutingContext(httpContext2, args2);

        await filter.OnActionExecutionAsync(executingContext2, () => Task.FromResult<ActionExecutedContext>(null!));

        var conflictResult = Assert.IsType<ConflictObjectResult>(executingContext2.Result);
        Assert.Equal(409, conflictResult.StatusCode);
        var apiResp = Assert.IsType<ApiResponse<string>>(conflictResult.Value);
        Assert.Equal("IDEMPOTENCY_PAYLOAD_MISMATCH", apiResp.ErrorCode);
    }

    [Fact]
    public async Task OnActionExecutionAsync_WhenSameKeySamePayload_ShouldReturnCachedResponse()
    {
        var tenantContext = new FakeTenantContext();
        using var context = CreateDbContext(tenantContext);
        var filter = new IdempotencyFilter(context, tenantContext);

        var idempotencyKey = Guid.NewGuid().ToString();

        var httpContext1 = new DefaultHttpContext();
        httpContext1.Request.Method = "POST";
        httpContext1.Request.Path = "/api/expenses";
        httpContext1.Request.Headers["Idempotency-Key"] = idempotencyKey;

        var args = new Dictionary<string, object?>
        {
            ["amount"] = 100000m,
            ["vendor"] = "Công ty A"
        };
        var executingContext1 = CreateActionExecutingContext(httpContext1, args);

        var executedContext1 = new ActionExecutedContext(
            executingContext1,
            new List<IFilterMetadata>(),
            executingContext1.Controller
        )
        {
            Result = new ObjectResult(new { Voucher = "EXP-999" }) { StatusCode = 200 }
        };

        await filter.OnActionExecutionAsync(executingContext1, () => Task.FromResult(executedContext1));

        var httpContext2 = new DefaultHttpContext();
        httpContext2.Request.Method = "POST";
        httpContext2.Request.Path = "/api/expenses";
        httpContext2.Request.Headers["Idempotency-Key"] = idempotencyKey;

        var executingContext2 = CreateActionExecutingContext(httpContext2, args);

        bool delegateCalled = false;
        await filter.OnActionExecutionAsync(executingContext2, () =>
        {
            delegateCalled = true;
            return Task.FromResult<ActionExecutedContext>(null!);
        });

        Assert.False(delegateCalled);
        var contentResult = Assert.IsType<ContentResult>(executingContext2.Result);
        Assert.Equal(200, contentResult.StatusCode);
        Assert.Contains("EXP-999", contentResult.Content);
    }

    [Fact]
    public async Task OnActionExecutionAsync_When4xxStateErrorOccurs_ShouldNotCacheAndAllowRetry()
    {
        var tenantContext = new FakeTenantContext();
        using var context = CreateDbContext(tenantContext);
        var filter = new IdempotencyFilter(context, tenantContext);

        var idempotencyKey = Guid.NewGuid().ToString();
        var args = new Dictionary<string, object?> { ["amount"] = 500000m };

        // 1. Lần đầu: Tài khoản chưa đủ tiền -> Server trả về 400 INSUFFICIENT_FUNDS
        var httpContext1 = new DefaultHttpContext();
        httpContext1.Request.Method = "POST";
        httpContext1.Request.Path = "/api/cashbook/payments";
        httpContext1.Request.Headers["Idempotency-Key"] = idempotencyKey;

        var executingContext1 = CreateActionExecutingContext(httpContext1, args);
        var executedContext1 = new ActionExecutedContext(
            executingContext1,
            new List<IFilterMetadata>(),
            executingContext1.Controller
        )
        {
            Result = new ObjectResult(new { ErrorCode = "INSUFFICIENT_FUNDS" }) { StatusCode = 400 }
        };

        await filter.OnActionExecutionAsync(executingContext1, () => Task.FromResult(executedContext1));

        // Kiểm tra: Database KHÔNG được lưu record idempotency cho lỗi 400 này
        var recordInDb = await context.IdempotencyRecords.FirstOrDefaultAsync(r => r.Key.Contains(idempotencyKey));
        Assert.Null(recordInDb);

        // 2. Lần hai: Sau khi đã nạp tiền, người dùng gửi lại đúng Key đó -> Delegate ĐƯỢC CHẠY LẠI và thành công 200!
        var httpContext2 = new DefaultHttpContext();
        httpContext2.Request.Method = "POST";
        httpContext2.Request.Path = "/api/cashbook/payments";
        httpContext2.Request.Headers["Idempotency-Key"] = idempotencyKey;

        var executingContext2 = CreateActionExecutingContext(httpContext2, args);
        bool delegateCalledOnRetry = false;

        var executedContext2 = new ActionExecutedContext(
            executingContext2,
            new List<IFilterMetadata>(),
            executingContext2.Controller
        )
        {
            Result = new ObjectResult(new { VoucherCode = "PC-001" }) { StatusCode = 200 }
        };

        await filter.OnActionExecutionAsync(executingContext2, () =>
        {
            delegateCalledOnRetry = true;
            return Task.FromResult(executedContext2);
        });

        Assert.True(delegateCalledOnRetry);
    }

    [Fact]
    public async Task OnActionExecutionAsync_ScopeIncludesUserId_DifferentUsersCanUseSameKey()
    {
        var tenantContext = new FakeTenantContext();
        using var context = CreateDbContext(tenantContext);
        var filter = new IdempotencyFilter(context, tenantContext);

        var idempotencyKey = "shared-uuid-001";
        var args = new Dictionary<string, object?> { ["amount"] = 50000m };

        // 1. User 1 gửi request
        tenantContext.CurrentUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var httpContext1 = new DefaultHttpContext();
        httpContext1.Request.Method = "POST";
        httpContext1.Request.Path = "/api/cashbook/payments";
        httpContext1.Request.Headers["Idempotency-Key"] = idempotencyKey;

        var executingContext1 = CreateActionExecutingContext(httpContext1, args);
        var executedContext1 = new ActionExecutedContext(
            executingContext1,
            new List<IFilterMetadata>(),
            executingContext1.Controller
        )
        {
            Result = new ObjectResult(new { Result = "User1 Success" }) { StatusCode = 200 }
        };

        await filter.OnActionExecutionAsync(executingContext1, () => Task.FromResult(executedContext1));

        // 2. User 2 gửi request CÙNG KEY idempotencyKey
        tenantContext.CurrentUserId = Guid.Parse("99999999-9999-9999-9999-999999999999");
        var httpContext2 = new DefaultHttpContext();
        httpContext2.Request.Method = "POST";
        httpContext2.Request.Path = "/api/cashbook/payments";
        httpContext2.Request.Headers["Idempotency-Key"] = idempotencyKey;

        var executingContext2 = CreateActionExecutingContext(httpContext2, args);
        bool user2DelegateCalled = false;

        var executedContext2 = new ActionExecutedContext(
            executingContext2,
            new List<IFilterMetadata>(),
            executingContext2.Controller
        )
        {
            Result = new ObjectResult(new { Result = "User2 Success" }) { StatusCode = 200 }
        };

        await filter.OnActionExecutionAsync(executingContext2, () =>
        {
            user2DelegateCalled = true;
            return Task.FromResult(executedContext2);
        });

        // Do scope có UserId nên User 2 không bị nhận nhầm cache của User 1
        Assert.True(user2DelegateCalled);
    }

    [Fact]
    public async Task OnActionExecutionAsync_When5xxServerErrorOccurs_ShouldNotCacheAndAllowRetry()
    {
        var tenantContext = new FakeTenantContext();
        using var context = CreateDbContext(tenantContext);
        var filter = new IdempotencyFilter(context, tenantContext);

        var idempotencyKey = Guid.NewGuid().ToString();
        var args = new Dictionary<string, object?> { ["amount"] = 1_000_000m };

        // 1. Request thứ nhất: Server gặp sự cố hạ tầng / sập kết nối -> Trả về 500 Internal Server Error
        var httpContext1 = new DefaultHttpContext();
        httpContext1.Request.Method = "POST";
        httpContext1.Request.Path = "/api/cashbook/payments";
        httpContext1.Request.Headers["Idempotency-Key"] = idempotencyKey;

        var executingContext1 = CreateActionExecutingContext(httpContext1, args);
        var executedContext1 = new ActionExecutedContext(
            executingContext1,
            new List<IFilterMetadata>(),
            executingContext1.Controller
        )
        {
            Result = new ObjectResult(new { ErrorCode = "DATABASE_TIMEOUT", Message = "Lỗi kết nối cơ sở dữ liệu." }) { StatusCode = 500 }
        };

        await filter.OnActionExecutionAsync(executingContext1, () => Task.FromResult(executedContext1));

        // Kiểm chứng: Tuyệt đối KHÔNG cache phản hồi lỗi 500 vào IdempotencyRecords
        var recordInDb = await context.IdempotencyRecords.FirstOrDefaultAsync(r => r.Key.Contains(idempotencyKey));
        Assert.Null(recordInDb);

        // 2. Request thứ hai: Client retry cùng Idempotency-Key sau khi server đã phục hồi -> Delegate PHẢI được gọi lại và xử lý thành công 200
        var httpContext2 = new DefaultHttpContext();
        httpContext2.Request.Method = "POST";
        httpContext2.Request.Path = "/api/cashbook/payments";
        httpContext2.Request.Headers["Idempotency-Key"] = idempotencyKey;

        var executingContext2 = CreateActionExecutingContext(httpContext2, args);
        bool retryDelegateCalled = false;

        var executedContext2 = new ActionExecutedContext(
            executingContext2,
            new List<IFilterMetadata>(),
            executingContext2.Controller
        )
        {
            Result = new ObjectResult(new { VoucherCode = "PC-SUCCESS" }) { StatusCode = 200 }
        };

        await filter.OnActionExecutionAsync(executingContext2, () =>
        {
            retryDelegateCalled = true;
            return Task.FromResult(executedContext2);
        });

        Assert.True(retryDelegateCalled);

        // Sau khi thành công 200, record mới chính thức được lưu vào cache Idempotency
        var savedRecord = await context.IdempotencyRecords.FirstOrDefaultAsync(r => r.Key.Contains(idempotencyKey));
        Assert.NotNull(savedRecord);
        Assert.Equal(200, savedRecord.StatusCode);
    }

    [Fact]
    public async Task OnActionExecutionAsync_WhenConcurrentRequestWhilePending_ShouldReturn409ConflictInProgress()
    {
        var tenantContext = new FakeTenantContext();
        using var context = CreateDbContext(tenantContext);
        var filter = new IdempotencyFilter(context, tenantContext);

        var idempotencyKey = Guid.NewGuid().ToString();
        var args = new Dictionary<string, object?> { ["amount"] = 2_000_000m };

        // 1. Giả lập Request 1 đã ghi nhận bản ghi PENDING (đang trong quá trình trừ tiền két)
        var scopedKey = $"{tenantContext.CurrentUserId}:{idempotencyKey}";
        var pendingRecord = new IdempotencyRecord
        {
            TenantId = tenantContext.CurrentTenantId,
            UserId = tenantContext.CurrentUserId,
            Key = scopedKey,
            Endpoint = "POST:/api/cashbook/payments",
            PayloadHash = "some-hash",
            Status = "PENDING",
            StatusCode = 0,
            ResponseJson = string.Empty,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(5)
        };
        context.IdempotencyRecords.Add(pendingRecord);
        await context.SaveChangesAsync();

        // 2. Request 2 (bấm đúp hoặc song song) đến cùng Idempotency-Key
        var httpContext2 = new DefaultHttpContext();
        httpContext2.Request.Method = "POST";
        httpContext2.Request.Path = "/api/cashbook/payments";
        httpContext2.Request.Headers["Idempotency-Key"] = idempotencyKey;

        var executingContext2 = CreateActionExecutingContext(httpContext2, args);
        bool delegateExecuted = false;

        await filter.OnActionExecutionAsync(executingContext2, () =>
        {
            delegateExecuted = true;
            return Task.FromResult<ActionExecutedContext>(null!);
        });

        // Xác minh: Request 2 bị chặn đứng NGAY LẬP TỨC với 409 Conflict IDEMPOTENCY_IN_PROGRESS
        Assert.False(delegateExecuted);
        var conflictResult = Assert.IsType<ConflictObjectResult>(executingContext2.Result);
        Assert.Equal(409, conflictResult.StatusCode);
        var apiResp = Assert.IsType<ApiResponse<string>>(conflictResult.Value);
        Assert.Equal("IDEMPOTENCY_IN_PROGRESS", apiResp.ErrorCode);
    }

    [Fact]
    public async Task OnActionExecutionAsync_WhenPendingRecordExpiredDueToCrash_ShouldReclaimLockAndSucceed()
    {
        var tenantContext = new FakeTenantContext();
        using var context = CreateDbContext(tenantContext);
        var filter = new IdempotencyFilter(context, tenantContext);

        var idempotencyKey = Guid.NewGuid().ToString();
        var args = new Dictionary<string, object?> { ["amount"] = 300_000m };

        // 1. Giả lập một bản ghi PENDING bị treo quá 5 phút do server trước đó bị crash
        var scopedKey = $"{tenantContext.CurrentUserId}:{idempotencyKey}";
        var expiredPendingRecord = new IdempotencyRecord
        {
            TenantId = tenantContext.CurrentTenantId,
            UserId = tenantContext.CurrentUserId,
            Key = scopedKey,
            Endpoint = "POST:/api/cashbook/payments",
            PayloadHash = "stale-hash",
            Status = "PENDING",
            StatusCode = 0,
            ResponseJson = string.Empty,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-10) // Đã quá hạn 10 phút trước
        };
        context.IdempotencyRecords.Add(expiredPendingRecord);
        await context.SaveChangesAsync();

        // 2. Request mới gửi lại cùng Key -> Bộ lọc phải tự dọn bản ghi treo và cho phép chạy thành công
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "POST";
        httpContext.Request.Path = "/api/cashbook/payments";
        httpContext.Request.Headers["Idempotency-Key"] = idempotencyKey;

        var executingContext = CreateActionExecutingContext(httpContext, args);
        bool delegateExecuted = false;

        var executedContext = new ActionExecutedContext(
            executingContext,
            new List<IFilterMetadata>(),
            executingContext.Controller
        )
        {
            Result = new ObjectResult(new { VoucherCode = "RECOVERED-001" }) { StatusCode = 200 }
        };

        await filter.OnActionExecutionAsync(executingContext, () =>
        {
            delegateExecuted = true;
            return Task.FromResult(executedContext);
        });

        // Xác minh: Được thực thi và chuyển sang COMPLETED
        Assert.True(delegateExecuted);
        var activeRecord = await context.IdempotencyRecords.FirstOrDefaultAsync(r => r.Key == scopedKey);
        Assert.NotNull(activeRecord);
        Assert.Equal("COMPLETED", activeRecord.Status);
        Assert.Equal(200, activeRecord.StatusCode);
    }

    [Fact]
    public async Task OnActionExecutionAsync_WhenUnhandledExceptionOccurs_ShouldRemovePendingRecordAndAllowRetry()
    {
        var tenantContext = new FakeTenantContext();
        using var context = CreateDbContext(tenantContext);
        var filter = new IdempotencyFilter(context, tenantContext);

        var idempotencyKey = Guid.NewGuid().ToString();
        var args = new Dictionary<string, object?> { ["amount"] = 750_000m };

        var httpContext1 = new DefaultHttpContext();
        httpContext1.Request.Method = "POST";
        httpContext1.Request.Path = "/api/cashbook/payments";
        httpContext1.Request.Headers["Idempotency-Key"] = idempotencyKey;

        var executingContext1 = CreateActionExecutingContext(httpContext1, args);

        // 1. Lần đầu: Delegate ném Exception nghiêm trọng
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await filter.OnActionExecutionAsync(executingContext1, () =>
                throw new InvalidOperationException("Mất kết nối mạng đột ngột"));
        });

        // Bản ghi PENDING phải được dọn dẹp sạch sẽ
        var recordInDb = await context.IdempotencyRecords.FirstOrDefaultAsync(r => r.Key.Contains(idempotencyKey));
        Assert.Null(recordInDb);

        // 2. Client retry cùng Key -> Chạy lại bình thường
        var httpContext2 = new DefaultHttpContext();
        httpContext2.Request.Method = "POST";
        httpContext2.Request.Path = "/api/cashbook/payments";
        httpContext2.Request.Headers["Idempotency-Key"] = idempotencyKey;

        var executingContext2 = CreateActionExecutingContext(httpContext2, args);
        bool retryExecuted = false;

        var executedContext2 = new ActionExecutedContext(
            executingContext2,
            new List<IFilterMetadata>(),
            executingContext2.Controller
        )
        {
            Result = new ObjectResult(new { VoucherCode = "RETRY-OK" }) { StatusCode = 200 }
        };

        await filter.OnActionExecutionAsync(executingContext2, () =>
        {
            retryExecuted = true;
            return Task.FromResult(executedContext2);
        });

        Assert.True(retryExecuted);
        var finalRecord = await context.IdempotencyRecords.FirstOrDefaultAsync(r => r.Key.Contains(idempotencyKey));
        Assert.NotNull(finalRecord);
        Assert.Equal("COMPLETED", finalRecord.Status);
    }
}

