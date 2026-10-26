using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using MT_GRMS.Application.Auth;
using MT_GRMS.Application.Batches;
using MT_GRMS.Application.Cashbook;
using MT_GRMS.Application.Catalog;
using MT_GRMS.Application.Inventory;
using MT_GRMS.Application.PosOrders;

namespace MT_GRMS.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        // 1. Tự động đăng ký toàn bộ FluentValidation Validators trong assembly
        services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());

        // 2. Đăng ký các Service nghiệp vụ theo từng chức năng (Feature Services)
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IInventoryService, InventoryService>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<ICatalogService, CatalogService>();
        services.AddScoped<IBatchService, BatchService>();
        services.AddScoped<ICashbookService, CashbookService>();
        services.AddScoped<Expenses.IExpenseService, Expenses.ExpenseService>();
        services.AddScoped<Debt.ICustomerDebtService, Debt.CustomerDebtService>();
                services.AddScoped<Reports.IPnLReportService, Reports.PnLReportService>();
        services.AddScoped<Tax.IHouseholdTaxService, Tax.HouseholdTaxService>();
        services.AddSingleton<Common.Services.IAuditHashService, Common.Services.AuditHashService>();
        services.AddScoped<Suppliers.ISupplierService, Suppliers.SupplierService>();
        services.AddScoped<AiPipeline.IAiRecommendationService, AiPipeline.AiRecommendationService>();

        return services;
    }
}


