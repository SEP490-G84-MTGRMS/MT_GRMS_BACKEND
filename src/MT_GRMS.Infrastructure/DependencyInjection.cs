using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MT_GRMS.Application.Admin.Repositories;
using MT_GRMS.Application.Auth;
using MT_GRMS.Application.Batches;
using MT_GRMS.Application.Branches;
using MT_GRMS.Application.Cashbook;
using MT_GRMS.Application.Catalog;
using MT_GRMS.Application.Common;
using MT_GRMS.Application.Common.Interfaces;
using MT_GRMS.Application.HR.Employees;
using MT_GRMS.Application.HR.ShiftConfigs;
using MT_GRMS.Application.HR.AttendancePolicies;
using MT_GRMS.Application.HR.Payslips;
using MT_GRMS.Application.HR.Schedules;
using MT_GRMS.Application.Inventory;
using MT_GRMS.Application.PosOrders;
using MT_GRMS.Application.PurchaseOrders.Dispatch;
using MT_GRMS.Application.Suppliers;
using MT_GRMS.Infrastructure.Persistence;
using MT_GRMS.Infrastructure.Repositories;
using MT_GRMS.Infrastructure.Services;
using MT_GRMS.Infrastructure.Services.Printer;
using MT_GRMS.Infrastructure.Workers;

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
            (connectionString.StartsWith("postgres://") || connectionString.StartsWith("postgresql://")))
        {
            connectionString = ParsePostgreSqlUri(connectionString);
        }

        services.AddDbContext<AppDbContext>(options =>
        {
            options.ConfigureWarnings(w => w.Ignore(CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning));

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                options.UseInMemoryDatabase("MT_GRMS_DevDb");
            }
            else
            {
                options.UseNpgsql(connectionString, npgsqlOptions =>
                {
                    npgsqlOptions.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                    npgsqlOptions.EnableRetryOnFailure(
                        maxRetryCount: 3,
                        maxRetryDelay: TimeSpan.FromSeconds(5),
                        errorCodesToAdd: null);
                });
            }
        });

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.WriteLine("[MT-GRMS] Database: InMemory (không dùng Postgres local).");
        }
        else
        {
            Console.WriteLine("[MT-GRMS] Database: PostgreSQL local Host=localhost;Port=5432;Database=mtgrms_db");
        }

        // Đăng ký Unit of Work & Repositories
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped(typeof(IRepository<>), typeof(GenericRepository<>));

        // Đăng ký Repository cụ thể cho từng module
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IInventoryRepository, InventoryRepository>();
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<Application.PosOrders.ISalesOrderRepository, SalesOrderRepository>();
        services.AddScoped<Application.PosShifts.IShiftRepository, ShiftRepository>();
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<IProductUnitRepository, ProductUnitRepository>();
        services.AddScoped<IBatchRepository, BatchRepository>();
        services.AddScoped<ICashbookRepository, CashbookRepository>();
        services.AddScoped<Application.Expenses.IExpenseRepository, ExpenseRepository>();
        services.AddScoped<Application.Debt.ICustomerDebtRepository, CustomerDebtRepository>();
        services.AddScoped<Application.Reports.IPnLReportRepository, PnLReportRepository>();
        services.AddScoped<Application.Tax.IHouseholdTaxRepository, HouseholdTaxRepository>();
        services.AddScoped<Application.Tax.ITaxExportService, Services.TaxExportService>();
        services.AddScoped<Application.Tax.Security.IPinVerificationService, Services.PinVerificationService>();
        services.AddScoped<ISupplierRepository, SupplierRepository>();
        services.AddScoped<IBranchRepository, BranchRepository>();
        services.AddScoped<IBranchProductRepository, BranchProductRepository>();
        services.AddScoped<MT_GRMS.Application.PurchaseOrders.IPurchaseOrderRepository, PurchaseOrderRepository>();
        services.AddScoped<MT_GRMS.Application.GoodsReceipts.IGoodsReceiptRepository, GoodsReceiptRepository>();
        services.AddScoped<MT_GRMS.Application.SupplierReturns.ISupplierReturnRepository, SupplierReturnRepository>();
        services.AddScoped<MT_GRMS.Application.Stocktakes.IStocktakeRepository, StocktakeRepository>();
        services.AddScoped<Application.Promotions.IPromotionRepository, PromotionRepository>();
        services.AddScoped<IEmployeeRepository, EmployeeRepository>();
        services.AddScoped<IShiftConfigRepository, ShiftConfigRepository>();
        services.AddScoped<IBranchAttendancePolicyRepository, BranchAttendancePolicyRepository>();
        services.AddScoped<IScheduleRepository, ScheduleRepository>();
        services.AddScoped<IScheduleApprovalRequestRepository, ScheduleApprovalRequestRepository>();
        services.AddScoped<IPayslipRepository, PayslipRepository>();
        services.AddScoped<ISubscriptionPlanRepository, SubscriptionPlanRepository>();
        services.AddScoped<ITenantRepository, TenantRepository>();

        // Đăng ký dịch vụ máy in nhiệt hóa đơn POS-58 (khổ in 43mm siêu thị mini)
        services.AddScoped<IThermalPrintService, ThermalPrintService>();

        // Cấu hình & Đăng ký dịch vụ lưu trữ đám mây Cloudinary
        services.Configure<CloudinarySettings>(configuration.GetSection(CloudinarySettings.SectionName));
        services.AddScoped<ICloudinaryService, CloudinaryService>();

        // Supplier order Excel + email + local storage + package
        services.AddScoped<IFileStorage, LocalFileStorage>();
        services.AddScoped<ISupplierOrderExcelGenerator, SupplierOrderExcelGenerator>();
        services.AddScoped<ISupplierOrderEmailSender, SmtpSupplierOrderEmailSender>();
        services.AddScoped<ISupplierDispatchPackageService, SupplierDispatchPackageService>();
        services.AddHostedService<SupplierOrderDispatchWorker>();

        // Cấu hình SePay & Dịch vụ Ví gian hàng
        services.Configure<MT_GRMS.Application.Common.SePaySettings>(configuration.GetSection(MT_GRMS.Application.Common.SePaySettings.SectionName));
        services.AddScoped<MT_GRMS.Application.SaaS.Services.IWalletService, Services.WalletService>();

        // Cấu hình Email & Dịch vụ gửi thư điện tử OTP
        services.Configure<EmailSettings>(configuration.GetSection(EmailSettings.SectionName));
        services.AddScoped<IEmailService, EmailService>();

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
