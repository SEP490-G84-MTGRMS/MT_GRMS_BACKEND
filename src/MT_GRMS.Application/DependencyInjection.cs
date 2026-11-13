using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using MT_GRMS.Application.Admin.Services;
using MT_GRMS.Application.Auth;
using MT_GRMS.Application.Batches;
using MT_GRMS.Application.Cashbook;
using MT_GRMS.Application.Catalog;
using MT_GRMS.Application.HR.Employees;
using MT_GRMS.Application.HR.ShiftConfigs;
using MT_GRMS.Application.HR.AttendancePolicies;
using MT_GRMS.Application.HR.Schedules;
using MT_GRMS.Application.Inventory;
using MT_GRMS.Application.PosOrders;
using MT_GRMS.Application.Repacks;

namespace MT_GRMS.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        // 1. Tự động đăng ký toàn bộ FluentValidation Validators trong assembly
        services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());

        // 2. Đăng ký các Service nghiệp vụ theo từng chức năng (Feature Services)
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IInventoryService, InventoryService>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<PosShifts.IShiftService, PosShifts.ShiftService>();
        services.AddScoped<ICatalogService, CatalogService>();
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<IProductUnitService, ProductUnitService>();
        services.AddScoped<IBatchService, BatchService>();
        services.AddScoped<ICashbookService, CashbookService>();
        services.AddScoped<Expenses.IExpenseService, Expenses.ExpenseService>();
        services.AddScoped<Debt.ICustomerDebtService, Debt.CustomerDebtService>();
        services.AddScoped<Reports.IPnLReportService, Reports.PnLReportService>();
        services.AddScoped<Tax.IHouseholdTaxService, Tax.HouseholdTaxService>();
        services.AddScoped<Tax.Strategies.ITaxCalculationStrategy, Tax.Strategies.RevenuePercentageTaxStrategy>();
        services.AddScoped<Tax.Strategies.ITaxCalculationStrategy, Tax.Strategies.DeclarationBasedTaxStrategy>();
        services.AddScoped<Tax.Strategies.ITaxCalculationStrategyFactory, Tax.Strategies.TaxCalculationStrategyFactory>();
        services.AddSingleton<Tax.Security.ITaxSnapshotSignatureService, Tax.Security.TaxSnapshotSignatureService>();
        services.AddSingleton<Common.Services.IAuditHashService, Common.Services.AuditHashService>();
        services.AddScoped<Suppliers.ISupplierService, Suppliers.SupplierService>();
        services.AddScoped<Branches.IBranchService, Branches.BranchService>();
        services.AddScoped<Branches.IBranchProductService, Branches.BranchProductService>();
        services.AddScoped<PurchaseOrders.IPurchaseOrderService, PurchaseOrders.PurchaseOrderService>();
        services.AddScoped<GoodsReceipts.IGoodsReceiptService, GoodsReceipts.GoodsReceiptService>();
        services.AddScoped<SupplierReturns.ISupplierReturnService, SupplierReturns.SupplierReturnService>();
        services.AddScoped<Stocktakes.IStocktakeService, Stocktakes.StocktakeService>();
        services.AddScoped<IRepackService, RepackService>();
        services.AddScoped<Promotions.IPromotionService, Promotions.PromotionService>();
        services.AddScoped<Promotions.ILoyaltyService, Promotions.LoyaltyService>();
        services.AddScoped<IEmployeeService, EmployeeService>();
        services.AddScoped<IShiftConfigService, ShiftConfigService>();
        services.AddScoped<IBranchAttendancePolicyService, BranchAttendancePolicyService>();
        services.AddScoped<IScheduleService, ScheduleService>();
        services.AddScoped<IScheduleApprovalService, ScheduleApprovalService>();
        services.AddScoped<HR.Payslips.IPayslipService, HR.Payslips.PayslipService>();

        // 3. Đăng ký các Service cho Master Admin Portal
        services.AddScoped<ISubscriptionPlanService, SubscriptionPlanService>();
        services.AddScoped<ITenantService, TenantService>();
        services.AddScoped<IAdminDashboardService, AdminDashboardService>();

        return services;
    }
}
