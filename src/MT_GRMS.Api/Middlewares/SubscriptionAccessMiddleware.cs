using Microsoft.EntityFrameworkCore;
using MT_GRMS.Infrastructure.Persistence;

namespace MT_GRMS.Api.Middlewares;

public sealed class SubscriptionAccessMiddleware(RequestDelegate next)
{
    private static readonly string[] AllowedPrefixes =
    [
        "/api/auth", "/api/tenant/store-info", "/api/tenant/pricing-plans",
        "/api/tenant/subscriptions", "/api/tenant/wallet", "/api/webhooks/sepay", "/api/payments/sepay-webhook"
    ];

    public async Task InvokeAsync(HttpContext context, AppDbContext db)
    {
        if (context.User.Identity?.IsAuthenticated != true ||
            HttpMethods.IsOptions(context.Request.Method) ||
            AllowedPrefixes.Any(prefix => context.Request.Path.StartsWithSegments(prefix)))
        {
            await next(context);
            return;
        }

        var tenantId = context.User.FindFirst("tenant_id")?.Value;
        if (string.IsNullOrWhiteSpace(tenantId) || tenantId == "DEFAULT")
        {
            await next(context);
            return;
        }

        var tenant = await db.Tenants.AsNoTracking()
            .Where(x => x.Id == tenantId)
            .Select(x => new { x.SubscriptionStatus, x.SubscriptionExpiresAt, x.TrialExpiresAt })
            .SingleOrDefaultAsync(context.RequestAborted);

        var trialExpiry = tenant?.TrialExpiresAt ?? tenant?.SubscriptionExpiresAt;
        var blocked = tenant == null ||
            string.Equals(tenant.SubscriptionStatus, "NO_PLAN", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(tenant.SubscriptionStatus, "REVIEW_REQUIRED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(tenant.SubscriptionStatus, "EXPIRED", StringComparison.OrdinalIgnoreCase) ||
            (string.Equals(tenant.SubscriptionStatus, "TRIAL", StringComparison.OrdinalIgnoreCase) &&
             (!trialExpiry.HasValue || trialExpiry.Value <= DateTime.UtcNow)) ||
            (string.Equals(tenant.SubscriptionStatus, "ACTIVE", StringComparison.OrdinalIgnoreCase) &&
             tenant.SubscriptionExpiresAt.HasValue &&
             tenant.SubscriptionExpiresAt.Value <= DateTime.UtcNow);

        if (!blocked)
        {
            await next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await Results.Problem(
            statusCode: StatusCodes.Status403Forbidden,
            title: "Gói dịch vụ chưa khả dụng",
            detail: "Vui lòng bắt đầu dùng thử hoặc mua gói dịch vụ để sử dụng chức năng này.",
            extensions: new Dictionary<string, object?>
            {
                ["code"] = "SUBSCRIPTION_REQUIRED",
                ["traceId"] = context.TraceIdentifier
            }).ExecuteAsync(context);
    }
}
