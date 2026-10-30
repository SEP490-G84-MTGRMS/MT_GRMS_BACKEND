using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MT_GRMS.Application.Auth;
using MT_GRMS.Application.Batches;
using MT_GRMS.Application.Branches;
using MT_GRMS.Application.Cashbook;
using MT_GRMS.Application.Catalog;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Inventory;
using MT_GRMS.Application.PosOrders;
using MT_GRMS.Application.Suppliers;
using MT_GRMS.Infrastructure.Persistence;
using MT_GRMS.Infrastructure.Repositories;
using MT_GRMS.Application.Common.Interfaces;
using MT_GRMS.Infrastructure.Services;

namespace MT_GRMS.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");

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
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<IBatchRepository, BatchRepository>();
        services.AddScoped<ICashbookRepository, CashbookRepository>();
        services.AddScoped<ISupplierRepository, SupplierRepository>();
        services.AddScoped<IBranchRepository, BranchRepository>();

                // Cấu hình & Đăng ký dịch vụ lưu trữ đám mây Cloudinary
        services.Configure<CloudinarySettings>(configuration.GetSection(CloudinarySettings.SectionName));
        services.AddScoped<ICloudinaryService, CloudinaryService>();

        return services;
    }
}
