using Microsoft.EntityFrameworkCore;
using MT_GRMS.Api.Middlewares;
using MT_GRMS.Application;
using MT_GRMS.Application.Common;
using MT_GRMS.Infrastructure;
using MT_GRMS.Api.Common;
using MT_GRMS.Infrastructure.Persistence;

// 0. Nạp biến môi trường từ file .env (nếu có)
EnvLoader.Load();

var builder = WebApplication.CreateBuilder(args);

// 1. Thêm Controllers & Swagger
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// 2. Đăng ký Scoped Context đa người thuê
builder.Services.AddScoped<ITenantContext, TenantContext>();

// 3. Đăng ký các tầng Application & Infrastructure
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);

// 4. Cấu hình CORS (cho phép frontend gọi API)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins("http://localhost:5173", "http://localhost:3000")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

var app = builder.Build();

// 5. Tự động áp dụng Migration (Auto-migration) và Nạp dữ liệu mẫu (Seeder) mỗi lần khởi động
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();
    try
    {
        var dbContext = services.GetRequiredService<AppDbContext>();
        if (dbContext.Database.IsRelational())
        {
            logger.LogInformation("Applying EF Core migrations to Neon PostgreSQL database...");
            await dbContext.Database.MigrateAsync();
            logger.LogInformation("Database migrations applied successfully.");
        }

        logger.LogInformation("Seeding sample data into database...");
        await DatabaseSeeder.SeedAsync(dbContext, logger);
        logger.LogInformation("Database seeded successfully.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred while applying migrations or seeding the database.");
    }
}

// 6. Cấu hình Swagger trong môi trường Development
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "MT_GRMS API v1");
        c.RoutePrefix = "swagger";
    });
}

// 7. Cấu hình HTTP Pipeline theo thứ tự chuẩn
app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseHttpsRedirection();
app.UseCors("AllowFrontend");

app.UseRouting();

// Middleware xác thực & cô lập Tenant
app.UseAuthentication();
app.UseMiddleware<MultiTenantMiddleware>();
app.UseAuthorization();

app.MapControllers();

app.Run();