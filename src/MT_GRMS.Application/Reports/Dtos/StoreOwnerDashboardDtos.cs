namespace MT_GRMS.Application.Reports.Dtos;

public record DashboardBranchDto(Guid Id, string Name);
public record DashboardScopeDto(DateTime FromUtc, DateTime ToUtc, DateTime AsOfUtc, Guid? BranchId, string BranchName);
public record DashboardRevenueTrendPointDto(DateTime Date, decimal Revenue);
public record DashboardSalesDto(decimal NetRevenue, int TotalOrders, decimal AverageOrderValue, decimal? RevenueGrowthPercent, IReadOnlyList<DashboardRevenueTrendPointDto> Trend, IReadOnlyList<TopProductPnLDto> TopProducts);
public record DashboardFinanceDto(decimal GrossProfit, decimal OperatingExpenses, decimal NetProfit, string CogsSource, string? CogsWarningNotice);
public record DashboardProcurementDto(int PendingApprovalCount, int AwaitingReceiptCount, decimal ReceivedAmountInPeriod);
public record DashboardStockItemDto(Guid ProductId, Guid BranchId, string ProductName, string BranchName, decimal AvailableQty, decimal MinStock, string Unit);
public record DashboardExpiryItemDto(Guid ProductId, Guid BranchId, string ProductName, string BranchName, string? BatchCode, DateTime ExpiryDate, decimal QuantityOnHand);
public record DashboardInventoryDto(int LowStockCount, int OutOfStockCount, int ExpiryWarningCount, IReadOnlyList<DashboardStockItemDto> LowStockItems, IReadOnlyList<DashboardExpiryItemDto> ExpiringBatches);
public record DashboardEmployeesDto(int TotalCount, int ActiveCount);
public record StoreOwnerDashboardResponse(DashboardScopeDto Scope, IReadOnlyList<DashboardBranchDto> Branches, DashboardSalesDto Sales, DashboardFinanceDto Finance, DashboardProcurementDto Procurement, DashboardInventoryDto Inventory, DashboardEmployeesDto Employees);
