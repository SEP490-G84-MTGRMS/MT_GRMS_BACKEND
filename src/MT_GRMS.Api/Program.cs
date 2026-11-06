using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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

var builder = WebApplication.CreateBuilder(args);

// 1. Thêm Controllers & Cấu hình Swagger
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.CustomSchemaIds(type => type.FullName?.Replace("+", ".") ?? type.Name);
});

// 2. Cấu hình JWT Bearer Authentication
var jwtKey = builder.Configuration["Jwt:Key"] ?? "MT_GRMS_Super_Secret_Key_For_JWT_Authentication_2026_FPTU_Capstone_Project!";
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "MTGRMS_API";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "MTGRMS_CLIENT";

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false;
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        ValidateIssuer = true,
        ValidIssuer = jwtIssuer,
        ValidateAudience = true,
        ValidAudience = jwtAudience,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero
    };
});

builder.Services.AddAuthorization();

// 3. Đăng ký Scoped Context đa người thuê
builder.Services.AddScoped<ITenantContext, TenantContext>();

// 4. Đăng ký các tầng Application & Infrastructure
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);

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

// 7. Cấu hình HTTP Pipeline theo thứ tự chuẩn
app.UseMiddleware<ExceptionHandlingMiddleware>();

// CORS đặt trước để xử lý preflight OPTIONS request tránh 307 Redirect
app.UseCors("AllowFrontend");

app.UseRouting();

// Middleware xác thực & cô lập Tenant
app.UseAuthentication();
app.UseMiddleware<MultiTenantMiddleware>();
app.UseAuthorization();

app.MapControllers();

// 8. Tự động áp dụng Migration (Auto-migration) và Nạp dữ liệu mẫu (Seeder) mỗi lần khởi động
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();
    try
    {
        var dbContext = services.GetRequiredService<AppDbContext>();

        logger.LogInformation("Initializing database schema and seed data...");
        await DbInitializer.InitializeAsync(dbContext, logger);
        logger.LogInformation("Database seeded successfully.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Lỗi khi tự động áp dụng migrations hoặc nạp dữ liệu mẫu vào CSDL.");
    }
}

app.Run();
