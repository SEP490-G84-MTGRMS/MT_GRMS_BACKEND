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
        services.AddScoped<Suppliers.ISupplierService, Suppliers.SupplierService>();
        services.AddScoped<Branches.IBranchService, Branches.BranchService>();
        services.AddScoped<Branches.IBranchProductService, Branches.BranchProductService>();
        services.AddScoped<PurchaseOrders.IPurchaseOrderService, PurchaseOrders.PurchaseOrderService>();
        services.AddScoped<GoodsReceipts.IGoodsReceiptService, GoodsReceipts.GoodsReceiptService>();
        services.AddScoped<SupplierReturns.ISupplierReturnService, SupplierReturns.SupplierReturnService>();
        services.AddScoped<AiPipeline.IAiRecommendationService, AiPipeline.AiRecommendationService>();

        return services;
    }
}
