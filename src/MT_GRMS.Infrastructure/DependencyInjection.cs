using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MT_GRMS.Application.Auth;
using MT_GRMS.Application.Batches;
using MT_GRMS.Application.Cashbook;
using MT_GRMS.Application.Catalog;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Inventory;
using MT_GRMS.Application.PosOrders;
using MT_GRMS.Application.Suppliers;
using MT_GRMS.Infrastructure.Persistence;
using MT_GRMS.Infrastructure.Repositories;

namespace MT_GRMS.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = configuration["DATABASE_URL"]
                ?? Environment.GetEnvironmentVariable("DATABASE_URL")
                ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
                ?? Environment.GetEnvironmentVariable("DB_CONNECTION_STRING");
        }

        if (!string.IsNullOrWhiteSpace(connectionString) &&
            (connectionString.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
             connectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase)))
        {
            connectionString = ParsePostgreSqlUri(connectionString);
        }

        // Cấu hình DbContext kết nối Database thật (Database-First)
        services.AddDbContext<AppDbContext>((serviceProvider, options) =>
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                // Fallback nếu chưa cấu hình chuỗi kết nối
                options.UseInMemoryDatabase("MT_GRMS_Db");
            }
            else if (connectionString.Contains("Host=", StringComparison.OrdinalIgnoreCase) ||
                     connectionString.Contains("Port=", StringComparison.OrdinalIgnoreCase))
            {
                // Kết nối cơ sở dữ liệu PostgreSQL (Npgsql)
                options.UseNpgsql(connectionString, b =>
                {
                    b.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                });
            }
            else
            {
                // Kết nối cơ sở dữ liệu Microsoft SQL Server
                options.UseSqlServer(connectionString, b =>
                {
                    b.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                });
            }
        });

        // Đăng ký Unit of Work & Repositories
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped(typeof(IRepository<>), typeof(GenericRepository<>));

        // Đăng ký Repository cụ thể cho từng module
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IInventoryRepository, InventoryRepository>();
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IBatchRepository, BatchRepository>();
        services.AddScoped<ICashbookRepository, CashbookRepository>();
        services.AddScoped<Application.Expenses.IExpenseRepository, ExpenseRepository>();
        services.AddScoped<Application.Debt.ICustomerDebtRepository, CustomerDebtRepository>();
                services.AddScoped<Application.Reports.IPnLReportRepository, PnLReportRepository>();
        services.AddScoped<Application.Tax.IHouseholdTaxRepository, HouseholdTaxRepository>();
        services.AddScoped<Application.Tax.Security.IPinVerificationService, Services.PinVerificationService>();
        services.AddScoped<ISupplierRepository, SupplierRepository>();

        return services;
    }

    private static string ParsePostgreSqlUri(string uriString)
    {
        try
        {
            var uri = new Uri(uriString);
            var userInfo = uri.UserInfo.Split(':');
            var username = userInfo.Length > 0 ? Uri.UnescapeDataString(userInfo[0]) : "";
            var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "";
            var host = uri.Host;
            var port = uri.Port > 0 ? uri.Port : 5432;
            var database = uri.AbsolutePath.Trim('/');

            return $"Host={host};Port={port};Database={database};Username={username};Password={password};SSL Mode=Require;Trust Server Certificate=true;";
        }
        catch
        {
            return uriString;
        }
    }
}