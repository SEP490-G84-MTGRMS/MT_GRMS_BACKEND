using System.Net;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using MT_GRMS.Api.Common;
using MT_GRMS.Api.Middlewares;
using MT_GRMS.Application;
using MT_GRMS.Application.Common;
using MT_GRMS.Infrastructure;
using MT_GRMS.Infrastructure.Persistence;

// 0. Nạp biến môi trường từ file .env (nếu có)
EnvLoader.Load();

AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var resetDemo = args.Contains("--reset-demo");
var confirmReset = args.Contains("--confirm-reset");
var seedDemoOnly = args.Contains("--seed-demo-only");
var configArgs = args.Where((arg, index) =>
    arg is not ("--reset-demo" or "--confirm-reset" or "--seed-demo-only") &&
    !(arg == "true" && index > 0 && args[index - 1] == "--seed-demo-only")).ToArray();
var builder = WebApplication.CreateBuilder(configArgs);
if (SePayTestMode.IsEnabled(builder.Configuration) && !builder.Environment.IsDevelopment())
    throw new InvalidOperationException("SePay Test Mode chỉ được bật cho backend Development.");

// 1. Thêm Controllers & Cấu hình Swagger
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.Converters.Add(new UtcDateTimeJsonConverter());
        options.JsonSerializerOptions.Converters.Add(new UtcNullableDateTimeJsonConverter());
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddHttpClient();
builder.Services.AddSwaggerGen(options =>
{
    options.CustomSchemaIds(type => type.FullName?.Replace("+", ".") ?? type.Name);
});

MT_GRMS.Api.Authorization.AuthenticationRegistration.AddSessionAuthentication(builder.Services, builder.Configuration);

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("PlatformAdminOnly", policy =>
        policy.RequireRole("ADMIN")
              .RequireAssertion(ctx => string.IsNullOrEmpty(ctx.User.FindFirst("tenant_id")?.Value)));

    options.AddPolicy("StoreOwnerOnly", policy =>
        policy.RequireRole("STORE_OWNER")
              .RequireAssertion(ctx => !string.IsNullOrEmpty(ctx.User.FindFirst("tenant_id")?.Value)));

    options.AddPolicy("TenantOwnerOrAssignedBranchStaff", policy =>
        policy.RequireRole("STORE_OWNER", "STAFF")
              .RequireAssertion(ctx => !string.IsNullOrEmpty(ctx.User.FindFirst("tenant_id")?.Value))
              .AddRequirements(new MT_GRMS.Api.Authorization.BranchScopedOperationRequirement("BranchResourceAccess")));
});

builder.Services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, MT_GRMS.Api.Authorization.BranchScopedResourceAuthorizationHandler>();
builder.Services.AddDataProtection();

// 3. Đăng ký Scoped Context đa người thuê
builder.Services.AddScoped<ITenantContext, TenantContext>();

// 4. Đăng ký các tầng Application & Infrastructure
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration, allowInMemory: !builder.Environment.IsProduction());
builder.Services.AddHealthChecks().AddCheck<DatabaseReadinessCheck>("database", timeout: TimeSpan.FromSeconds(5));
builder.Services.AddSignalR();
builder.Services.AddScoped<MT_GRMS.Application.Notifications.INotificationRealtimeDispatcher, MT_GRMS.Api.Hubs.SignalRNotificationDispatcher>();

// 5. Cấu hình CORS (cho phép frontend gọi API từ localhost bất kỳ port nào)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.SetIsOriginAllowed(origin =>
        {
            if (string.IsNullOrWhiteSpace(origin)) return false;
            try
            {
                var uri = new Uri(origin);
                return uri.Host == "localhost" || uri.Host == "127.0.0.1";
            }
            catch
            {
                return false;
            }
        })
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    var configuredProxies = builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>();
    if (configuredProxies is { Length: > 0 })
    {
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
        foreach (var proxy in configuredProxies)
        {
            if (IPAddress.TryParse(proxy, out var ip))
            {
                options.KnownProxies.Add(ip);
            }
        }
    }
});

var app = builder.Build();

// 6. Cấu hình Swagger (Bật ở cả Development và Production để test API trên Render/Cloud)
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "MT_GRMS API v1");
    c.RoutePrefix = "swagger";
});

// Chuyển hướng trang chủ "/" sang "/swagger" để khi vào domain là mở Swagger ngay
app.MapGet("/", () => Results.Redirect("/swagger"));

app.UseForwardedHeaders();

// 7. Cấu hình HTTP Pipeline theo thứ tự chuẩn
app.UseMiddleware<ExceptionHandlingMiddleware>();

// CORS đặt trước để xử lý preflight OPTIONS request tránh 307 Redirect
app.UseCors("AllowFrontend");

app.UseRouting();

// Middleware xác thực & cô lập Tenant
app.UseAuthentication();
app.UseMiddleware<MultiTenantMiddleware>();
app.UseMiddleware<SubscriptionAccessMiddleware>();
app.UseAuthorization();

app.MapControllers();
app.MapHub<MT_GRMS.Api.Hubs.NotificationHub>("/hubs/notifications");

// 8. Tự động áp dụng Migration (Auto-migration); Seed mẫu chỉ khi Database:SeedSampleData=true
var skipDbInit = app.Configuration.GetValue<bool>("Database:SkipDbInitialization");
if (resetDemo || confirmReset)
{
    if (!resetDemo || !confirmReset || seedDemoOnly || !app.Environment.IsDevelopment())
        throw new InvalidOperationException("Reset demo yêu cầu Development và cả --reset-demo --confirm-reset.");

    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await DbInitializer.ResetDemoAsync(db);
    app.Logger.LogInformation("Đã dựng lại schema và nạp dữ liệu mẫu vào mtgrms_demo.");
    return;
}

if (seedDemoOnly && skipDbInit)
    throw new InvalidOperationException("Database:SkipDbInitialization đang bật.");

if (app.Configuration.GetValue<bool>("Database:RepairTransferStockColumns"))
{
    using var repairScope = app.Services.CreateScope();
    var repairDb = repairScope.ServiceProvider.GetRequiredService<AppDbContext>();
    await repairDb.Database.ExecuteSqlRawAsync("ALTER TABLE \"BranchStocks\" ADD COLUMN IF NOT EXISTS \"FefoDiscountPercent\" numeric(5,2) NOT NULL DEFAULT 0;");
    await repairDb.Database.ExecuteSqlRawAsync("ALTER TABLE \"BranchStocks\" ADD COLUMN IF NOT EXISTS \"IsFefoDiscountActive\" boolean NOT NULL DEFAULT false;");
    await repairDb.Database.ExecuteSqlRawAsync("ALTER TABLE IF EXISTS \"Transfers\" ADD COLUMN IF NOT EXISTS \"RowVersion\" integer NOT NULL DEFAULT 1;");
}

if (app.Environment.IsDevelopment() && app.Configuration.GetValue<bool>("Database:RepairLocalSchema"))
{
    using var repairScope = app.Services.CreateScope();
    var repairDb = repairScope.ServiceProvider.GetRequiredService<AppDbContext>();
    await DbInitializer.ApplyLocalSchemaRepairsAsync(repairDb);
}

if (!skipDbInit)
{
    using var scope = app.Services.CreateScope();
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();
    var startupInitializationTimer = System.Diagnostics.Stopwatch.StartNew();
    try
    {
        var dbContext = services.GetRequiredService<AppDbContext>();
        if (seedDemoOnly)
        {
            if (!app.Environment.IsDevelopment())
                throw new InvalidOperationException("Seed demo chỉ hỗ trợ môi trường Development.");
            await DbInitializer.SeedDemoAsync(dbContext);
            var demoCount = await dbContext.Tenants.CountAsync(t => t.Id.StartsWith("thviet-demo-"));
            var branchCount = await dbContext.Branches.IgnoreQueryFilters().CountAsync(b => b.TenantId.StartsWith("thviet-demo-"));
            var employeeCount = await dbContext.Employees.IgnoreQueryFilters().CountAsync(e => e.TenantId.StartsWith("thviet-demo-"));
            var orderCount = await dbContext.Orders.IgnoreQueryFilters().CountAsync(o => o.TenantId.StartsWith("thviet-demo-"));
            logger.LogInformation("Demo THViet: {Tenants} tenant, {Branches} chi nhánh, {Employees} nhân viên, {Orders} hóa đơn. Dữ liệu hiện có được giữ nguyên.", demoCount, branchCount, employeeCount, orderCount);
            return;
        }
        var seedSampleData = app.Configuration.GetValue<bool?>("Database:SeedSampleData")
            ?? app.Configuration.GetValue<bool?>("Database:SeedDemoData")
            ?? app.Environment.IsDevelopment();

        logger.LogInformation(
            "Initializing database schema{0}...",
            seedSampleData ? " and seed data" : " (SeedSampleData=false)");
        await DbInitializer.InitializeAsync(dbContext, logger, seedSampleData);
        logger.LogInformation(
            seedSampleData
                ? "Database seeded successfully in {0} ms."
                : "Database schema sync completed in {0} ms (sample seed skipped).",
            startupInitializationTimer.ElapsedMilliseconds);
    }
    catch (Exception ex) when (ex is not WalletTopupSchemaMigrationException && ex is not HouseholdTaxSnapshotSchemaException && !seedDemoOnly && !app.Environment.IsProduction())
    {
        logger.LogError(ex, "Lỗi khi tự động áp dụng schema hoặc nạp dữ liệu mẫu vào CSDL sau {ElapsedMilliseconds} ms.", startupInitializationTimer.ElapsedMilliseconds);
    }
}

app.Run();
