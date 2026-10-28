using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using MT_GRMS.Api.Middlewares;
using MT_GRMS.Application;
using MT_GRMS.Application.Common;
using MT_GRMS.Infrastructure;

AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);

// 1. Thêm Controllers & Cấu hình Swagger
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

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

// 5. Cấu hình CORS (cho phép frontend gọi API)
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

// 8. Tự động khởi tạo Database và nạp dữ liệu mẫu (Code-First Migration & Seed Data)
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<MT_GRMS.Infrastructure.Persistence.AppDbContext>();
        await MT_GRMS.Infrastructure.Persistence.DbInitializer.InitializeAsync(context);
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "Lỗi khi tự động khởi tạo CSDL hoặc nạp Seed Data.");
    }
}

app.Run();
