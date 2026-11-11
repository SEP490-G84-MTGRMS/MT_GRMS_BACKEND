using Order = MT_GRMS.Domain.Entities.Sales.Order;
using OrderItem = MT_GRMS.Domain.Entities.Sales.OrderDetail;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using MT_GRMS.Domain.Enums;
using Promotion = MT_GRMS.Domain.Entities.Sales.Promotion;
using PromotionCondition = MT_GRMS.Domain.Entities.Sales.PromotionCondition;
using VoucherCode = MT_GRMS.Domain.Entities.Sales.VoucherCode;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using MT_GRMS.Application.Auth;
using MT_GRMS.Domain.Common;
using MT_GRMS.Domain.Constants;
using MT_GRMS.Domain.Entities.Catalog;
using MT_GRMS.Domain.Entities.Finance;
using MT_GRMS.Domain.Entities.HR;
using MT_GRMS.Domain.Entities.Identity;
using MT_GRMS.Domain.Entities.Inventory;
using MT_GRMS.Domain.Entities.SaaS;
using MT_GRMS.Domain.Entities.Sales;
using MT_GRMS.Domain.Entities.Supplier;

namespace MT_GRMS.Infrastructure.Persistence;

/// <summary>
/// Lớp hỗ trợ Code-First: Tự động khởi tạo cấu trúc CSDL và nạp dữ liệu mẫu ban đầu (Seed Data).
/// </summary>
public static class DbInitializer
{
    private static async Task EnsureSupplierDispatchSchemaAsync(AppDbContext context, ILogger? logger = null)
    {
        // 1. Tạo các bảng độc lập trước (CREATE TABLE IF NOT EXISTS)
        try
        {
            await context.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS ""SupplierDispatchPackages"" (
    ""Id"" uuid NOT NULL PRIMARY KEY,
    ""TenantId"" character varying(50) NOT NULL,
    ""DispatchBatchId"" uuid NOT NULL,
    ""SupplierId"" uuid NOT NULL,
    ""SupplierCodeSnapshot"" character varying(50) NOT NULL,
    ""SupplierNameSnapshot"" character varying(250) NOT NULL,
    ""EmailTo"" character varying(200) NOT NULL,
    ""EmailCcJson"" text NOT NULL DEFAULT '[]',
    ""EmailSendMode"" character varying(30) NOT NULL,
    ""ScheduledSendAtUtc"" timestamp without time zone NULL,
    ""EmailStatus"" character varying(30) NOT NULL,
    ""GenerationStatus"" character varying(30) NOT NULL,
    ""FileName"" character varying(255) NULL,
    ""FileStorageKey"" character varying(500) NULL,
    ""FileSize"" bigint NULL,
    ""FileChecksum"" character varying(128) NULL,
    ""AttemptCount"" integer NOT NULL DEFAULT 0,
    ""LastError"" character varying(2000) NULL,
    ""GeneratedAtUtc"" timestamp without time zone NULL,
    ""SentAtUtc"" timestamp without time zone NULL,
    ""Note"" character varying(500) NULL,
    ""IdempotencyKey"" character varying(100) NULL,
    ""ProcessingLeaseUntilUtc"" timestamp without time zone NULL,
    ""ProcessingLockedBy"" character varying(100) NULL,
    ""CreatedBy"" uuid NULL,
    ""CreatedAt"" timestamp without time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
    ""UpdatedAt"" timestamp without time zone NULL,
    ""IsDeleted"" boolean NOT NULL DEFAULT false
);

CREATE TABLE IF NOT EXISTS ""SupplierDispatchPackageItems"" (
    ""Id"" uuid NOT NULL PRIMARY KEY,
    ""TenantId"" character varying(50) NOT NULL,
    ""PackageId"" uuid NOT NULL,
    ""PurchaseOrderId"" uuid NOT NULL,
    ""CreatedBy"" uuid NULL,
    ""CreatedAt"" timestamp without time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
    ""UpdatedAt"" timestamp without time zone NULL,
    ""IsDeleted"" boolean NOT NULL DEFAULT false
);

CREATE TABLE IF NOT EXISTS ""PurchaseOrderRevisions"" (
    ""Id"" uuid NOT NULL PRIMARY KEY,
    ""TenantId"" character varying(50) NOT NULL,
    ""PurchaseOrderId"" uuid NOT NULL,
    ""RevisionNumber"" integer NOT NULL,
    ""PreviousDataSnapshotJson"" text NULL,
    ""NewDataSnapshotJson"" text NULL,
    ""ChangedByUserId"" character varying(50) NULL,
    ""ChangedAtUtc"" timestamp without time zone NOT NULL,
    ""ChangeReason"" character varying(500) NULL,
    ""CreatedBy"" uuid NULL,
    ""CreatedAt"" timestamp without time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
    ""UpdatedAt"" timestamp without time zone NULL,
    ""IsDeleted"" boolean NOT NULL DEFAULT false
);

CREATE TABLE IF NOT EXISTS ""OutboxMessages"" (
    ""Id"" uuid NOT NULL PRIMARY KEY,
    ""TenantId"" character varying(50) NOT NULL,
    ""Type"" character varying(100) NOT NULL,
    ""PayloadJson"" text NOT NULL,
    ""IdempotencyKey"" character varying(120) NOT NULL,
    ""Status"" character varying(30) NOT NULL,
    ""AttemptCount"" integer NOT NULL DEFAULT 0,
    ""NextAttemptAtUtc"" timestamp without time zone NULL,
    ""ProcessedAtUtc"" timestamp without time zone NULL,
    ""LockedUntilUtc"" timestamp without time zone NULL,
    ""LockedBy"" character varying(100) NULL,
    ""LastError"" character varying(2000) NULL,
    ""CreatedBy"" uuid NULL,
    ""CreatedAt"" timestamp without time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
    ""UpdatedAt"" timestamp without time zone NULL,
    ""IsDeleted"" boolean NOT NULL DEFAULT false
);
");
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Lỗi khi tạo các bảng SupplierDispatch / OutboxMessages");
        }

        // 2. Bổ sung các cột mới vào các bảng đã tồn tại
        try
        {
            await context.Database.ExecuteSqlRawAsync(@"
ALTER TABLE IF EXISTS ""PurchaseOrders"" ADD COLUMN IF NOT EXISTS ""DispatchPackageId"" uuid NULL;
ALTER TABLE IF EXISTS ""PurchaseOrders"" ADD COLUMN IF NOT EXISTS ""DispatchBatchId"" uuid NULL;
ALTER TABLE IF EXISTS ""PurchaseOrders"" ADD COLUMN IF NOT EXISTS ""SupplierCodeSnapshot"" character varying(50) NULL;
ALTER TABLE IF EXISTS ""PurchaseOrders"" ADD COLUMN IF NOT EXISTS ""SupplierNameSnapshot"" character varying(250) NULL;
ALTER TABLE IF EXISTS ""PurchaseOrders"" ADD COLUMN IF NOT EXISTS ""SupplierEmailSnapshot"" character varying(200) NULL;
ALTER TABLE IF EXISTS ""PurchaseOrders"" ADD COLUMN IF NOT EXISTS ""StoreCodeSnapshot"" character varying(50) NULL;
ALTER TABLE IF EXISTS ""PurchaseOrders"" ADD COLUMN IF NOT EXISTS ""StoreNameSnapshot"" character varying(250) NULL;
ALTER TABLE IF EXISTS ""PurchaseOrders"" ADD COLUMN IF NOT EXISTS ""StoreAddressSnapshot"" character varying(500) NULL;
ALTER TABLE IF EXISTS ""PurchaseOrders"" ADD COLUMN IF NOT EXISTS ""StorePhoneSnapshot"" character varying(50) NULL;

ALTER TABLE IF EXISTS ""PurchaseOrderDetails"" ADD COLUMN IF NOT EXISTS ""ProductCodeSnapshot"" character varying(50) NULL;
ALTER TABLE IF EXISTS ""PurchaseOrderDetails"" ADD COLUMN IF NOT EXISTS ""ProductNameSnapshot"" character varying(250) NULL;
ALTER TABLE IF EXISTS ""PurchaseOrderDetails"" ADD COLUMN IF NOT EXISTS ""PurchaseUnitNameSnapshot"" character varying(50) NULL;

ALTER TABLE IF EXISTS ""PurchaseOrders"" ADD COLUMN IF NOT EXISTS ""RevisionNumber"" integer NOT NULL DEFAULT 1;
ALTER TABLE IF EXISTS ""PurchaseOrders"" ADD COLUMN IF NOT EXISTS ""LastAmendedAtUtc"" timestamp without time zone NULL;
ALTER TABLE IF EXISTS ""PurchaseOrders"" ADD COLUMN IF NOT EXISTS ""LastAmendedByUserId"" character varying(50) NULL;

ALTER TABLE IF EXISTS ""SupplierDispatchPackages"" ADD COLUMN IF NOT EXISTS ""PackageRevisionNumber"" integer NOT NULL DEFAULT 1;
ALTER TABLE IF EXISTS ""SupplierDispatchPackages"" ADD COLUMN IF NOT EXISTS ""SupersededByPackageId"" uuid NULL;

ALTER TABLE IF EXISTS ""SupplierBranch"" ADD COLUMN IF NOT EXISTS ""DeliveryDaysJson"" character varying(500) NULL;
ALTER TABLE IF EXISTS ""SupplierBranch"" ADD COLUMN IF NOT EXISTS ""DeliveryWeekdaysMask"" integer NULL;

ALTER TABLE IF EXISTS ""Suppliers"" ADD COLUMN IF NOT EXISTS ""CutoffDayOffset"" integer NOT NULL DEFAULT 1;
ALTER TABLE IF EXISTS ""Suppliers"" ADD COLUMN IF NOT EXISTS ""CutoffTime"" character varying(10) NOT NULL DEFAULT '17:00';
ALTER TABLE IF EXISTS ""Suppliers"" ADD COLUMN IF NOT EXISTS ""DeliveryWeekdaysMask"" integer NOT NULL DEFAULT 127;
ALTER TABLE IF EXISTS ""Suppliers"" ADD COLUMN IF NOT EXISTS ""DeliveryDaysJson"" text NOT NULL DEFAULT '[""Mon"",""Tue"",""Wed"",""Thu"",""Fri"",""Sat""]';
ALTER TABLE IF EXISTS ""Suppliers"" ADD COLUMN IF NOT EXISTS ""MinOrderQuantity"" integer NOT NULL DEFAULT 0;
ALTER TABLE IF EXISTS ""Suppliers"" ADD COLUMN IF NOT EXISTS ""AllowUrgent"" boolean NOT NULL DEFAULT FALSE;
ALTER TABLE IF EXISTS ""Suppliers"" ADD COLUMN IF NOT EXISTS ""UrgentLeadTimeHours"" integer NOT NULL DEFAULT 4;
ALTER TABLE IF EXISTS ""Suppliers"" ADD COLUMN IF NOT EXISTS ""UrgentFeePercent"" numeric(5,2) NOT NULL DEFAULT 0;

ALTER TABLE IF EXISTS ""SupplierProducts"" ADD COLUMN IF NOT EXISTS ""AllowedShortageQuantity"" integer NOT NULL DEFAULT 0;

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'SupplierDeliveryConfigs')
       AND EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'SupplierDeliveryConfigs' AND column_name = 'CutoffDayOffset') THEN
        UPDATE ""Suppliers"" s
        SET ""CutoffDayOffset"" = c.""CutoffDayOffset"",
            ""CutoffTime"" = COALESCE(c.""CutoffTime"", '17:00'),
            ""DeliveryWeekdaysMask"" = c.""DeliveryWeekdaysMask"",
            ""DeliveryDaysJson"" = COALESCE(c.""DeliveryDaysJson"", '[""Mon"",""Tue"",""Wed"",""Thu"",""Fri"",""Sat""]'),
            ""MinOrderQuantity"" = c.""MinOrderQuantity"",
            ""AllowUrgent"" = c.""AllowUrgent"",
            ""UrgentLeadTimeHours"" = c.""UrgentLeadTimeHours"",
            ""UrgentFeePercent"" = c.""UrgentFeePercent""
        FROM ""SupplierDeliveryConfigs"" c
        WHERE s.""Id"" = c.""SupplierId"";

        IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'SupplierBlackoutDates' AND column_name = 'SupplierDeliveryConfigId') THEN
            UPDATE ""SupplierBlackoutDates"" b
            SET ""SupplierId"" = c.""SupplierId""
            FROM ""SupplierDeliveryConfigs"" c
            WHERE b.""SupplierDeliveryConfigId"" = c.""Id"" AND (b.""SupplierId"" IS NULL OR b.""SupplierId"" = '00000000-0000-0000-0000-000000000000');
        END IF;

        DROP TABLE IF EXISTS ""SupplierDeliveryConfigs"" CASCADE;
    END IF;

    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'SupplierBlackoutDates' AND column_name = 'SupplierDeliveryConfigId') THEN
        ALTER TABLE ""SupplierBlackoutDates"" DROP COLUMN IF EXISTS ""SupplierDeliveryConfigId"";
    END IF;
END $$;
");
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Cảnh báo khi bổ sung cột bảng PurchaseOrders/SupplierDispatchPackages/Suppliers");
        }

        // 3. Tạo các chỉ mục Index
        try
        {
            await context.Database.ExecuteSqlRawAsync(@"
CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PurchaseOrderRevisions_Tenant_Po_Rev""
    ON ""PurchaseOrderRevisions"" (""TenantId"", ""PurchaseOrderId"", ""RevisionNumber"");

DROP INDEX IF EXISTS ""IX_SupplierDispatchPackageItems_PurchaseOrderId"";
CREATE UNIQUE INDEX IF NOT EXISTS ""IX_SupplierDispatchPackageItems_PurchaseOrderId_Active""
    ON ""SupplierDispatchPackageItems"" (""PurchaseOrderId"") WHERE ""IsDeleted"" = false;

CREATE UNIQUE INDEX IF NOT EXISTS ""IX_OutboxMessages_TenantId_IdempotencyKey""
    ON ""OutboxMessages"" (""TenantId"", ""IdempotencyKey"");
");
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Cảnh báo khi tạo index OutboxMessages / PurchaseOrderRevisions");
        }
    }

    private static async Task ExecuteSqlSafeAsync(AppDbContext context, string sql, ILogger? logger = null)
    {
        try
        {
            await context.Database.ExecuteSqlRawAsync(sql);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "ExecuteSqlSafe warning: {Sql}", sql);
        }
    }

    /// <summary>
    /// Đồng bộ cột còn thiếu: với mỗi bảng đã tồn tại trong DB nhưng thiếu cột so với EF model
    /// (do DB cũ chưa theo kịp code sau khi merge), tự động ADD COLUMN IF NOT EXISTS.
    /// Cột NOT NULL được gán DEFAULT an toàn theo kiểu dữ liệu để không lỗi với dữ liệu hiện có.
    /// </summary>
    private static async Task SyncMissingColumnsAsync(AppDbContext context, ILogger? logger = null)
    {
        foreach (var entityType in context.Model.GetEntityTypes())
        {
            var table = entityType.GetTableName();
            if (string.IsNullOrEmpty(table)) continue;
            var store = StoreObjectIdentifier.Table(table, entityType.GetSchema());

            foreach (var prop in entityType.GetProperties())
            {
                var column = prop.GetColumnName(store);
                var type = prop.GetColumnType(store);
                if (string.IsNullOrEmpty(column) || string.IsNullOrEmpty(type) || column == "xmin") continue;

                var t = type.ToLowerInvariant();
                string? defaultExpr = prop.GetDefaultValueSql(store);
                if (defaultExpr == null)
                {
                    defaultExpr = t.StartsWith("uuid") ? "'00000000-0000-0000-0000-000000000000'"
                        : t.StartsWith("bool") ? "false"
                        : (t.StartsWith("int") || t.StartsWith("bigint") || t.StartsWith("smallint")
                            || t.StartsWith("numeric") || t.StartsWith("decimal") || t.StartsWith("double") || t.StartsWith("real")) ? "0"
                        : t.StartsWith("timestamp") ? "CURRENT_TIMESTAMP"
                        : (t.StartsWith("text") || t.StartsWith("character")) ? "''"
                        : null;
                }

                var nullable = prop.IsColumnNullable(store);
                var ddl = nullable || defaultExpr == null
                    ? $"ALTER TABLE IF EXISTS \"{table}\" ADD COLUMN IF NOT EXISTS \"{column}\" {type} NULL;"
                    : $"ALTER TABLE IF EXISTS \"{table}\" ADD COLUMN IF NOT EXISTS \"{column}\" {type} NOT NULL DEFAULT {defaultExpr};";

                try
                {
                    // Bảng chưa tồn tại sẽ báo 42P01 → bỏ qua; chỉ bổ sung cột cho bảng đã có
                    await context.Database.ExecuteSqlRawAsync(ddl.Replace("{", "{{").Replace("}", "}}"));
                }
                catch (Exception ex)
                {
                    logger?.LogDebug(ex, "SyncMissingColumns bỏ qua: {Ddl}", ddl);
                }
            }
        }
    }

    private static async Task EnsureBranchColumnsAsync(AppDbContext context, ILogger? logger = null)
    {
        try
        {
            await context.Database.ExecuteSqlRawAsync(@"
                ALTER TABLE ""Branches"" ADD COLUMN IF NOT EXISTS ""Email"" character varying(150) NULL;
                ALTER TABLE ""Branches"" ADD COLUMN IF NOT EXISTS ""ManagerName"" character varying(150) NULL;
                ALTER TABLE ""Branches"" ADD COLUMN IF NOT EXISTS ""IsHeadquarter"" boolean NOT NULL DEFAULT false;
            ");
            logger?.LogInformation("[MT-GRMS] Đã đồng bộ các cột Email, ManagerName, IsHeadquarter cho bảng Branches.");
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Cảnh báo khi đồng bộ cột cho bảng Branches");
        }
    }

    private static async Task EnsureBranchProductsSchemaAsync(AppDbContext context, ILogger? logger = null)
    {
        try
        {
            await context.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS ""BranchProducts"" (
                    ""Id"" uuid NOT NULL PRIMARY KEY,
                    ""TenantId"" character varying(50) NOT NULL,
                    ""BranchId"" uuid NOT NULL,
                    ""ProductId"" uuid NOT NULL,
                    ""SellingPriceOverride"" numeric(18,2) NULL,
                    ""Status"" character varying(20) NOT NULL DEFAULT 'Active',
                    ""ShelfLocation"" character varying(50) NULL,
                    ""CreatedAt"" timestamp with time zone NOT NULL DEFAULT (NOW() AT TIME ZONE 'UTC'),
                    ""UpdatedAt"" timestamp with time zone NULL,
                    ""IsDeleted"" boolean NOT NULL DEFAULT FALSE,
                    ""CreatedBy"" uuid NULL
                );
                CREATE INDEX IF NOT EXISTS ""IX_BranchProducts_BranchId"" ON ""BranchProducts"" (""BranchId"");
                CREATE INDEX IF NOT EXISTS ""IX_BranchProducts_ProductId"" ON ""BranchProducts"" (""ProductId"");

                WITH ranked_bp AS (
                    SELECT ""Id"",
                           ROW_NUMBER() OVER (PARTITION BY ""TenantId"", ""BranchId"", ""ProductId"" ORDER BY ""CreatedAt"" ASC) AS rn
                    FROM ""BranchProducts""
                    WHERE ""IsDeleted"" = false
                )
                DELETE FROM ""BranchProducts""
                WHERE ""Id"" IN (SELECT ""Id"" FROM ranked_bp WHERE rn > 1);

                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_BranchProducts_TenantId_BranchId_ProductId""
                    ON ""BranchProducts"" (""TenantId"", ""BranchId"", ""ProductId"")
                    WHERE ""IsDeleted"" = false;

                CREATE TABLE IF NOT EXISTS ""BranchProductUnitPrices"" (
                    ""Id"" uuid NOT NULL PRIMARY KEY,
                    ""TenantId"" character varying(50) NOT NULL,
                    ""BranchProductId"" uuid NOT NULL,
                    ""ProductUnitId"" uuid NOT NULL,
                    ""SellingPriceOverride"" numeric(18,2) NOT NULL,
                    ""CreatedAt"" timestamp with time zone NOT NULL DEFAULT (NOW() AT TIME ZONE 'UTC'),
                    ""UpdatedAt"" timestamp with time zone NULL,
                    ""IsDeleted"" boolean NOT NULL DEFAULT FALSE,
                    ""CreatedBy"" uuid NULL
                );
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_BranchProductUnitPrices_BranchProductId_ProductUnitId""
                    ON ""BranchProductUnitPrices"" (""BranchProductId"", ""ProductUnitId"")
                    WHERE ""IsDeleted"" = false;

                ALTER TABLE ""BranchStocks"" ADD COLUMN IF NOT EXISTS ""RetailPrice"" numeric(18,2) NULL;
                ALTER TABLE ""BranchStocks"" ADD COLUMN IF NOT EXISTS ""ShelfLocation"" character varying(100) NULL;

                INSERT INTO ""BranchProducts"" (""Id"", ""TenantId"", ""BranchId"", ""ProductId"", ""SellingPriceOverride"", ""Status"", ""ShelfLocation"", ""CreatedAt"", ""IsDeleted"")
                SELECT
                    md5(bs.""TenantId"" || ':' || bs.""BranchId""::text || ':' || bs.""ProductId""::text)::uuid,
                    bs.""TenantId"",
                    bs.""BranchId"",
                    bs.""ProductId"",
                    MAX(bs.""RetailPrice""),
                    'Active',
                    MAX(bs.""ShelfLocation""),
                    NOW() AT TIME ZONE 'UTC',
                    false
                FROM ""BranchStocks"" bs
                INNER JOIN ""Branches"" b ON b.""BranchID"" = bs.""BranchId"" AND b.""TenantID"" = bs.""TenantId"" AND b.""IsDeleted"" = false
                INNER JOIN ""Products"" p ON p.""ProductID"" = bs.""ProductId"" AND p.""TenantID"" = bs.""TenantId"" AND p.""IsDeleted"" = false
                WHERE bs.""IsDeleted"" = false
                  AND NOT EXISTS (
                      SELECT 1 FROM ""BranchProducts"" bp
                      WHERE bp.""TenantId"" = bs.""TenantId""
                        AND bp.""BranchId"" = bs.""BranchId""
                        AND bp.""ProductId"" = bs.""ProductId""
                  )
                GROUP BY bs.""TenantId"", bs.""BranchId"", bs.""ProductId"";
            ");
            logger?.LogInformation("[MT-GRMS] Đã đồng bộ bảng BranchProducts, BranchProductUnitPrices và backfill theo chi nhánh.");
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Cảnh báo khi đồng bộ bảng BranchProducts");
        }
    }

    private static async Task EnsureTenantSubscriptionColumnsAsync(AppDbContext context, ILogger? logger = null)
    {
        try
        {
            await context.Database.ExecuteSqlRawAsync(@"
                ALTER TABLE ""Tenants"" ADD COLUMN IF NOT EXISTS ""ExtraBranches"" integer NOT NULL DEFAULT 0;
                ALTER TABLE ""Tenants"" ADD COLUMN IF NOT EXISTS ""HasStaffManagementAddon"" boolean NOT NULL DEFAULT false;
                ALTER TABLE ""Tenants"" ADD COLUMN IF NOT EXISTS ""SubscriptionStatus"" character varying(30) NOT NULL DEFAULT 'TRIAL';
                ALTER TABLE ""Tenants"" ADD COLUMN IF NOT EXISTS ""WalletBalance"" numeric(18,2) NOT NULL DEFAULT 0;
                ALTER TABLE ""Tenants"" ADD COLUMN IF NOT EXISTS ""SubscriptionExpiresAt"" timestamp with time zone NULL;
                ALTER TABLE ""Tenants"" ADD COLUMN IF NOT EXISTS ""GracePeriodExpiresAt"" timestamp with time zone NULL;
                ALTER TABLE ""Tenants"" ADD COLUMN IF NOT EXISTS ""LockType"" character varying(50) NULL;
                ALTER TABLE ""Tenants"" ADD COLUMN IF NOT EXISTS ""LockReason"" character varying(500) NULL;
                ALTER TABLE ""Tenants"" ADD COLUMN IF NOT EXISTS ""LockedAt"" timestamp with time zone NULL;
                ALTER TABLE ""Tenants"" ADD COLUMN IF NOT EXISTS ""LockedBy"" character varying(100) NULL;

                CREATE TABLE IF NOT EXISTS ""WalletTransactions"" (
                    ""Id"" character varying(50) NOT NULL PRIMARY KEY,
                    ""TenantId"" character varying(50) NOT NULL,
                    ""TransactionCode"" character varying(50) NOT NULL,
                    ""Type"" character varying(40) NOT NULL,
                    ""Amount"" numeric(18,2) NOT NULL,
                    ""BalanceBefore"" numeric(18,2) NOT NULL,
                    ""BalanceAfter"" numeric(18,2) NOT NULL,
                    ""Description"" character varying(255) NOT NULL,
                    ""PaymentMethod"" character varying(50) NOT NULL,
                    ""Status"" character varying(30) NOT NULL DEFAULT 'SUCCESS',
                    ""ReferenceCode"" character varying(100) NULL,
                    ""Metadata"" character varying(1000) NULL,
                    ""CreatedBy"" uuid NULL,
                    ""CreatedAt"" timestamp with time zone NOT NULL DEFAULT (NOW() AT TIME ZONE 'UTC'),
                    ""UpdatedAt"" timestamp with time zone NULL,
                    ""IsDeleted"" boolean NOT NULL DEFAULT false
                );
                CREATE INDEX IF NOT EXISTS ""IX_WalletTransactions_TenantId"" ON ""WalletTransactions"" (""TenantId"");
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_WalletTransactions_TransactionCode"" ON ""WalletTransactions"" (""TransactionCode"");
                CREATE INDEX IF NOT EXISTS ""IX_WalletTransactions_ReferenceCode"" ON ""WalletTransactions"" (""ReferenceCode"");

                CREATE TABLE IF NOT EXISTS ""WalletTopupOrders"" (
                    ""Id"" character varying(50) NOT NULL PRIMARY KEY,
                    ""TenantId"" character varying(50) NOT NULL,
                    ""OrderCode"" character varying(50) NOT NULL,
                    ""Amount"" numeric(18,2) NOT NULL,
                    ""TransferContent"" character varying(100) NOT NULL,
                    ""BankCode"" character varying(30) NOT NULL,
                    ""BankName"" character varying(100) NOT NULL,
                    ""AccountNumber"" character varying(50) NOT NULL,
                    ""AccountName"" character varying(150) NOT NULL,
                    ""QrUrl"" character varying(500) NOT NULL,
                    ""Status"" character varying(30) NOT NULL DEFAULT 'PENDING',
                    ""ExpiresAt"" timestamp with time zone NOT NULL,
                    ""CompletedAt"" timestamp with time zone NULL,
                    ""ReferenceCode"" character varying(100) NULL,
                    ""CreatedBy"" uuid NULL,
                    ""CreatedAt"" timestamp with time zone NOT NULL DEFAULT (NOW() AT TIME ZONE 'UTC'),
                    ""UpdatedAt"" timestamp with time zone NULL,
                    ""IsDeleted"" boolean NOT NULL DEFAULT false
                );
                CREATE INDEX IF NOT EXISTS ""IX_WalletTopupOrders_TenantId"" ON ""WalletTopupOrders"" (""TenantId"");
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_WalletTopupOrders_OrderCode"" ON ""WalletTopupOrders"" (""OrderCode"");
                CREATE INDEX IF NOT EXISTS ""IX_WalletTopupOrders_Status"" ON ""WalletTopupOrders"" (""Status"");

                CREATE TABLE IF NOT EXISTS ""LoyaltySettings"" (
                    ""LoyaltySettingID"" uuid NOT NULL PRIMARY KEY,
                    ""TenantID"" character varying(50) NOT NULL,
                    ""IsEnabled"" boolean NOT NULL DEFAULT true,
                    ""EarningRateAmount"" numeric(18,2) NOT NULL DEFAULT 10000.00,
                    ""EarningPoints"" integer NOT NULL DEFAULT 1,
                    ""RedemptionRatePoints"" integer NOT NULL DEFAULT 1,
                    ""RedemptionRateAmount"" numeric(18,2) NOT NULL DEFAULT 1000.00,
                    ""MinRedemptionPoints"" integer NOT NULL DEFAULT 20,
                    ""AllowPointsWithPromotions"" boolean NOT NULL DEFAULT true,
                    ""CreatedBy"" uuid NULL,
                    ""CreatedAt"" timestamp with time zone NOT NULL DEFAULT (NOW() AT TIME ZONE 'UTC'),
                    ""UpdatedAt"" timestamp with time zone NULL,
                    ""IsDeleted"" boolean NOT NULL DEFAULT false
                );
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_LoyaltySettings_TenantID"" ON ""LoyaltySettings"" (""TenantID"");

                UPDATE ""SubscriptionPlans"" SET ""IsActive"" = false WHERE ""PlanCode"" NOT IN ('TRIAL', 'STANDARD', 'PRO');
            ");
            logger?.LogInformation("[MT-GRMS] Đã đồng bộ đầy đủ cột Wallet/Subscription cho bảng Tenants cùng WalletTransactions, WalletTopupOrders và LoyaltySettings.");
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Cảnh báo khi đồng bộ cột subscription cho bảng Tenants");
        }
    }

    private static async Task EnsureMissingModelSchemaAsync(AppDbContext context, ILogger? logger = null)
    {
        if (!context.Database.IsRelational()) return;

        try
        {
            var conn = context.Database.GetDbConnection();
            var shouldClose = conn.State != System.Data.ConnectionState.Open;
            if (shouldClose) await conn.OpenAsync();

            try
            {
                var existingTables = new HashSet<string>(StringComparer.Ordinal);
                await using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public';";
                    await using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        existingTables.Add(reader.GetString(0));
                    }
                }

                var modelTableNames = context.Model.GetEntityTypes()
                    .Where(et => et.GetViewName() == null && !string.IsNullOrEmpty(et.GetTableName()))
                    .Select(et => et.GetTableName()!)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                if (modelTableNames.Any(t => !existingTables.Contains(t)) &&
                    context.Database.GetService<IDatabaseCreator>() is IRelationalDatabaseCreator creator)
                {
                    var createScript = creator.GenerateCreateScript();
                    var createBlocks = Regex.Matches(createScript, @"CREATE TABLE ""?(?<table>[^""\s\(]+)""?\s*\([\s\S]*?\);", RegexOptions.Multiline);
                    for (var pass = 0; pass < 2; pass++)
                    {
                        foreach (Match m in createBlocks)
                        {
                            var tbl = m.Groups["table"].Value;
                            if (existingTables.Contains(tbl)) continue;
                            var safeSql = Regex.Replace(m.Value, @"^CREATE TABLE ", "CREATE TABLE IF NOT EXISTS ");
                            try
                            {
                                await context.Database.ExecuteSqlRawAsync(safeSql);
                                existingTables.Add(tbl);
                            }
                            catch { }
                        }
                    }
                }

                var existingColumns = new HashSet<(string Table, string Column)>();
                await using (var colCmd = conn.CreateCommand())
                {
                    colCmd.CommandText = "SELECT table_name, column_name FROM information_schema.columns WHERE table_schema = 'public';";
                    await using var colReader = await colCmd.ExecuteReaderAsync();
                    while (await colReader.ReadAsync())
                    {
                        existingColumns.Add((colReader.GetString(0), colReader.GetString(1)));
                    }
                }

                var addedCols = 0;
                foreach (var entityType in context.Model.GetEntityTypes())
                {
                    if (entityType.GetViewName() != null) continue;
                    var tableName = entityType.GetTableName();
                    if (string.IsNullOrEmpty(tableName) || !existingTables.Contains(tableName)) continue;

                    var storeObject = StoreObjectIdentifier.Table(tableName, entityType.GetSchema());
                    foreach (var prop in entityType.GetProperties())
                    {
                        if (prop.IsPrimaryKey()) continue;
                        var colName = prop.GetColumnName(storeObject);
                        if (string.IsNullOrEmpty(colName) || existingColumns.Contains((tableName, colName))) continue;

                        var colType = prop.GetColumnType(storeObject);
                        if (string.IsNullOrEmpty(colType) || colType.Contains("serial", StringComparison.OrdinalIgnoreCase)) continue;

                        var lowerType = colType.ToLowerInvariant();
                        string constraintClause;
                        if (prop.IsNullable)
                        {
                            constraintClause = "NULL";
                        }
                        else if (lowerType.EndsWith("[]"))
                        {
                            constraintClause = "NOT NULL DEFAULT '{}'";
                        }
                        else if (lowerType.Contains("bool"))
                        {
                            constraintClause = "NOT NULL DEFAULT false";
                        }
                        else if (lowerType.Contains("int") || lowerType.Contains("numeric") || lowerType.Contains("decimal") || lowerType.Contains("double") || lowerType.Contains("real"))
                        {
                            constraintClause = "NOT NULL DEFAULT 0";
                        }
                        else if (lowerType == "uuid")
                        {
                            constraintClause = "NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000'";
                        }
                        else if (lowerType.StartsWith("timestamp"))
                        {
                            constraintClause = "NOT NULL DEFAULT CURRENT_TIMESTAMP";
                        }
                        else if (lowerType.StartsWith("time"))
                        {
                            constraintClause = "NOT NULL DEFAULT '00:00:00'";
                        }
                        else if (lowerType == "date")
                        {
                            constraintClause = "NOT NULL DEFAULT CURRENT_DATE";
                        }
                        else if (lowerType.Contains("json"))
                        {
                            constraintClause = "NOT NULL DEFAULT '{}'";
                        }
                        else
                        {
                            constraintClause = "NOT NULL DEFAULT ''";
                        }

                        var alterSql = $"ALTER TABLE \"{tableName}\" ADD COLUMN IF NOT EXISTS \"{colName}\" {colType} {constraintClause};";
                        try
                        {
                            await context.Database.ExecuteSqlRawAsync(alterSql);
                            existingColumns.Add((tableName, colName));
                            addedCols++;
                        }
                        catch (Exception colEx)
                        {
                            logger?.LogWarning(colEx, "[MT-GRMS] Không thể tự động thêm cột {Table}.{Column}", tableName, colName);
                        }
                    }
                }

                if (addedCols > 0)
                {
                    logger?.LogInformation("[MT-GRMS] Đã tự động đồng bộ {Count} cột còn thiếu từ AppDbContext Model sang PostgreSQL.", addedCols);
                }
            }
            finally
            {
                if (shouldClose) await conn.CloseAsync();
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "[MT-GRMS] Cảnh báo khi quét tự động schema thiếu từ AppDbContext Model.");
        }
    }

    public static async Task InitializeAsync(AppDbContext context, ILogger? logger = null)
    {
        // 0. Bổ sung các cột schema còn thiếu trên Cloud DB
        await ExecuteSqlSafeAsync(context, @"ALTER TABLE IF EXISTS ""ProductUnits"" ADD COLUMN IF NOT EXISTS ""CreatedBy"" uuid;", logger);
        await ExecuteSqlSafeAsync(context, @"ALTER TABLE IF EXISTS ""ProductUnits"" ADD COLUMN IF NOT EXISTS ""TenantId"" character varying(50);", logger);
        await ExecuteSqlSafeAsync(context, @"ALTER TABLE IF EXISTS ""ProductUnits"" ADD COLUMN IF NOT EXISTS ""CreatedAt"" timestamp without time zone DEFAULT CURRENT_TIMESTAMP;", logger);
        await ExecuteSqlSafeAsync(context, @"ALTER TABLE IF EXISTS ""ProductUnits"" ADD COLUMN IF NOT EXISTS ""UpdatedAt"" timestamp without time zone;", logger);
        await ExecuteSqlSafeAsync(context, @"ALTER TABLE IF EXISTS ""ProductUnits"" ADD COLUMN IF NOT EXISTS ""IsDeleted"" boolean DEFAULT false;", logger);
        await ExecuteSqlSafeAsync(context, @"ALTER TABLE IF EXISTS ""ProductUnits"" ADD COLUMN IF NOT EXISTS ""IsPurchaseUnit"" boolean NOT NULL DEFAULT false;", logger);

        await ExecuteSqlSafeAsync(context, @"ALTER TABLE IF EXISTS ""BranchProductUnitPrices"" ADD COLUMN IF NOT EXISTS ""CreatedBy"" uuid;", logger);
        await ExecuteSqlSafeAsync(context, @"ALTER TABLE IF EXISTS ""BranchProductUnitPrices"" ADD COLUMN IF NOT EXISTS ""TenantId"" character varying(50);", logger);
        await ExecuteSqlSafeAsync(context, @"ALTER TABLE IF EXISTS ""BranchProductUnitPrices"" ADD COLUMN IF NOT EXISTS ""CreatedAt"" timestamp without time zone DEFAULT CURRENT_TIMESTAMP;", logger);
        await ExecuteSqlSafeAsync(context, @"ALTER TABLE IF EXISTS ""BranchProductUnitPrices"" ADD COLUMN IF NOT EXISTS ""UpdatedAt"" timestamp without time zone;", logger);
        await ExecuteSqlSafeAsync(context, @"ALTER TABLE IF EXISTS ""BranchProductUnitPrices"" ADD COLUMN IF NOT EXISTS ""IsDeleted"" boolean DEFAULT false;", logger);

        await ExecuteSqlSafeAsync(context, @"ALTER TABLE IF EXISTS ""OrderDetails"" ADD COLUMN IF NOT EXISTS ""CreatedBy"" uuid;", logger);
        await ExecuteSqlSafeAsync(context, @"ALTER TABLE IF EXISTS ""OrderDetails"" ADD COLUMN IF NOT EXISTS ""TenantId"" character varying(50);", logger);
        await ExecuteSqlSafeAsync(context, @"ALTER TABLE IF EXISTS ""OrderDetails"" ADD COLUMN IF NOT EXISTS ""CreatedAt"" timestamp without time zone DEFAULT CURRENT_TIMESTAMP;", logger);
        await ExecuteSqlSafeAsync(context, @"ALTER TABLE IF EXISTS ""OrderDetails"" ADD COLUMN IF NOT EXISTS ""UpdatedAt"" timestamp without time zone;", logger);
        await ExecuteSqlSafeAsync(context, @"ALTER TABLE IF EXISTS ""OrderDetails"" ADD COLUMN IF NOT EXISTS ""IsDeleted"" boolean DEFAULT false;", logger);
        await ExecuteSqlSafeAsync(context, @"ALTER TABLE IF EXISTS ""OrderDetails"" ADD COLUMN IF NOT EXISTS ""CostPriceAtSale"" numeric(18,4) NOT NULL DEFAULT 0.0000;", logger);
        await ExecuteSqlSafeAsync(context, @"ALTER TABLE IF EXISTS ""OrderDetails"" ADD COLUMN IF NOT EXISTS ""AppliedVatRate"" numeric(5,2) NOT NULL DEFAULT 0.00;", logger);
        await ExecuteSqlSafeAsync(context, @"ALTER TABLE IF EXISTS ""OrderDetails"" ADD COLUMN IF NOT EXISTS ""VatAmount"" numeric(18,2) NOT NULL DEFAULT 0.00;", logger);
        await ExecuteSqlSafeAsync(context, @"ALTER TABLE IF EXISTS ""OrderDetails"" ADD COLUMN IF NOT EXISTS ""BatchCode"" character varying(100);", logger);
        await ExecuteSqlSafeAsync(context, @"ALTER TABLE IF EXISTS ""OrderDetails"" ADD COLUMN IF NOT EXISTS ""UnitId"" uuid;", logger);
        await ExecuteSqlSafeAsync(context, @"ALTER TABLE IF EXISTS ""OrderDetails"" ADD COLUMN IF NOT EXISTS ""UnitName"" character varying(50);", logger);

        await ExecuteSqlSafeAsync(context, @"ALTER TABLE IF EXISTS ""OrderDetails"" ADD COLUMN IF NOT EXISTS ""VatDiscountAmount"" numeric(18,2) NOT NULL DEFAULT 0.00;", logger);

        await ExecuteSqlSafeAsync(context, @"ALTER TABLE IF EXISTS ""BranchStocks"" ADD COLUMN IF NOT EXISTS ""Status"" character varying(20) NOT NULL DEFAULT 'Active';", logger);
        await ExecuteSqlSafeAsync(context, @"ALTER TABLE IF EXISTS ""BranchStocks"" ADD COLUMN IF NOT EXISTS ""RetailPrice"" numeric(18,2) NULL;", logger);
        await ExecuteSqlSafeAsync(context, @"ALTER TABLE IF EXISTS ""BranchStocks"" ADD COLUMN IF NOT EXISTS ""ZoneArea"" character varying(50) NULL;", logger);
        await ExecuteSqlSafeAsync(context, @"ALTER TABLE IF EXISTS ""BranchStocks"" ADD COLUMN IF NOT EXISTS ""RowVersion"" integer NOT NULL DEFAULT 1;", logger);
        await ExecuteSqlSafeAsync(context, @"ALTER TABLE IF EXISTS ""BranchStocks"" ADD COLUMN IF NOT EXISTS ""CreatedBy"" uuid NULL;", logger);
        await ExecuteSqlSafeAsync(context, @"ALTER TABLE IF EXISTS ""BranchStocks"" ADD COLUMN IF NOT EXISTS ""CreatedAt"" timestamp without time zone DEFAULT CURRENT_TIMESTAMP;", logger);
        await ExecuteSqlSafeAsync(context, @"ALTER TABLE IF EXISTS ""BranchStocks"" ADD COLUMN IF NOT EXISTS ""UpdatedAt"" timestamp without time zone;", logger);
        await ExecuteSqlSafeAsync(context, @"ALTER TABLE IF EXISTS ""BranchStocks"" ADD COLUMN IF NOT EXISTS ""IsDeleted"" boolean DEFAULT false;", logger);

        // Đảm bảo bảng BranchProducts tồn tại
        await ExecuteSqlSafeAsync(context, @"
CREATE TABLE IF NOT EXISTS ""BranchProducts"" (
    ""Id"" uuid NOT NULL PRIMARY KEY,
    ""TenantId"" character varying(50) NOT NULL,
    ""BranchId"" uuid NOT NULL,
    ""ProductId"" uuid NOT NULL,
    ""SellingPriceOverride"" numeric(18,2) NULL,
    ""Status"" character varying(20) NOT NULL DEFAULT 'Active',
    ""ShelfLocation"" character varying(50) NULL,
    ""CreatedAt"" timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
    ""UpdatedAt"" timestamp with time zone NULL,
    ""IsDeleted"" boolean NOT NULL DEFAULT false,
    ""CreatedBy"" uuid NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS ""IX_BranchProducts_TenantId_BranchId_ProductId""
    ON ""BranchProducts"" (""TenantId"", ""BranchId"", ""ProductId"")
    WHERE ""IsDeleted"" = false;
CREATE INDEX IF NOT EXISTS ""IX_BranchProducts_BranchId"" ON ""BranchProducts"" (""BranchId"");
CREATE INDEX IF NOT EXISTS ""IX_BranchProducts_ProductId"" ON ""BranchProducts"" (""ProductId"");
", logger);

        // Đảm bảo bảng BranchProductUnitPrices tồn tại
        await ExecuteSqlSafeAsync(context, @"
CREATE TABLE IF NOT EXISTS ""BranchProductUnitPrices"" (
    ""Id"" uuid NOT NULL PRIMARY KEY,
    ""TenantId"" character varying(50) NOT NULL,
    ""BranchProductId"" uuid NOT NULL,
    ""ProductUnitId"" uuid NOT NULL,
    ""SellingPriceOverride"" numeric(18,2) NOT NULL,
    ""CreatedAt"" timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
    ""UpdatedAt"" timestamp with time zone NULL,
    ""IsDeleted"" boolean NOT NULL DEFAULT false,
    ""CreatedBy"" uuid NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS ""IX_BranchProductUnitPrices_BranchProductId_ProductUnitId""
    ON ""BranchProductUnitPrices"" (""BranchProductId"", ""ProductUnitId"")
    WHERE ""IsDeleted"" = false;
CREATE INDEX IF NOT EXISTS ""IX_BranchProductUnitPrices_ProductUnitId""
    ON ""BranchProductUnitPrices"" (""ProductUnitId"");
", logger);

        // Backfill BranchProducts từ BranchStocks nếu trống (chỉ chạy khi bảng BranchStocks đã tồn tại)
        await ExecuteSqlSafeAsync(context, @"
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = 'public' AND table_name = 'BranchStocks') THEN
        INSERT INTO ""BranchProducts"" (
            ""Id"", ""TenantId"", ""BranchId"", ""ProductId"", ""SellingPriceOverride"",
            ""Status"", ""ShelfLocation"", ""CreatedAt"", ""UpdatedAt"", ""IsDeleted"", ""CreatedBy""
        )
        SELECT
            gen_random_uuid(),
            src.""TenantId"",
            src.""BranchId"",
            src.""ProductId"",
            NULL,
            'Active',
            src.""ShelfLocation"",
            CURRENT_TIMESTAMP,
            CURRENT_TIMESTAMP,
            false,
            NULL
        FROM (
            SELECT DISTINCT ON (bs.""TenantId"", bs.""BranchId"", bs.""ProductId"")
                bs.""TenantId"",
                bs.""BranchId"",
                bs.""ProductId"",
                bs.""ShelfLocation""
            FROM ""BranchStocks"" bs
            WHERE bs.""IsDeleted"" = false
        ) src
        ON CONFLICT DO NOTHING;
    END IF;
END $$;
", logger);

        // 1. Tự động áp dụng Migrations hoặc EnsureCreatedAsync để khởi tạo toàn bộ CSDL và các bảng nếu chưa có
        try
        {
            if (context.Database.IsRelational())
            {
                // Đảm bảo bảng __EFMigrationsHistory tồn tại
                try
                {
                    await context.Database.ExecuteSqlRawAsync(@"
                        CREATE TABLE IF NOT EXISTS ""__EFMigrationsHistory"" (
                            ""MigrationId"" character varying(150) NOT NULL PRIMARY KEY,
                            ""ProductVersion"" character varying(32) NOT NULL
                        );
                    ");
                }
                catch { }

                // Sinh toàn bộ các bảng trong model nếu chưa tồn tại (chỉ chạy khi CSDL chưa có bảng nào)
                var creator = context.Database.GetService<IDatabaseCreator>() as IRelationalDatabaseCreator;
                if (creator != null)
                {
                    try
                    {
                        var script = creator.GenerateCreateScript();
                        var safeScript = script
                            .Replace("CREATE TABLE ", "CREATE TABLE IF NOT EXISTS ")
                            .Replace("CREATE INDEX ", "CREATE INDEX IF NOT EXISTS ")
                            .Replace("CREATE UNIQUE INDEX ", "CREATE UNIQUE INDEX IF NOT EXISTS ");

                        // Chạy TỪNG câu lệnh riêng lẻ: 1 câu lỗi (vd. index trên cột chưa tồn tại ở DB cũ)
                        // không được làm hỏng toàn bộ script khiến các bảng phía sau không được tạo.
                        var statements = System.Text.RegularExpressions.Regex
                            .Split(safeScript, @";\s*\r?\n\s*\r?\n")
                            .Select(s => s.Trim())
                            .Where(s => s.Length > 0)
                            .ToList();

                        // Bước 1: tạo bảng thiếu → Bước 2: bổ sung cột thiếu ở bảng đã tồn tại → Bước 3: tạo index
                        foreach (var stmt in statements.Where(s => s.StartsWith("CREATE TABLE", StringComparison.OrdinalIgnoreCase)))
                            await ExecuteSqlSafeAsync(context, stmt.Replace("{", "{{").Replace("}", "}}"), logger);

                        await SyncMissingColumnsAsync(context, logger);

                        foreach (var stmt in statements.Where(s => !s.StartsWith("CREATE TABLE", StringComparison.OrdinalIgnoreCase)))
                            await ExecuteSqlSafeAsync(context, stmt.Replace("{", "{{").Replace("}", "}}"), logger);
                    }
                    catch (Exception ex)
                    {
                        logger?.LogWarning(ex, "Lỗi khi chạy CreateTables safe script (có thể bảng đã tồn tại).");
                    }
                }
                else
                {
                    await context.Database.EnsureCreatedAsync();
                }

                try
                {
                    await context.Database.ExecuteSqlRawAsync(@"
                        CREATE TABLE IF NOT EXISTS supplier_branch_assignments (
                            ""Id"" uuid NOT NULL PRIMARY KEY,
                            ""BranchId"" uuid NOT NULL,
                            ""SupplierId"" uuid NOT NULL,
                            ""TenantId"" text NOT NULL,
                            ""CreatedBy"" uuid,
                            ""CreatedAt"" timestamp without time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
                            ""UpdatedAt"" timestamp without time zone,
                            ""IsDeleted"" boolean NOT NULL DEFAULT false
                        );
                    ");
                }
                catch (Exception ex)
                {
                    logger?.LogError(ex, "Lỗi khi chạy SQL DDL bổ sung bảng HR");
                }

                // Tách riêng từng khối DDL độc lập để lỗi một bảng không rollback các bảng còn lại
                try
                {
                    await context.Database.ExecuteSqlRawAsync(@"
                        ALTER TABLE IF EXISTS ""Products"" ADD COLUMN IF NOT EXISTS ""ImageUrls"" text[] DEFAULT ARRAY[]::text[];
                        ALTER TABLE IF EXISTS ""Users"" ADD COLUMN IF NOT EXISTS ""IsAdmin"" boolean DEFAULT false;
                        ALTER TABLE IF EXISTS ""Users"" ADD COLUMN IF NOT EXISTS ""IsEmailVerified"" boolean DEFAULT false;
                        ALTER TABLE IF EXISTS ""Users"" ADD COLUMN IF NOT EXISTS ""IsTwoFactorEnabled"" boolean NOT NULL DEFAULT true;
                        UPDATE ""Users"" SET ""IsTwoFactorEnabled"" = true WHERE ""IsTwoFactorEnabled"" IS NULL;
                        ALTER TABLE IF EXISTS ""Users"" ADD COLUMN IF NOT EXISTS ""MustChangePassword"" boolean DEFAULT false;
                        ALTER TABLE IF EXISTS ""Users"" ADD COLUMN IF NOT EXISTS ""LastLoginAt"" timestamp without time zone;
                        ALTER TABLE IF EXISTS ""Users"" ADD COLUMN IF NOT EXISTS ""LastLoginIp"" text;
                        ALTER TABLE IF EXISTS ""Users"" ADD COLUMN IF NOT EXISTS ""SecurityStamp"" text;
                        ALTER TABLE IF EXISTS ""Users"" ADD COLUMN IF NOT EXISTS ""PasswordResetOtp"" text;
                        ALTER TABLE IF EXISTS ""Users"" ADD COLUMN IF NOT EXISTS ""OtpExpiry"" timestamp without time zone;
                        ALTER TABLE IF EXISTS ""Users"" ADD COLUMN IF NOT EXISTS ""RefreshToken"" text;
                        ALTER TABLE IF EXISTS ""Users"" ADD COLUMN IF NOT EXISTS ""RefreshTokenExpiryTime"" timestamp without time zone;
                        ALTER TABLE IF EXISTS ""Users"" ADD COLUMN IF NOT EXISTS ""AccessFailedCount"" integer DEFAULT 0;
                        ALTER TABLE IF EXISTS ""Users"" ADD COLUMN IF NOT EXISTS ""LockoutEnd"" timestamp without time zone;
                        ALTER TABLE IF EXISTS ""Users"" ADD COLUMN IF NOT EXISTS ""SecurityPinHash"" text;
                        ALTER TABLE IF EXISTS ""Users"" ADD COLUMN IF NOT EXISTS ""SecurityPinSalt"" text;
                        ALTER TABLE IF EXISTS ""Users"" ADD COLUMN IF NOT EXISTS ""FailedPinAttempts"" integer DEFAULT 0;
                        ALTER TABLE IF EXISTS ""Users"" ADD COLUMN IF NOT EXISTS ""PinLockedUntilUtc"" timestamp without time zone;
                        UPDATE ""Users"" SET ""IsAdmin"" = true WHERE LOWER(""UserName"") = 'admin';
                    ");
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "Cảnh báo khi chạy DDL nâng cấp bảng Users");
                }

                try
                {
                    await context.Database.ExecuteSqlRawAsync(@"
                        ALTER TABLE IF EXISTS ""Orders"" ADD COLUMN IF NOT EXISTS ""ShiftID"" uuid;
                        ALTER TABLE IF EXISTS ""Orders"" ADD COLUMN IF NOT EXISTS ""CashierName"" character varying(150);
                        ALTER TABLE IF EXISTS ""Orders"" ADD COLUMN IF NOT EXISTS ""FulfilledByCashierName"" character varying(150);
                        ALTER TABLE IF EXISTS ""Orders"" ADD COLUMN IF NOT EXISTS ""FulfilledAtUtc"" timestamp without time zone;
                        ALTER TABLE IF EXISTS ""Orders"" ADD COLUMN IF NOT EXISTS ""Note"" character varying(1000);
                        ALTER TABLE IF EXISTS ""Orders"" ADD COLUMN IF NOT EXISTS ""AppliedPromotionId"" uuid;
                        ALTER TABLE IF EXISTS ""Orders"" ADD COLUMN IF NOT EXISTS ""AppliedPromotionSummary"" character varying(300);
                        ALTER TABLE IF EXISTS ""Orders"" ADD COLUMN IF NOT EXISTS ""PromotionDiscountAmount"" numeric(18,2) DEFAULT 0;
                        ALTER TABLE IF EXISTS ""Orders"" ADD COLUMN IF NOT EXISTS ""AppliedVoucherCode"" character varying(50);
                        ALTER TABLE IF EXISTS ""Orders"" ADD COLUMN IF NOT EXISTS ""VoucherDiscountAmount"" numeric(18,2) DEFAULT 0;
                        ALTER TABLE IF EXISTS ""Orders"" ADD COLUMN IF NOT EXISTS ""PointsRedeemed"" integer DEFAULT 0;
                        ALTER TABLE IF EXISTS ""Orders"" ADD COLUMN IF NOT EXISTS ""PointsDiscountAmount"" numeric(18,2) DEFAULT 0;

                        ALTER TABLE IF EXISTS ""OrderDetails"" ADD COLUMN IF NOT EXISTS ""ProductUnitID"" uuid;
                        ALTER TABLE IF EXISTS ""OrderDetails"" ADD COLUMN IF NOT EXISTS ""UnitName"" character varying(50);
                        ALTER TABLE IF EXISTS ""OrderDetails"" ADD COLUMN IF NOT EXISTS ""ConversionRate"" numeric(18,4) DEFAULT 1.00;
                        UPDATE ""OrderDetails"" SET ""ConversionRate"" = 1.00 WHERE ""ConversionRate"" IS NULL OR ""ConversionRate"" <= 0;
                        ALTER TABLE IF EXISTS ""OrderDetails"" ALTER COLUMN ""ConversionRate"" SET DEFAULT 1.00;
                        ALTER TABLE IF EXISTS ""OrderDetails"" ALTER COLUMN ""ConversionRate"" SET NOT NULL;
                        CREATE INDEX IF NOT EXISTS ""IX_OrderDetails_ProductUnitID"" ON ""OrderDetails"" (""ProductUnitID"");

                        ALTER TABLE IF EXISTS ""ShiftZReports"" ADD COLUMN IF NOT EXISTS ""BranchId"" uuid;
                        ALTER TABLE IF EXISTS ""ShiftZReports"" ADD COLUMN IF NOT EXISTS ""TillId"" uuid;
                        ALTER TABLE IF EXISTS ""ShiftZReports"" ADD COLUMN IF NOT EXISTS ""ShiftCode"" text;
                        ALTER TABLE IF EXISTS ""ShiftZReports"" ADD COLUMN IF NOT EXISTS ""ShiftName"" text;
                        ALTER TABLE IF EXISTS ""ShiftZReports"" ADD COLUMN IF NOT EXISTS ""CashierUserId"" uuid;
                        ALTER TABLE IF EXISTS ""ShiftZReports"" ADD COLUMN IF NOT EXISTS ""ReceiverUserId"" text;
                        ALTER TABLE IF EXISTS ""ShiftZReports"" ADD COLUMN IF NOT EXISTS ""PrevShiftId"" uuid;
                        ALTER TABLE IF EXISTS ""ShiftZReports"" ADD COLUMN IF NOT EXISTS ""OpeningFloatExpected"" numeric(18,2) DEFAULT 0;
                        ALTER TABLE IF EXISTS ""ShiftZReports"" ADD COLUMN IF NOT EXISTS ""OpeningFloat"" numeric(18,2) DEFAULT 0;
                        ALTER TABLE IF EXISTS ""ShiftZReports"" ADD COLUMN IF NOT EXISTS ""OpeningDiscrepancy"" numeric(18,2) DEFAULT 0;
                        ALTER TABLE IF EXISTS ""ShiftZReports"" ADD COLUMN IF NOT EXISTS ""TotalCashIn"" numeric(18,2) DEFAULT 0;
                        ALTER TABLE IF EXISTS ""ShiftZReports"" ADD COLUMN IF NOT EXISTS ""TotalCashOut"" numeric(18,2) DEFAULT 0;
                        ALTER TABLE IF EXISTS ""ShiftZReports"" ADD COLUMN IF NOT EXISTS ""TotalBankRevenue"" numeric(18,2) DEFAULT 0;
                        ALTER TABLE IF EXISTS ""ShiftZReports"" ADD COLUMN IF NOT EXISTS ""TotalOrdersCount"" integer DEFAULT 0;
                        ALTER TABLE IF EXISTS ""ShiftZReports"" ADD COLUMN IF NOT EXISTS ""ExpectedCash"" numeric(18,2) DEFAULT 0;
                        ALTER TABLE IF EXISTS ""ShiftZReports"" ADD COLUMN IF NOT EXISTS ""ActualCashCounted"" numeric(18,2);
                        ALTER TABLE IF EXISTS ""ShiftZReports"" ADD COLUMN IF NOT EXISTS ""Discrepancy"" numeric(18,2);
                        ALTER TABLE IF EXISTS ""ShiftZReports"" ALTER COLUMN ""Discrepancy"" DROP NOT NULL;
                        ALTER TABLE IF EXISTS ""ShiftZReports"" ADD COLUMN IF NOT EXISTS ""RetainedFloatAmount"" numeric(18,2) DEFAULT 0;
                        ALTER TABLE IF EXISTS ""ShiftZReports"" ADD COLUMN IF NOT EXISTS ""DroppedToSafeAmount"" numeric(18,2) DEFAULT 0;
                        ALTER TABLE IF EXISTS ""ShiftZReports"" ADD COLUMN IF NOT EXISTS ""HandoverStatus"" integer DEFAULT 1;
                        ALTER TABLE IF EXISTS ""ShiftZReports"" ADD COLUMN IF NOT EXISTS ""Notes"" text;
                        ALTER TABLE IF EXISTS ""ShiftZReports"" ADD COLUMN IF NOT EXISTS ""ClosingStartedByUserId"" uuid;
                        ALTER TABLE IF EXISTS ""ShiftZReports"" ADD COLUMN IF NOT EXISTS ""ClosingStartedAtUtc"" timestamp without time zone;
                        ALTER TABLE IF EXISTS ""ShiftZReports"" ADD COLUMN IF NOT EXISTS ""ClosedByUserId"" uuid;
                        ALTER TABLE IF EXISTS ""ShiftZReports"" ADD COLUMN IF NOT EXISTS ""ForceCloseReason"" text;

                        CREATE TABLE IF NOT EXISTS ""ShiftParticipants"" (
                            ""Id"" uuid NOT NULL PRIMARY KEY,
                            ""TenantId"" text NOT NULL,
                            ""ShiftId"" uuid NOT NULL,
                            ""UserId"" uuid NOT NULL,
                            ""UserName"" character varying(100),
                            ""FullName"" character varying(150),
                            ""JoinedAtUtc"" timestamp without time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
                            ""LastActiveAtUtc"" timestamp without time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
                            ""CreatedBy"" uuid,
                            ""CreatedAt"" timestamp without time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
                            ""UpdatedAt"" timestamp without time zone,
                            ""IsDeleted"" boolean NOT NULL DEFAULT false
                        );
                        CREATE INDEX IF NOT EXISTS ""IX_ShiftParticipants_TenantId_ShiftId_UserId"" ON ""ShiftParticipants"" (""TenantId"", ""ShiftId"", ""UserId"");

                        CREATE TABLE IF NOT EXISTS ""ShiftCloseConfirmations"" (
                            ""Id"" uuid NOT NULL PRIMARY KEY,
                            ""TenantId"" text NOT NULL,
                            ""ShiftId"" uuid NOT NULL,
                            ""UserId"" uuid NOT NULL,
                            ""UserName"" character varying(100),
                            ""ConfirmedCountedCash"" numeric(18,2) NOT NULL DEFAULT 0,
                            ""ConfirmedAtUtc"" timestamp without time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
                            ""CreatedBy"" uuid,
                            ""CreatedAt"" timestamp without time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
                            ""UpdatedAt"" timestamp without time zone,
                            ""IsDeleted"" boolean NOT NULL DEFAULT false
                        );
                        CREATE INDEX IF NOT EXISTS ""IX_ShiftCloseConfirmations_TenantId_ShiftId"" ON ""ShiftCloseConfirmations"" (""TenantId"", ""ShiftId"");

                        CREATE TABLE IF NOT EXISTS ""ShiftCashMovements"" (
                            ""Id"" uuid NOT NULL PRIMARY KEY,
                            ""TenantId"" text NOT NULL,
                            ""ShiftId"" uuid NOT NULL,
                            ""MovementType"" integer NOT NULL,
                            ""Category"" integer NOT NULL,
                            ""Amount"" numeric(18,2) NOT NULL,
                            ""Reason"" character varying(500) NOT NULL,
                            ""CreatedByName"" character varying(150),
                            ""OccurredAtUtc"" timestamp without time zone NOT NULL,
                            ""CreatedBy"" uuid,
                            ""CreatedAt"" timestamp without time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
                            ""UpdatedAt"" timestamp without time zone,
                            ""IsDeleted"" boolean NOT NULL DEFAULT false
                        );

                        ALTER TABLE IF EXISTS ""ShiftCashMovements"" ADD COLUMN IF NOT EXISTS ""Status"" integer DEFAULT 1;
                        ALTER TABLE IF EXISTS ""ShiftCashMovements"" ADD COLUMN IF NOT EXISTS ""ApprovedBy"" text;
                        ALTER TABLE IF EXISTS ""ShiftCashMovements"" ADD COLUMN IF NOT EXISTS ""ApprovedAtUtc"" timestamp without time zone;
                        ALTER TABLE IF EXISTS ""ShiftCashMovements"" ADD COLUMN IF NOT EXISTS ""RejectionReason"" text;
                        ALTER TABLE IF EXISTS ""ShiftCashMovements"" ADD COLUMN IF NOT EXISTS ""IsConfirmedByOwner"" boolean DEFAULT false;
                        ALTER TABLE IF EXISTS ""ShiftCashMovements"" ADD COLUMN IF NOT EXISTS ""ConfirmedByOwnerAtUtc"" timestamp without time zone;

                        CREATE TABLE IF NOT EXISTS ""StorePaymentConfigs"" (
                            ""Id"" uuid NOT NULL PRIMARY KEY,
                            ""TenantId"" text NOT NULL,
                            ""BankCode"" character varying(50) NOT NULL DEFAULT 'MB',
                            ""BankName"" character varying(150) NOT NULL DEFAULT 'MBBank - Ngân hàng Quân Đội',
                            ""BankAccountNumber"" character varying(50) NOT NULL DEFAULT '',
                            ""BankAccountName"" character varying(150) NOT NULL DEFAULT '',
                            ""SepayApiKeyEncrypted"" text NOT NULL DEFAULT '',
                            ""SepayWebhookSecret"" text NOT NULL DEFAULT '',
                            ""IsActive"" boolean NOT NULL DEFAULT true,
                            ""CashOutApprovalThreshold"" numeric(18,2) NOT NULL DEFAULT 200000,
                            ""CashOutShiftTotalLimit"" numeric(18,2) NOT NULL DEFAULT 0,
                            ""CreatedBy"" uuid,
                            ""CreatedAt"" timestamp without time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
                            ""UpdatedAt"" timestamp without time zone,
                            ""IsDeleted"" boolean NOT NULL DEFAULT false
                        );

                        CREATE TABLE IF NOT EXISTS ""PaymentTransactions"" (
                            ""Id"" uuid NOT NULL PRIMARY KEY,
                            ""TenantId"" text NOT NULL,
                            ""OrderId"" uuid NULL,
                            ""BankTransactionId"" character varying(100) NOT NULL DEFAULT '',
                            ""Gateway"" character varying(50) NOT NULL DEFAULT '',
                            ""AmountIn"" numeric(18,2) NOT NULL DEFAULT 0,
                            ""Accumulated"" numeric(18,2) NULL,
                            ""TransactionContent"" text NOT NULL DEFAULT '',
                            ""ReferenceNumber"" character varying(100) NULL,
                            ""TransactionDate"" timestamp without time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
                            ""VerifiedBySepayApi"" boolean NOT NULL DEFAULT false,
                            ""RawPayloadJson"" text NULL,
                            ""CreatedBy"" uuid,
                            ""CreatedAt"" timestamp without time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
                            ""UpdatedAt"" timestamp without time zone,
                            ""IsDeleted"" boolean NOT NULL DEFAULT false
                        );

                        ALTER TABLE IF EXISTS ""StorePaymentConfigs"" ADD COLUMN IF NOT EXISTS ""CashOutApprovalThreshold"" numeric(18,2) DEFAULT 200000;
                        ALTER TABLE IF EXISTS ""StorePaymentConfigs"" ADD COLUMN IF NOT EXISTS ""CashOutShiftTotalLimit"" numeric(18,2) DEFAULT 0;

                        ALTER TABLE IF EXISTS ""Products"" ADD COLUMN IF NOT EXISTS ""TaxCategory"" text DEFAULT 'STANDARD_1PCT';
                        ALTER TABLE IF EXISTS ""Products"" ADD COLUMN IF NOT EXISTS ""EligibleForVatReduction"" boolean DEFAULT false;
                        ALTER TABLE IF EXISTS ""Products"" ALTER COLUMN ""Barcode"" DROP NOT NULL;
                        ALTER TABLE IF EXISTS ""Categories"" DROP COLUMN IF EXISTS ""DisplayOrder"";
                        ALTER TABLE IF EXISTS ""Categories"" DROP COLUMN IF EXISTS ""ParentCategoryID"";
                        ALTER TABLE IF EXISTS ""ProductUnits"" ADD COLUMN IF NOT EXISTS ""CreatedBy"" uuid;
                        ALTER TABLE IF EXISTS ""ProductUnits"" ADD COLUMN IF NOT EXISTS ""TenantId"" character varying(50);
                        ALTER TABLE IF EXISTS ""ProductUnits"" ADD COLUMN IF NOT EXISTS ""CreatedAt"" timestamp without time zone DEFAULT CURRENT_TIMESTAMP;
                        ALTER TABLE IF EXISTS ""ProductUnits"" ADD COLUMN IF NOT EXISTS ""UpdatedAt"" timestamp without time zone;
                        ALTER TABLE IF EXISTS ""ProductUnits"" ADD COLUMN IF NOT EXISTS ""IsDeleted"" boolean DEFAULT false;
                        ALTER TABLE IF EXISTS ""ProductUnits"" ADD COLUMN IF NOT EXISTS ""IsPurchaseUnit"" boolean NOT NULL DEFAULT false;

                        ALTER TABLE IF EXISTS ""BranchProductUnitPrices"" ADD COLUMN IF NOT EXISTS ""CreatedBy"" uuid;
                        ALTER TABLE IF EXISTS ""BranchProductUnitPrices"" ADD COLUMN IF NOT EXISTS ""TenantId"" character varying(50);
                        ALTER TABLE IF EXISTS ""BranchProductUnitPrices"" ADD COLUMN IF NOT EXISTS ""CreatedAt"" timestamp without time zone DEFAULT CURRENT_TIMESTAMP;
                        ALTER TABLE IF EXISTS ""BranchProductUnitPrices"" ADD COLUMN IF NOT EXISTS ""UpdatedAt"" timestamp without time zone;
                        ALTER TABLE IF EXISTS ""BranchProductUnitPrices"" ADD COLUMN IF NOT EXISTS ""IsDeleted"" boolean DEFAULT false;

                        ALTER TABLE IF EXISTS ""OrderDetails"" ADD COLUMN IF NOT EXISTS ""CreatedBy"" uuid;
                        ALTER TABLE IF EXISTS ""OrderDetails"" ADD COLUMN IF NOT EXISTS ""TenantId"" character varying(50);
                        ALTER TABLE IF EXISTS ""OrderDetails"" ADD COLUMN IF NOT EXISTS ""CreatedAt"" timestamp without time zone DEFAULT CURRENT_TIMESTAMP;
                        ALTER TABLE IF EXISTS ""OrderDetails"" ADD COLUMN IF NOT EXISTS ""UpdatedAt"" timestamp without time zone;
                        ALTER TABLE IF EXISTS ""OrderDetails"" ADD COLUMN IF NOT EXISTS ""IsDeleted"" boolean DEFAULT false;

                        ALTER TABLE IF EXISTS ""Suppliers"" DROP COLUMN IF EXISTS ""CategoryId"";
                        ALTER TABLE IF EXISTS ""Suppliers"" DROP COLUMN IF EXISTS ""CategoryCode"";
                        ALTER TABLE IF EXISTS ""SupplierDeliveryConfigs"" DROP COLUMN IF EXISTS ""LeadTimeHours"";
                        ALTER TABLE IF EXISTS ""SupplierDeliveryConfigs"" DROP COLUMN IF EXISTS ""LeadTimeDays"";
                        DROP TABLE IF EXISTS ""SupplierCategories"" CASCADE;
                        DROP TABLE IF EXISTS ""ProductCombos"";
                        DROP INDEX IF EXISTS ""IX_Products_TenantID_ProductCode"";
                        CREATE UNIQUE INDEX IF NOT EXISTS ""IX_Products_TenantID_ProductCode"" ON ""Products"" (""TenantID"", ""ProductCode"") WHERE ""IsDeleted"" = false;
                        DROP INDEX IF EXISTS ""IX_Products_Barcode"";
                        CREATE UNIQUE INDEX IF NOT EXISTS ""IX_Products_Barcode"" ON ""Products"" (""Barcode"") WHERE ""IsDeleted"" = false AND ""Barcode"" IS NOT NULL;
                        CREATE TABLE IF NOT EXISTS ""ProductPriceHistories"" (
                            ""Id"" uuid NOT NULL PRIMARY KEY,
                            ""TenantId"" character varying(50) NOT NULL,
                            ""ProductId"" uuid NOT NULL,
                            ""BranchId"" uuid NULL,
                            ""ProductUnitId"" uuid NULL,
                            ""PriceScope"" character varying(20) NOT NULL DEFAULT 'MASTER',
                            ""PriceType"" character varying(20) NOT NULL DEFAULT 'SELLING_PRICE',
                            ""OldPrice"" numeric(18,2) NOT NULL,
                            ""NewPrice"" numeric(18,2) NOT NULL,
                            ""CostPriceSnapshot"" numeric(18,2) NULL,
                            ""IsBelowCost"" boolean NOT NULL DEFAULT false,
                            ""ManagerApprovedBy"" character varying(100) NULL,
                            ""ManagerApprovalReason"" character varying(255) NULL,
                            ""ChangedByUserId"" character varying(50) NOT NULL,
                            ""ChangedByUserName"" character varying(100) NOT NULL,
                            ""Reason"" character varying(255) NOT NULL DEFAULT 'Điều chỉnh giá bán',
                            ""CreatedBy"" uuid NULL,
                            ""CreatedAt"" timestamp without time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
                            ""UpdatedAt"" timestamp without time zone NULL,
                            ""IsDeleted"" boolean NOT NULL DEFAULT false
                        );
                        CREATE INDEX IF NOT EXISTS ""IX_ProductPriceHistories_Tenant_Product"" ON ""ProductPriceHistories"" (""TenantId"", ""ProductId"", ""CreatedAt"" DESC);
                        CREATE INDEX IF NOT EXISTS ""IX_ProductPriceHistories_Tenant_Branch"" ON ""ProductPriceHistories"" (""TenantId"", ""BranchId"");
                    ");
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "Cảnh báo khi chạy DDL nâng cấp Products/Categories/Suppliers");
                }

                try
                {
                    await context.Database.ExecuteSqlRawAsync(@"
                        ALTER TABLE IF EXISTS ""Employees"" ADD COLUMN IF NOT EXISTS ""FaceDescriptor"" text;
                        ALTER TABLE IF EXISTS ""Employees"" ADD COLUMN IF NOT EXISTS ""FaceImageUrls"" text;
                        ALTER TABLE IF EXISTS ""Employees"" ADD COLUMN IF NOT EXISTS ""FaceRegisteredAt"" timestamp without time zone;
                        ALTER TABLE IF EXISTS ""EmployeeSchedules"" ADD COLUMN IF NOT EXISTS ""CheckInFaceImageUrl"" text;
                        ALTER TABLE IF EXISTS ""EmployeeSchedules"" ADD COLUMN IF NOT EXISTS ""CheckOutFaceImageUrl"" text;
                        ALTER TABLE IF EXISTS ""EmployeeSchedules"" ADD COLUMN IF NOT EXISTS ""CheckInMethod"" text;
                        ALTER TABLE IF EXISTS ""EmployeeSchedules"" ADD COLUMN IF NOT EXISTS ""CheckOutMethod"" text;
                    ");
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "Cảnh báo khi chạy DDL nâng cấp Employees/EmployeeSchedules");
                }

                // 1. Bảng Chính sách chấm công Chi nhánh (BranchAttendancePolicies)
                try
                {
                    await context.Database.ExecuteSqlRawAsync(@"
                        CREATE TABLE IF NOT EXISTS ""BranchAttendancePolicies"" (
                            ""BranchAttendancePolicyID"" uuid NOT NULL PRIMARY KEY,
                            ""BranchID"" uuid NOT NULL,
                            ""TenantID"" character varying(50) NOT NULL,
                            ""StandardDayHours"" numeric(4,2) NOT NULL DEFAULT 8.00,
                            ""IsConvertHoursToDays"" boolean NOT NULL DEFAULT true,
                            ""ConversionRulesJson"" text,
                            ""EnableLateThreshold"" boolean NOT NULL DEFAULT true,
                            ""LateThresholdMinutes"" integer NOT NULL DEFAULT 0,
                            ""EnableEarlyLeaveThreshold"" boolean NOT NULL DEFAULT true,
                            ""EarlyLeaveThresholdMinutes"" integer NOT NULL DEFAULT 0,
                            ""EnableOtBeforeShift"" boolean NOT NULL DEFAULT true,
                            ""OtBeforeShiftMinutes"" integer NOT NULL DEFAULT 0,
                            ""EnableOtAfterShift"" boolean NOT NULL DEFAULT true,
                            ""OtAfterShiftMinutes"" integer NOT NULL DEFAULT 0,
                            ""CreatedAt"" timestamp without time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
                            ""UpdatedAt"" timestamp without time zone,
                            ""IsDeleted"" boolean NOT NULL DEFAULT false,
                            ""CreatedBy"" uuid
                        );
                        CREATE UNIQUE INDEX IF NOT EXISTS ""IX_BranchAttendancePolicies_TenantID_BranchID"" 
                        ON ""BranchAttendancePolicies"" (""TenantID"", ""BranchID"") WHERE ""IsDeleted"" = false;
                        CREATE INDEX IF NOT EXISTS ""IX_BranchAttendancePolicies_BranchID"" 
                        ON ""BranchAttendancePolicies"" (""BranchID"");
                    ");
                }
                catch (Exception ex) { logger?.LogWarning(ex, "BranchAttendancePolicies DDL warning"); }

                // 2. Bảng Danh mục ca làm việc (ShiftConfigs)
                try
                {
                    await context.Database.ExecuteSqlRawAsync(@"
                        CREATE TABLE IF NOT EXISTS ""ShiftConfigs"" (
                            ""ShiftConfigID"" uuid NOT NULL PRIMARY KEY,
                            ""TenantID"" character varying(50) NOT NULL,
                            ""BranchID"" uuid NOT NULL,
                            ""ShiftCode"" character varying(30) NOT NULL,
                            ""ShiftName"" character varying(100) NOT NULL,
                            ""StartTime"" time without time zone NOT NULL,
                            ""EndTime"" time without time zone NOT NULL,
                            ""DurationHours"" numeric(4,2) NOT NULL DEFAULT 4.00,
                            ""AllowedCheckInStart"" time without time zone NOT NULL DEFAULT '00:00:00',
                            ""AllowedCheckInEnd"" time without time zone NOT NULL DEFAULT '23:59:59',
                            ""IsCheckInNextDay"" boolean NOT NULL DEFAULT false,
                            ""BranchIds"" character varying(2000),
                            ""Description"" character varying(255),
                            ""IsActive"" boolean NOT NULL DEFAULT true,
                            ""CreatedAt"" timestamp without time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
                            ""UpdatedAt"" timestamp without time zone,
                            ""IsDeleted"" boolean NOT NULL DEFAULT false,
                            ""CreatedBy"" uuid
                        );
                        CREATE UNIQUE INDEX IF NOT EXISTS ""IX_ShiftConfigs_TenantID_BranchID_ShiftCode"" 
                        ON ""ShiftConfigs"" (""TenantID"", ""BranchID"", ""ShiftCode"") WHERE ""IsDeleted"" = false;
                    ");
                }
                catch (Exception ex) { logger?.LogWarning(ex, "ShiftConfigs DDL warning"); }

                // 3. Bảng Hồ sơ nhân viên (Employees)
                try
                {
                    await context.Database.ExecuteSqlRawAsync(@"
                        CREATE TABLE IF NOT EXISTS ""Employees"" (
                            ""EmployeeID"" uuid NOT NULL PRIMARY KEY,
                            ""TenantID"" character varying(50) NOT NULL,
                            ""BranchID"" uuid NOT NULL,
                            ""UserID"" character varying(50),
                            ""EmployeeCode"" character varying(30) NOT NULL,
                            ""FullName"" character varying(100) NOT NULL,
                            ""CitizenId"" character varying(20) NOT NULL,
                            ""DateOfBirth"" timestamp without time zone,
                            ""Gender"" character varying(10),
                            ""Phone"" character varying(20) NOT NULL,
                            ""Email"" character varying(100),
                            ""Address"" character varying(255),
                            ""StartDate"" timestamp without time zone,
                            ""Note"" character varying(500),
                            ""AvatarUrl"" character varying(255),
                            ""OtWeekday"" character varying(20),
                            ""OtSunday"" character varying(20),
                            ""SalaryType"" character varying(30) NOT NULL DEFAULT 'monthly',
                            ""BaseRate"" numeric(18,2) NOT NULL DEFAULT 0.00,
                            ""BankAccountNumber"" character varying(30),
                            ""BankName"" character varying(100),
                            ""BankAccountHolder"" character varying(100),
                            ""DebtAmount"" numeric(18,2) NOT NULL DEFAULT 0.00,
                            ""Status"" character varying(30) NOT NULL DEFAULT 'active',
                            ""CreatedAt"" timestamp without time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
                            ""UpdatedAt"" timestamp without time zone,
                            ""IsDeleted"" boolean NOT NULL DEFAULT false,
                            ""CreatedBy"" uuid
                        );
                        CREATE UNIQUE INDEX IF NOT EXISTS ""IX_Employees_TenantID_CitizenId"" 
                        ON ""Employees"" (""TenantID"", ""CitizenId"") WHERE ""IsDeleted"" = false;
                        CREATE UNIQUE INDEX IF NOT EXISTS ""IX_Employees_TenantID_EmployeeCode"" 
                        ON ""Employees"" (""TenantID"", ""EmployeeCode"") WHERE ""IsDeleted"" = false;
                    ");
                }
                catch (Exception ex) { logger?.LogWarning(ex, "Employees DDL warning"); }

                // 4. Bảng Phụ cấp nhân viên (EmployeeAllowances)
                try
                {
                    await context.Database.ExecuteSqlRawAsync(@"
                        CREATE TABLE IF NOT EXISTS ""EmployeeAllowances"" (
                            ""AllowanceID"" uuid NOT NULL PRIMARY KEY,
                            ""TenantID"" character varying(50) NOT NULL,
                            ""EmployeeID"" uuid NOT NULL,
                            ""Name"" character varying(100) NOT NULL,
                            ""Amount"" numeric(18,2) NOT NULL DEFAULT 0.00,
                            ""CreatedAt"" timestamp without time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
                            ""UpdatedAt"" timestamp without time zone,
                            ""IsDeleted"" boolean NOT NULL DEFAULT false,
                            ""CreatedBy"" uuid
                        );
                    ");
                }
                catch (Exception ex) { logger?.LogWarning(ex, "EmployeeAllowances DDL warning"); }

                // 5. Bảng Giao dịch Sổ quỹ liên kết lương (CashbookTransaction)
                try
                {
                    await context.Database.ExecuteSqlRawAsync(@"
                        CREATE TABLE IF NOT EXISTS ""CashbookTransaction"" (
                            ""Id"" uuid NOT NULL PRIMARY KEY,
                            ""TransactionCode"" text NOT NULL,
                            ""BranchId"" uuid NOT NULL,
                            ""Type"" integer NOT NULL,
                            ""Category"" text NOT NULL,
                            ""Amount"" numeric NOT NULL,
                            ""ReferenceCode"" text NOT NULL,
                            ""Description"" text NOT NULL,
                            ""CreatedByUserId"" uuid NOT NULL,
                            ""CreatedAt"" timestamp without time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
                            ""UpdatedAt"" timestamp without time zone,
                            ""IsDeleted"" boolean NOT NULL DEFAULT false,
                            ""TenantId"" text NOT NULL,
                            ""CreatedBy"" uuid
                        );
                    ");
                }
                catch (Exception ex) { logger?.LogWarning(ex, "CashbookTransaction DDL warning"); }

                // 6. Bảng Phiếu lương (Payslips)
                try
                {
                    await context.Database.ExecuteSqlRawAsync(@"
                        CREATE TABLE IF NOT EXISTS ""Payslips"" (
                            ""PayslipID"" uuid NOT NULL PRIMARY KEY,
                            ""TenantId"" character varying(64) NOT NULL,
                            ""BranchID"" uuid NOT NULL,
                            ""EmployeeID"" uuid NOT NULL,
                            ""CashbookTransactionId"" uuid,
                            ""CashbookVoucherCode"" character varying(50),
                            ""PayslipCode"" character varying(50) NOT NULL,
                            ""PeriodMonth"" character varying(7) NOT NULL,
                            ""SalaryType"" character varying(30) NOT NULL DEFAULT 'MONTHLY',
                            ""BaseRate"" numeric(18,2) NOT NULL DEFAULT 0.00,
                            ""StandardDays"" numeric(5,2) NOT NULL DEFAULT 26.00,
                            ""StandardDayHours"" numeric(5,2) NOT NULL DEFAULT 8.00,
                            ""WorkedDays"" numeric(5,2) NOT NULL DEFAULT 0.00,
                            ""TotalWorkHours"" numeric(7,2) NOT NULL DEFAULT 0.00,
                            ""TotalShifts"" integer NOT NULL DEFAULT 0,
                            ""OtHours"" numeric(5,2) NOT NULL DEFAULT 0.00,
                            ""PaidLeaveDays"" numeric(5,2) NOT NULL DEFAULT 0.00,
                            ""UnpaidLeaveDays"" numeric(5,2) NOT NULL DEFAULT 0.00,
                            ""TotalLateMinutes"" integer NOT NULL DEFAULT 0,
                            ""TotalEarlyLeaveMinutes"" integer NOT NULL DEFAULT 0,
                            ""BaseSalaryEarned"" numeric(18,2) NOT NULL DEFAULT 0.00,
                            ""OvertimePay"" numeric(18,2) NOT NULL DEFAULT 0.00,
                            ""TotalAllowance"" numeric(18,2) NOT NULL DEFAULT 0.00,
                            ""AllowancesDetailJson"" text,
                            ""TotalEarnings"" numeric(18,2) NOT NULL DEFAULT 0.00,
                            ""AdvanceDeduction"" numeric(18,2) NOT NULL DEFAULT 0.00,
                            ""RemainingDebt"" numeric(18,2) NOT NULL DEFAULT 0.00,
                            ""SocialInsurance"" numeric(18,2) NOT NULL DEFAULT 0.00,
                            ""TotalDeductions"" numeric(18,2) NOT NULL DEFAULT 0.00,
                            ""NetSalary"" numeric(18,2) NOT NULL DEFAULT 0.00,
                            ""Status"" character varying(30) NOT NULL DEFAULT 'DRAFT',
                            ""PaymentMethod"" character varying(30) NOT NULL DEFAULT 'CASH',
                            ""PaidAt"" timestamp without time zone,
                            ""PaidBy"" character varying(100),
                            ""BankAccountNumber"" character varying(30),
                            ""BankName"" character varying(100),
                            ""BankAccountHolder"" character varying(100),
                            ""Note"" character varying(500),
                            ""CreatedAt"" timestamp without time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
                            ""UpdatedAt"" timestamp without time zone,
                            ""IsDeleted"" boolean NOT NULL DEFAULT false,
                            ""CreatedBy"" uuid
                        );
                        ALTER TABLE IF EXISTS ""Payslips"" ADD COLUMN IF NOT EXISTS ""TenantId"" character varying(64);
                        ALTER TABLE IF EXISTS ""Payslips"" ADD COLUMN IF NOT EXISTS ""TenantID"" character varying(64);
                        ALTER TABLE IF EXISTS ""Payslips"" ADD COLUMN IF NOT EXISTS ""BranchID"" uuid;
                        ALTER TABLE IF EXISTS ""Payslips"" ADD COLUMN IF NOT EXISTS ""PeriodMonth"" character varying(7);
                        ALTER TABLE IF EXISTS ""Payslips"" ADD COLUMN IF NOT EXISTS ""UnpaidLeaveDays"" numeric(5,2) DEFAULT 0.00;
                        ALTER TABLE IF EXISTS ""Payslips"" ADD COLUMN IF NOT EXISTS ""CashbookTransactionId"" uuid;

                        DO $$
                        BEGIN
                            IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'Payslips' AND column_name = 'BranchID') THEN
                                IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'Payslips' AND column_name = 'TenantId') THEN
                                    CREATE INDEX IF NOT EXISTS ""IX_Payslips_TenantId_BranchID_PeriodMonth"" ON ""Payslips"" (""TenantId"", ""BranchID"", ""PeriodMonth"");
                                ELSIF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'Payslips' AND column_name = 'TenantID') THEN
                                    CREATE INDEX IF NOT EXISTS ""IX_Payslips_TenantID_BranchID_PeriodMonth"" ON ""Payslips"" (""TenantID"", ""BranchID"", ""PeriodMonth"");
                                END IF;
                            END IF;
                        END $$;
                    ");
                }
                catch (Exception ex) { logger?.LogWarning(ex, "Payslips DDL warning"); }

                // 7. Bảng Lịch phân ca & Chấm công (EmployeeSchedules)
                try
                {
                    await context.Database.ExecuteSqlRawAsync(@"
                        CREATE TABLE IF NOT EXISTS ""EmployeeSchedules"" (
                            ""ScheduleID"" uuid NOT NULL PRIMARY KEY,
                            ""TenantId"" character varying(50) NOT NULL,
                            ""BranchID"" uuid NOT NULL,
                            ""EmployeeID"" uuid NOT NULL,
                            ""ShiftConfigID"" uuid,
                            ""PayslipID"" uuid,
                            ""WorkDate"" date NOT NULL,
                            ""ShiftCode"" character varying(30) NOT NULL,
                            ""ShiftName"" character varying(100) NOT NULL,
                            ""PlannedStartTime"" time without time zone NOT NULL DEFAULT '08:00:00',
                            ""PlannedEndTime"" time without time zone NOT NULL DEFAULT '17:00:00',
                            ""DurationHours"" numeric(4,2) NOT NULL DEFAULT 4.00,
                            ""AllowedCheckInStart"" time without time zone NOT NULL DEFAULT '00:00:00',
                            ""AllowedCheckInEnd"" time without time zone NOT NULL DEFAULT '23:59:59',
                            ""IsCheckInNextDay"" boolean NOT NULL DEFAULT false,
                            ""CheckInTime"" timestamp without time zone,
                            ""CheckInMethod"" character varying(30),
                            ""CheckInFaceImageUrl"" character varying(255),
                            ""FaceMatchScore"" numeric(5,2),
                            ""CheckOutTime"" timestamp without time zone,
                            ""CheckOutMethod"" character varying(30),
                            ""CheckOutFaceImageUrl"" character varying(255),
                            ""LateMinutes"" integer NOT NULL DEFAULT 0,
                            ""EarlyLeaveMinutes"" integer NOT NULL DEFAULT 0,
                            ""OvertimeMinutes"" integer NOT NULL DEFAULT 0,
                            ""ActualWorkHours"" numeric(4,2) NOT NULL DEFAULT 0.00,
                            ""Status"" character varying(30) NOT NULL DEFAULT 'draft',
                            ""ApprovalType"" character varying(30),
                            ""ApprovalStatus"" character varying(30) NOT NULL DEFAULT 'NONE',
                            ""ApprovalReason"" character varying(255),
                            ""EvidenceImageUrl"" character varying(255),
                            ""ApprovedBy"" character varying(100),
                            ""ApprovedAt"" timestamp without time zone,
                            ""CreatedAt"" timestamp without time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
                            ""UpdatedAt"" timestamp without time zone,
                            ""IsDeleted"" boolean NOT NULL DEFAULT false,
                            ""CreatedBy"" uuid
                        );
                        CREATE INDEX IF NOT EXISTS ""IX_EmployeeSchedules_TenantId_BranchID_WorkDate"" 
                        ON ""EmployeeSchedules"" (""TenantId"", ""BranchID"", ""WorkDate"");
                    ");
                }
                catch (Exception ex) { logger?.LogWarning(ex, "EmployeeSchedules DDL warning"); }

                // 8. Bổ sung các cột mới cho các bảng HR hiện hữu (nếu đã có bảng từ trước)
                try
                {
                    await context.Database.ExecuteSqlRawAsync(@"
                        ALTER TABLE IF EXISTS ""Employees"" ADD COLUMN IF NOT EXISTS ""Address"" character varying(255);
                        ALTER TABLE IF EXISTS ""Employees"" ADD COLUMN IF NOT EXISTS ""Note"" character varying(500);
                        ALTER TABLE IF EXISTS ""Employees"" ADD COLUMN IF NOT EXISTS ""OtSunday"" character varying(20);
                        ALTER TABLE IF EXISTS ""Employees"" ADD COLUMN IF NOT EXISTS ""OtWeekday"" character varying(20);
                        ALTER TABLE IF EXISTS ""Employees"" ADD COLUMN IF NOT EXISTS ""StartDate"" timestamp without time zone;
                        ALTER TABLE IF EXISTS ""Employees"" ADD COLUMN IF NOT EXISTS ""DateOfBirth"" timestamp without time zone;
                        ALTER TABLE IF EXISTS ""Employees"" ADD COLUMN IF NOT EXISTS ""AvatarUrl"" character varying(500);
                        ALTER TABLE IF EXISTS ""Employees"" ADD COLUMN IF NOT EXISTS ""FaceDescriptor"" text;
                        ALTER TABLE IF EXISTS ""Employees"" ADD COLUMN IF NOT EXISTS ""FaceImageUrls"" text;
                        ALTER TABLE IF EXISTS ""Employees"" ADD COLUMN IF NOT EXISTS ""FaceRegisteredAt"" timestamp without time zone;
                        ALTER TABLE IF EXISTS ""Employees"" ADD COLUMN IF NOT EXISTS ""DebtAmount"" numeric(18,2) DEFAULT 0;

                        ALTER TABLE IF EXISTS ""EmployeeSchedules"" ADD COLUMN IF NOT EXISTS ""BranchID"" uuid;
                        ALTER TABLE IF EXISTS ""EmployeeSchedules"" ADD COLUMN IF NOT EXISTS ""ShiftConfigID"" uuid;
                        ALTER TABLE IF EXISTS ""EmployeeSchedules"" ADD COLUMN IF NOT EXISTS ""PayslipID"" uuid;
                        ALTER TABLE IF EXISTS ""EmployeeSchedules"" ADD COLUMN IF NOT EXISTS ""DurationHours"" numeric(4,2) DEFAULT 4.00;
                        ALTER TABLE IF EXISTS ""EmployeeSchedules"" ADD COLUMN IF NOT EXISTS ""CheckInTime"" timestamp without time zone;
                        ALTER TABLE IF EXISTS ""EmployeeSchedules"" ADD COLUMN IF NOT EXISTS ""CheckInMethod"" character varying(30);
                        ALTER TABLE IF EXISTS ""EmployeeSchedules"" ADD COLUMN IF NOT EXISTS ""CheckInFaceImageUrl"" character varying(255);
                        ALTER TABLE IF EXISTS ""EmployeeSchedules"" ADD COLUMN IF NOT EXISTS ""FaceMatchScore"" numeric(5,2);
                        ALTER TABLE IF EXISTS ""EmployeeSchedules"" ADD COLUMN IF NOT EXISTS ""CheckOutTime"" timestamp without time zone;
                        ALTER TABLE IF EXISTS ""EmployeeSchedules"" ADD COLUMN IF NOT EXISTS ""CheckOutMethod"" character varying(30);
                        ALTER TABLE IF EXISTS ""EmployeeSchedules"" ADD COLUMN IF NOT EXISTS ""CheckOutFaceImageUrl"" character varying(255);
                        ALTER TABLE IF EXISTS ""EmployeeSchedules"" ADD COLUMN IF NOT EXISTS ""LateMinutes"" integer DEFAULT 0;
                        ALTER TABLE IF EXISTS ""EmployeeSchedules"" ADD COLUMN IF NOT EXISTS ""ActualWorkHours"" numeric(4,2) DEFAULT 0.00;
                        ALTER TABLE IF EXISTS ""EmployeeSchedules"" ADD COLUMN IF NOT EXISTS ""IsLeave"" boolean DEFAULT false;
                        ALTER TABLE IF EXISTS ""EmployeeSchedules"" ADD COLUMN IF NOT EXISTS ""IsPaidLeave"" boolean DEFAULT false;
                        ALTER TABLE IF EXISTS ""EmployeeSchedules"" ADD COLUMN IF NOT EXISTS ""ApprovalType"" character varying(30);
                        ALTER TABLE IF EXISTS ""EmployeeSchedules"" ADD COLUMN IF NOT EXISTS ""ApprovalStatus"" character varying(30) DEFAULT 'NONE';
                        ALTER TABLE IF EXISTS ""EmployeeSchedules"" ADD COLUMN IF NOT EXISTS ""ApprovalReason"" character varying(255);
                        ALTER TABLE IF EXISTS ""EmployeeSchedules"" ADD COLUMN IF NOT EXISTS ""EvidenceImageUrl"" character varying(255);
                        ALTER TABLE IF EXISTS ""EmployeeSchedules"" ADD COLUMN IF NOT EXISTS ""ApprovedBy"" character varying(100);
                        ALTER TABLE IF EXISTS ""EmployeeSchedules"" ADD COLUMN IF NOT EXISTS ""EarlyLeaveMinutes"" integer DEFAULT 0;
                        ALTER TABLE IF EXISTS ""EmployeeSchedules"" ADD COLUMN IF NOT EXISTS ""OvertimeMinutes"" integer DEFAULT 0;
                        ALTER TABLE IF EXISTS ""EmployeeSchedules"" ADD COLUMN IF NOT EXISTS ""IsCheckInNextDay"" boolean DEFAULT false;
                        ALTER TABLE IF EXISTS ""EmployeeSchedules"" ADD COLUMN IF NOT EXISTS ""TenantId"" text;
                        ALTER TABLE IF EXISTS ""EmployeeSchedules"" ADD COLUMN IF NOT EXISTS ""CreatedBy"" uuid;
                        ALTER TABLE IF EXISTS ""EmployeeSchedules"" ADD COLUMN IF NOT EXISTS ""ApprovedAt"" timestamp without time zone;
                        ALTER TABLE IF EXISTS ""EmployeeSchedules"" ADD COLUMN IF NOT EXISTS ""PlannedStartTime"" time without time zone DEFAULT '08:00:00';
                        ALTER TABLE IF EXISTS ""EmployeeSchedules"" ADD COLUMN IF NOT EXISTS ""PlannedEndTime"" time without time zone DEFAULT '17:00:00';
                        ALTER TABLE IF EXISTS ""EmployeeSchedules"" ADD COLUMN IF NOT EXISTS ""AllowedCheckInStart"" time without time zone DEFAULT '00:00:00';
                        ALTER TABLE IF EXISTS ""EmployeeSchedules"" ADD COLUMN IF NOT EXISTS ""AllowedCheckInEnd"" time without time zone DEFAULT '23:59:59';

                        ALTER TABLE IF EXISTS ""ShiftConfigs"" ADD COLUMN IF NOT EXISTS ""BranchIds"" character varying(2000);
                        ALTER TABLE IF EXISTS ""ShiftConfigs"" ADD COLUMN IF NOT EXISTS ""IsCheckInNextDay"" boolean DEFAULT false;
                        ALTER TABLE IF EXISTS ""ShiftConfigs"" ADD COLUMN IF NOT EXISTS ""AllowedCheckInStart"" time without time zone DEFAULT '00:00:00';
                        ALTER TABLE IF EXISTS ""ShiftConfigs"" ADD COLUMN IF NOT EXISTS ""AllowedCheckInEnd"" time without time zone DEFAULT '23:59:59';

                        ALTER TABLE IF EXISTS ""Payslips"" ADD COLUMN IF NOT EXISTS ""UnpaidLeaveDays"" numeric(5,2) DEFAULT 0.00;
                        ALTER TABLE IF EXISTS ""Payslips"" ADD COLUMN IF NOT EXISTS ""CashbookTransactionId"" uuid;
                        ALTER TABLE IF EXISTS ""Payslips"" ADD COLUMN IF NOT EXISTS ""CashbookVoucherCode"" character varying(50);
                        ALTER TABLE IF EXISTS ""Payslips"" ADD COLUMN IF NOT EXISTS ""PaidBy"" character varying(100);
                        ALTER TABLE IF EXISTS ""Payslips"" ADD COLUMN IF NOT EXISTS ""BankAccountNumber"" character varying(30);
                        ALTER TABLE IF EXISTS ""Payslips"" ADD COLUMN IF NOT EXISTS ""BankName"" character varying(100);
                        ALTER TABLE IF EXISTS ""Payslips"" ADD COLUMN IF NOT EXISTS ""BankAccountHolder"" character varying(100);
                    ");
                }
                catch (Exception ex) { logger?.LogWarning(ex, "HR Column Alter DDL warning"); }

                // 9. Khóa ngoại liên kết
                try
                {
                    await context.Database.ExecuteSqlRawAsync(@"
                        DO $$
                        BEGIN
                            IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_BranchAttendancePolicies_Branches_BranchID') THEN
                                ALTER TABLE ""BranchAttendancePolicies"" 
                                ADD CONSTRAINT ""FK_BranchAttendancePolicies_Branches_BranchID"" 
                                FOREIGN KEY (""BranchID"") REFERENCES ""Branches"" (""BranchID"") ON DELETE RESTRICT;
                            END IF;
                        END $$;
                    ");
                }
                catch { }

                try
                {
                    await EnsureSupplierDispatchSchemaAsync(context, logger);
                    await EnsureBranchColumnsAsync(context, logger);
                    await EnsureTenantSubscriptionColumnsAsync(context, logger);
                    await EnsureBranchProductsSchemaAsync(context, logger);
                    await EnsureMissingModelSchemaAsync(context, logger);
                }
                catch (Exception ex)
                {
                    logger?.LogError(ex, "Lỗi khi chạy EnsureSupplierDispatchSchemaAsync / EnsureBranchColumnsAsync");
                }

                // Đánh dấu các migration hiện tại đã được áp dụng sau khi toàn bộ DDL đồng bộ đã hoàn tất
                try
                {
                    var migrations = context.Database.GetMigrations();
                    foreach (var m in migrations)
                    {
                        await context.Database.ExecuteSqlRawAsync(
                            "INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ({0}, '8.0.11') ON CONFLICT DO NOTHING;",
                            m);
                    }
                }
                catch { }
            }
            else
            {
                await context.Database.EnsureCreatedAsync();
            }

        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Lỗi trong quá trình kiểm tra cấu trúc CSDL hoặc migrations (bỏ qua nếu bảng đã tồn tại).");
        }

        // 2. PHÂN HỆ SAAS: Nạp danh sách các gói cước SubscriptionPlans (Idempotent: TRIAL 0k, STANDARD 99k & PRO 249k)
        var planTrial = await context.SubscriptionPlans.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.PlanCode == "TRIAL");
        if (planTrial == null)
        {
            planTrial = new SubscriptionPlan
            {
                Id = "plan_trial",
                PlanCode = "TRIAL",
                PlanName = "Gói Dùng Thử (Trải Nghiệm)",
                PriceMonthly = 0m,
                PriceYearly = 0m,
                MaxBranches = 1,
                MaxStaff = 5,
                EnabledModulesJson = "[\"Catalog\",\"Pos\",\"Inventory\",\"Finance\",\"HR\"]",
                IsActive = true
            };
            await context.SubscriptionPlans.AddAsync(planTrial);
        }
        else
        {
            planTrial.PlanName = "Gói Dùng Thử (Trải Nghiệm)";
            planTrial.PriceMonthly = 0m;
            planTrial.PriceYearly = 0m;
            planTrial.MaxBranches = 1;
            planTrial.MaxStaff = 5;
            planTrial.EnabledModulesJson = "[\"Catalog\",\"Pos\",\"Inventory\",\"Finance\",\"HR\"]";
            planTrial.IsActive = true;
        }

        var planStandard = await context.SubscriptionPlans.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.PlanCode == "STANDARD");
        if (planStandard == null)
        {
            planStandard = new SubscriptionPlan
            {
                Id = "plan_standard",
                PlanCode = "STANDARD",
                PlanName = "Gói Thường (Cửa Hàng Độc Lập)",
                PriceMonthly = 99000.00m,
                PriceYearly = 1188000.00m,
                MaxBranches = 1,
                MaxStaff = 1,
                EnabledModulesJson = "[\"Catalog\",\"Pos\",\"Inventory\",\"Finance\"]",
                IsActive = true
            };
            await context.SubscriptionPlans.AddAsync(planStandard);
        }
        else
        {
            planStandard.PlanName = "Gói Thường (Cửa Hàng Độc Lập)";
            planStandard.PriceMonthly = 99000.00m;
            planStandard.PriceYearly = 1188000.00m;
            planStandard.MaxBranches = 1;
            planStandard.MaxStaff = 1;
            planStandard.EnabledModulesJson = "[\"Catalog\",\"Pos\",\"Inventory\",\"Finance\"]";
            planStandard.IsActive = true;
        }

        var planPro = await context.SubscriptionPlans.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.PlanCode == "PRO");
        if (planPro == null)
        {
            planPro = new SubscriptionPlan
            {
                Id = "plan_pro",
                PlanCode = "PRO",
                PlanName = "Gói Pro (Chuỗi Cửa Hàng)",
                PriceMonthly = 249000.00m,
                PriceYearly = 2988000.00m,
                MaxBranches = 2,
                MaxStaff = 100,
                EnabledModulesJson = "[\"Catalog\",\"Pos\",\"Inventory\",\"Supplier\",\"Finance\",\"HR\",\"Tax\",\"MultiBranch\"]",
                IsActive = true
            };
            await context.SubscriptionPlans.AddAsync(planPro);
        }
        else
        {
            planPro.PlanName = "Gói Pro (Chuỗi Cửa Hàng)";
            planPro.PriceMonthly = 249000.00m;
            planPro.PriceYearly = 2988000.00m;
            planPro.MaxBranches = 2;
            planPro.MaxStaff = 100;
            planPro.EnabledModulesJson = "[\"Catalog\",\"Pos\",\"Inventory\",\"Supplier\",\"Finance\",\"HR\",\"Tax\",\"MultiBranch\"]";
            planPro.IsActive = true;
        }

        // Vô hiệu hóa tất cả các gói cũ (FREE, SUPPORT, PREMIUM...) chỉ giữ lại 3 gói chuẩn: TRIAL, STANDARD và PRO
        var outdatedPlans = await context.SubscriptionPlans
            .Where(p => p.PlanCode != "TRIAL" && p.PlanCode != "STANDARD" && p.PlanCode != "PRO")
            .ToListAsync();
        foreach (var op in outdatedPlans)
        {
            op.IsActive = false;
        }

        // Đồng bộ các tenant đang mang mã gói cũ
        var outdatedTenants = await context.Tenants
            .Where(t => t.CurrentPlan == "FREE" || t.CurrentPlan == "SUPPORT" || string.IsNullOrEmpty(t.CurrentPlan))
            .ToListAsync();
        foreach (var ot in outdatedTenants)
        {
            ot.CurrentPlan = "PRO";
            if (string.IsNullOrEmpty(ot.SubscriptionStatus) || ot.SubscriptionStatus == "ACTIVE")
            {
                ot.SubscriptionStatus = "ACTIVE";
            }
        }

        await context.SaveChangesAsync();

        // Doanh nghiệp thuê SaaS mẫu (Default Tenant)
        var defaultTenantId = "tenant_mt_mart_01";
        if (!await context.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Id == defaultTenantId))
        {
            var demoTenant = new Tenant
            {
                Id = defaultTenantId,
                Name = "Chuỗi Siêu Thị Thực Phẩm Sạch MT-Mart",
                Subdomain = "mtmart",
                Code = "MTMART",
                TaxCode = "0314567890",
                Hotline = "1900 6868",
                Address = "Số 123 Đường Nguyễn Huệ, Phường Bến Nghé",
                City = "Hồ Chí Minh",
                Status = "ACTIVE",
                CurrentPlan = "PRO",
                SubscriptionExpiresAt = DateTime.UtcNow.AddYears(1)
            };
            await context.Tenants.AddAsync(demoTenant);
            await context.SaveChangesAsync();
        }

        // 4. TÀI KHOẢN NGƯỜI DÙNG & PHÂN QUYỀN
        if (!await context.Users.IgnoreQueryFilters().AnyAsync(u => u.UserName == "admin"))
        {
            var userAdmin = new User
            {
                Id = "usr_super_admin",
                UserName = "admin",
                PasswordHash = PasswordHasher.Hash("Admin@123"),
                FullName = "Hệ Thống Super Admin",
                Email = "admin@mtgrms.vn",
                Phone = "0901234567",
                IsActive = true,
                IsAdmin = true,
                IsEmailVerified = true
            };

            var userStoreManager = new User
            {
                Id = "usr_store_manager",
                UserName = "manager01",
                PasswordHash = PasswordHasher.Hash("Manager@123"),
                FullName = "Nguyễn Văn Hùng (Quản Lý Cửa Hàng)",
                Email = "hung.nguyen@mtmart.vn",
                Phone = "0909876543",
                IsActive = true,
                IsAdmin = false,
                IsEmailVerified = true
            };

            var userCashier = new User
            {
                Id = "usr_cashier",
                UserName = "cashier01",
                PasswordHash = PasswordHasher.Hash("Cashier@123"),
                FullName = "Lê Thị Thảo (Thu Ngân POS)",
                Email = "thao.le@mtmart.vn",
                Phone = "0912345678",
                IsActive = true,
                IsAdmin = false,
                IsEmailVerified = true
            };

            var userStoreOwner = new User
            {
                Id = "usr_store_owner",
                UserName = "chucuahang",
                PasswordHash = PasswordHasher.Hash("123456"),
                FullName = "Tran Van Chu (Chu cua hang)",
                Email = "chucuahang@mtmart.vn",
                Phone = "0901234568", // Đổi số điện thoại tránh trùng lặp với admin
                IsActive = true,
                IsEmailVerified = true
            };

            var userStaff = new User
            {
                Id = "usr_staff_cashier",
                UserName = "nhanvien",
                PasswordHash = PasswordHasher.Hash("123456"),
                FullName = "Le Thi Thu Ngan (Nhan vien ban hang)",
                Email = "nhanvien@mtmart.vn",
                Phone = "0988765432",
                IsActive = true,
                IsEmailVerified = true
            };

            await context.Users.AddRangeAsync(userAdmin, userStoreManager, userCashier, userStoreOwner, userStaff);

            var tenantUserAdmin = new TenantUser
            {
                TenantId = defaultTenantId,
                UserId = userAdmin.Id,
                Role = "SUPER_ADMIN",
                IsActive = true
            };

            var tenantUserManager = new TenantUser
            {
                TenantId = defaultTenantId,
                UserId = userStoreManager.Id,
                Role = "BRANCH_MANAGER",
                IsActive = true
            };

            var tenantUserCashier = new TenantUser
            {
                TenantId = defaultTenantId,
                UserId = userCashier.Id,
                Role = SystemRoles.Staff,
                IsActive = true
            };

            var tenantUserOwner = new TenantUser
            {
                TenantId = defaultTenantId,
                UserId = userStoreOwner.Id,
                Role = SystemRoles.StoreOwner,
                IsActive = true
            };

            var tenantUserStaff = new TenantUser
            {
                TenantId = defaultTenantId,
                UserId = userStaff.Id,
                Role = SystemRoles.Staff,
                IsActive = true
            };

            await context.TenantUsers.AddRangeAsync(tenantUserAdmin, tenantUserManager, tenantUserCashier, tenantUserOwner, tenantUserStaff);
            await context.SaveChangesAsync();
        }

        // =========================================================================
        // =========================================================================
        // PHÂN HỆ 2: CHI NHÁNH CỬA HÀNG (Branches)
        // =========================================================================
        if (!await context.Branches.IgnoreQueryFilters().AnyAsync(b => b.TenantId == defaultTenantId))
        {
            var branchQ1 = new Branch
            {
                TenantId = defaultTenantId,
                BranchCode = "BR-HCM-01",
                Name = "MT-Mart Chi Nhánh Quận 1",
                Address = "Số 45 Lê Duẩn, Bến Nghé, Quận 1",
                Phone = "028 3822 5566",
                Latitude = 10.7769m,
                Longitude = 106.7009m,
                CheckinRadiusMeters = 100,
                IsActive = true
            };

            var branchHN = new Branch
            {
                TenantId = defaultTenantId,
                BranchCode = "BR-HN-01",
                Name = "MT-Mart Chi Nhánh Cầu Giấy",
                Address = "Số 88 Cầu Giấy, Quan Hoa, Cầu Giấy",
                Phone = "024 3766 8899",
                Latitude = 21.0333m,
                Longitude = 105.7833m,
                CheckinRadiusMeters = 150,
                IsActive = true
            };

            var branchTX = new Branch
            {
                TenantId = defaultTenantId,
                BranchCode = "BR-HN-02",
                Name = "MT-Mart Chi Nhánh Thanh Xuân",
                Address = "Số 129 Nguyễn Trãi, Thượng Đình, Thanh Xuân",
                Phone = "024 3858 6677",
                Latitude = 20.9998m,
                Longitude = 105.8144m,
                CheckinRadiusMeters = 100,
                IsActive = true
            };

            await context.Branches.AddRangeAsync(branchQ1, branchHN, branchTX);
            await context.SaveChangesAsync();
        }

        // =========================================================================
        // PHÂN HỆ 3: DANH MỤC & SẢN PHẨM MẪU (Catalog & Products)
        // =========================================================================
        // Nạp 15 danh mục siêu thị chuẩn và các sản phẩm phong phú
        await CatalogSeedData.SeedAsync(context, defaultTenantId);

        // Bổ sung các sản phẩm mẫu đặc thù cho POS nếu chưa có
        var catDry = await context.Categories.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.TenantId == defaultTenantId && c.CategoryCode == "DRY");
        var catBeverage = await context.Categories.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.TenantId == defaultTenantId && c.CategoryCode == "BEV");

        if (catDry != null && catBeverage != null)
        {
            var extraProducts = new List<Product>();

            if (!await context.Products.IgnoreQueryFilters().AnyAsync(p => (p.TenantId == defaultTenantId && p.ProductCode == "SP-GAO-ST25") || p.Barcode == "893850123401"))
            {
                extraProducts.Add(new Product
                {
                    TenantId = defaultTenantId,
                    CategoryId = catDry.Id,
                    ProductCode = "SP-GAO-ST25",
                    Barcode = "893850123401",
                    Name = "Gạo Thơm ST25 Ông Cua Túi 5kg",
                    BaseUnit = "Túi",
                    CostPrice = 160000.00m,
                    SellingPrice = 195000.00m,
                    MinStock = 10.00m,
                    MaxStock = 500.00m,
                    VatRate = 0.00m,
                    ProductType = "STANDARD",
                    Status = ProductStatus.Active,
                    HasBatches = true,
                    IsFavorite = true
                });
            }

            if (!await context.Products.IgnoreQueryFilters().AnyAsync(p => (p.TenantId == defaultTenantId && p.ProductCode == "SP-SUA-VNM-1L") || p.Barcode == "893850123402"))
            {
                extraProducts.Add(new Product
                {
                    TenantId = defaultTenantId,
                    CategoryId = catBeverage.Id,
                    ProductCode = "SP-SUA-VNM-1L",
                    Barcode = "893850123402",
                    Name = "Sữa Tươi Tiệt Trùng Vinamilk Có Đường 1L",
                    BaseUnit = "Hộp",
                    CostPrice = 30000.00m,
                    SellingPrice = 38000.00m,
                    MinStock = 20.00m,
                    MaxStock = 1000.00m,
                    VatRate = 8.00m,
                    ProductType = "STANDARD",
                    Status = ProductStatus.Active,
                    HasBatches = true,
                    IsFavorite = true
                });
            }

            if (!await context.Products.IgnoreQueryFilters().AnyAsync(p => (p.TenantId == defaultTenantId && p.ProductCode == "SP-NUOCMAM-NAMNGU") || p.Barcode == "893850123403"))
            {
                extraProducts.Add(new Product
                {
                    TenantId = defaultTenantId,
                    CategoryId = catDry.Id,
                    ProductCode = "SP-NUOCMAM-NAMNGU",
                    Barcode = "893850123403",
                    Name = "Nước Mắm Nam Ngư Đệ Nhị Chai 900ml",
                    BaseUnit = "Chai",
                    CostPrice = 22000.00m,
                    SellingPrice = 28500.00m,
                    MinStock = 15.00m,
                    MaxStock = 300.00m,
                    VatRate = 8.00m,
                    ProductType = "STANDARD",
                    Status = ProductStatus.Active
                });
            }

            if (!await context.Products.IgnoreQueryFilters().AnyAsync(p => (p.TenantId == defaultTenantId && p.ProductCode == "G7-3IN1-18G") || p.Barcode == "8935024122310"))
            {
                extraProducts.Add(new Product
                {
                    TenantId = defaultTenantId,
                    CategoryId = catBeverage.Id,
                    ProductCode = "G7-3IN1-18G",
                    Barcode = "8935024122310",
                    Name = "Cà phê hòa tan G7 3in1 Hộp 18 gói",
                    BaseUnit = "Hộp",
                    CostPrice = 45000.00m,
                    SellingPrice = 58000.00m,
                    MinStock = 10.00m,
                    MaxStock = 200.00m,
                    VatRate = 1.00m,
                    ProductType = "STANDARD",
                    Status = ProductStatus.Active,
                    HasBatches = true,
                    IsFavorite = true
                });
            }

            if (!await context.Products.IgnoreQueryFilters().AnyAsync(p => (p.TenantId == defaultTenantId && p.ProductCode == "CHOCO-12P") || p.Barcode == "8936036020380"))
            {
                extraProducts.Add(new Product
                {
                    TenantId = defaultTenantId,
                    CategoryId = catDry.Id,
                    ProductCode = "CHOCO-12P",
                    Barcode = "8936036020380",
                    Name = "Bánh Orion Choco-Pie truyền thống Hộp 12 cái",
                    BaseUnit = "Hộp",
                    CostPrice = 42000.00m,
                    SellingPrice = 55000.00m,
                    MinStock = 10.00m,
                    MaxStock = 200.00m,
                    VatRate = 1.00m,
                    ProductType = "STANDARD",
                    Status = ProductStatus.Active,
                    HasBatches = true,
                    IsFavorite = true
                });
            }

            if (!await context.Products.IgnoreQueryFilters().AnyAsync(p => (p.TenantId == defaultTenantId && p.ProductCode == "MHH-TOMCHUA-120G") || p.Barcode == "8934563065249"))
            {
                extraProducts.Add(new Product
                {
                    TenantId = defaultTenantId,
                    CategoryId = catDry.Id,
                    ProductCode = "MHH-TOMCHUA-120G",
                    Barcode = "8934563065249",
                    Name = "Muối chấm Hảo Hảo tôm chua cay Hũ 120g",
                    BaseUnit = "Hũ",
                    CostPrice = 12000.00m,
                    SellingPrice = 16000.00m,
                    MinStock = 20.00m,
                    MaxStock = 300.00m,
                    VatRate = 1.00m,
                    ProductType = "STANDARD",
                    Status = ProductStatus.Active,
                    HasBatches = false,
                    IsFavorite = true
                });
            }

            if (!await context.Products.IgnoreQueryFilters().AnyAsync(p => (p.TenantId == defaultTenantId && p.ProductCode == "BIA-HANOI-SLEEK") || p.Barcode == "8936000440824"))
            {
                extraProducts.Add(new Product
                {
                    TenantId = defaultTenantId,
                    CategoryId = catBeverage.Id,
                    ProductCode = "BIA-HANOI-SLEEK",
                    Barcode = "8936000440824",
                    Name = "Bia Hà Nội Sleek Lon cao 330ml",
                    BaseUnit = "Lon",
                    CostPrice = 10500.00m,
                    SellingPrice = 14000.00m,
                    MinStock = 24.00m,
                    MaxStock = 500.00m,
                    VatRate = 1.00m,
                    ProductType = "STANDARD",
                    Status = ProductStatus.Active,
                    HasBatches = true,
                    IsFavorite = true
                });
            }

            if (extraProducts.Any())
            {
                await context.Products.AddRangeAsync(extraProducts);
                await context.SaveChangesAsync();
            }

            var prodMilk = await context.Products.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.TenantId == defaultTenantId && p.ProductCode == "SP-SUA-VNM-1L");
            if (prodMilk != null && !await context.ProductUnits.IgnoreQueryFilters().AnyAsync(u => u.ProductId == prodMilk.Id && u.UnitName == "Thùng"))
            {
                var unitCartonMilk = new ProductUnit(prodMilk.Id, "Thùng", 12.00m, 445000.00m, "893850123499", isBaseUnit: false, isPurchaseUnit: true)
                {
                    TenantId = defaultTenantId
                };
                var baseUnitMilk = await context.ProductUnits.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.ProductId == prodMilk.Id && u.IsBaseUnit);
                if (baseUnitMilk != null)
                {
                    baseUnitMilk.IsPurchaseUnit = false;
                }
                await context.ProductUnits.AddAsync(unitCartonMilk);
                await context.SaveChangesAsync();
            }
        }

        // =========================================================================
        // PHÂN HỆ 4: NHÀ CUNG CẤP & TỒN KHO THEO LÔ FEFO (Suppliers & BranchStocks)
        // =========================================================================
        if (!await context.Suppliers.IgnoreQueryFilters().AnyAsync(s => s.TenantId == defaultTenantId))
        {
            var supplierVinamilk = new Domain.Entities.Supplier.Supplier
            {
                TenantId = defaultTenantId,
                Code = "SUP-VINAMILK",
                Name = "Công Ty Cổ Phần Sữa Việt Nam (Vinamilk)",
                SupplierType = Domain.Enums.SupplierType.Goods,
                Status = Domain.Enums.SupplierStatus.Active,
                TaxCode = "0300588569",
                Phone = "02854155555",
                Email = "contact@vinamilk.com.vn",
                Address = "Số 10 Tân Trào, Tân Phú, Quận 7, TP.HCM",
                ContactPerson = "Nguyễn Anh Tuấn",
                PaymentTermDays = 30,
                CreditLimit = 200000000.00m
            };

            var supplierRice = new Domain.Entities.Supplier.Supplier
            {
                TenantId = defaultTenantId,
                Code = "SUP-HOCUA",
                Name = "Doanh Nghiệp Tư Nhân Hồ Quang Cua (Gạo ST25)",
                SupplierType = Domain.Enums.SupplierType.Goods,
                Status = Domain.Enums.SupplierStatus.Active,
                TaxCode = "2200123456",
                Phone = "02993888999",
                Email = "gaost25@soc-trang.vn",
                Address = "Tỉnh Sóc Trăng",
                ContactPerson = "Hồ Quang Cua",
                PaymentTermDays = 15,
                CreditLimit = 100000000.00m
            };

            await context.Suppliers.AddRangeAsync(supplierVinamilk, supplierRice);
            await context.SaveChangesAsync();
        }

        var bQ1 = await context.Branches.IgnoreQueryFilters().FirstOrDefaultAsync(b => b.TenantId == defaultTenantId && b.BranchCode == "BR-HCM-01");
        if (bQ1 != null && !await context.BranchStocks.IgnoreQueryFilters().AnyAsync(s => s.TenantId == defaultTenantId && s.BranchId == bQ1.Id))
        {
            var pRice = await context.Products.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.TenantId == defaultTenantId && (p.ProductCode == "SP-GAO-ST25" || p.ProductCode == "SP-DRY-001"));
            var pMilk = await context.Products.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.TenantId == defaultTenantId && (p.ProductCode == "SP-SUA-VNM-1L" || p.ProductCode == "SP-BEV-006"));
            var pSauce = await context.Products.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.TenantId == defaultTenantId && (p.ProductCode == "SP-NUOCMAM-NAMNGU" || p.ProductCode == "SP-SPC-001"));
            var pG7 = await context.Products.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.TenantId == defaultTenantId && (p.ProductCode == "G7-3IN1-18G" || p.ProductCode == "SP-BEV-004"));
            var pChoco = await context.Products.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.TenantId == defaultTenantId && (p.ProductCode == "CHOCO-12P" || p.ProductCode == "SP-DRY-006"));
            var pMuoi = await context.Products.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.TenantId == defaultTenantId && (p.ProductCode == "MHH-TOMCHUA-120G" || p.ProductCode == "SP-SPC-004"));
            var pBia = await context.Products.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.TenantId == defaultTenantId && (p.ProductCode == "BIA-HANOI-SLEEK" || p.ProductCode == "SP-BEV-001"));

            var supRice = await context.Suppliers.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.TenantId == defaultTenantId && s.Code == "SUP-HOCUA");
            var supVnm = await context.Suppliers.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.TenantId == defaultTenantId && s.Code == "SUP-VINAMILK");

            var stocksToAdd = new List<BranchStock>();
            if (pRice != null)
            {
                stocksToAdd.Add(new BranchStock
                {
                    TenantId = defaultTenantId,
                    BranchId = bQ1.Id,
                    ProductId = pRice.Id,
                    SupplierId = supRice?.Id,
                    BatchCode = "BATCH-ST25-20261001",
                    ManufactureDate = DateTime.UtcNow.AddMonths(-1),
                    ExpiryDate = DateTime.UtcNow.AddMonths(11),
                    QuantityOnHand = 150.000m,
                    QuantityReserved = 5.000m,
                    LandedCostPerUnit = 160000.00m,
                    ShelfLocation = "A1-03",
                    ZoneArea = "Kho Khô"
                });
            }
            if (pMilk != null)
            {
                stocksToAdd.Add(new BranchStock
                {
                    TenantId = defaultTenantId,
                    BranchId = bQ1.Id,
                    ProductId = pMilk.Id,
                    SupplierId = supVnm?.Id,
                    BatchCode = "BATCH-VNM-20261015",
                    ManufactureDate = DateTime.UtcNow.AddDays(-12),
                    ExpiryDate = DateTime.UtcNow.AddMonths(5),
                    QuantityOnHand = 320.000m,
                    QuantityReserved = 0.000m,
                    LandedCostPerUnit = 30000.00m,
                    ShelfLocation = "B2-01",
                    ZoneArea = "Kho Mát"
                });
            }
            if (pSauce != null)
            {
                stocksToAdd.Add(new BranchStock
                {
                    TenantId = defaultTenantId,
                    BranchId = bQ1.Id,
                    ProductId = pSauce.Id,
                    BatchCode = "BATCH-SAUCE-2026",
                    ExpiryDate = DateTime.UtcNow.AddYears(1),
                    QuantityOnHand = 100.000m,
                    QuantityReserved = 0.000m,
                    LandedCostPerUnit = 22000.00m
                });
            }
            if (pG7 != null)
            {
                stocksToAdd.Add(new BranchStock
                {
                    TenantId = defaultTenantId,
                    BranchId = bQ1.Id,
                    ProductId = pG7.Id,
                    BatchCode = "BATCH-G7-2026",
                    ExpiryDate = DateTime.UtcNow.AddYears(2),
                    QuantityOnHand = 80.000m,
                    QuantityReserved = 0.000m,
                    LandedCostPerUnit = 45000.00m
                });
            }
            if (pChoco != null)
            {
                stocksToAdd.Add(new BranchStock
                {
                    TenantId = defaultTenantId,
                    BranchId = bQ1.Id,
                    ProductId = pChoco.Id,
                    BatchCode = "BATCH-CP-2026",
                    ExpiryDate = DateTime.UtcNow.AddMonths(8),
                    QuantityOnHand = 60.000m,
                    QuantityReserved = 0.000m,
                    LandedCostPerUnit = 42000.00m
                });
            }
            if (pMuoi != null)
            {
                stocksToAdd.Add(new BranchStock
                {
                    TenantId = defaultTenantId,
                    BranchId = bQ1.Id,
                    ProductId = pMuoi.Id,
                    BatchCode = "BATCH-MHH-2026",
                    ExpiryDate = DateTime.UtcNow.AddYears(1),
                    QuantityOnHand = 150.000m,
                    QuantityReserved = 0.000m,
                    LandedCostPerUnit = 12000.00m
                });
            }
            if (pBia != null)
            {
                stocksToAdd.Add(new BranchStock
                {
                    TenantId = defaultTenantId,
                    BranchId = bQ1.Id,
                    ProductId = pBia.Id,
                    BatchCode = "BATCH-BIA-2026",
                    ExpiryDate = DateTime.UtcNow.AddMonths(6),
                    QuantityOnHand = 120.000m,
                    QuantityReserved = 0.000m,
                    LandedCostPerUnit = 10500.00m
                });
            }

            if (stocksToAdd.Any())
            {
                await context.BranchStocks.AddRangeAsync(stocksToAdd);
                await context.SaveChangesAsync();
            }
        }

        // =========================================================================
        // PHÂN HỆ 5: NHÂN SỰ, CA LÀM VIỆC, CHẤM CÔNG & BẢNG LƯƠNG (EmployeeSeedData)
        // =========================================================================
        try
        {
            await EmployeeSeedData.SeedAsync(context, defaultTenantId);
        }
        catch (Exception ex)
        {
            context.ChangeTracker.Clear();
            logger?.LogWarning(ex, "[MT-GRMS] Bỏ qua một phần dữ liệu mẫu EmployeeSeedData ({TenantId}), tiếp tục nạp dữ liệu các phân hệ kế tiếp.", defaultTenantId);
        }

        // =========================================================================
        // PHÂN HỆ 6: KHÁCH HÀNG & CHƯƠNG TRÌNH KHUYẾN MÃI (Customers & Promotions)
        // =========================================================================
        var customerVip = new Customer
        {
            TenantId = defaultTenantId,
            CustomerCode = "KH-0001",
            Name = "Nguyễn Văn Hưng",
            Phone = "0903112233",
            Email = "hung.nguyen@gmail.com",
            Address = "24 Đường số 5, Phường Đa Kao, Quận 1",
            MembershipTier = "Gold",
            RewardPoints = 250,
            TotalSpending = 12500000.00m,
            AllowDebt = true,
            DebtLimit = 5000000.00m
        };

        var promoOpening = new Promotion
        {
            TenantId = defaultTenantId,
            PromotionCode = "KM-KHAITRUONG-10K",
            PromotionName = "Giảm 10,000đ Mừng Tuần Lễ Mua Sắm",
            Type = "FIXED_AMOUNT",
            DiscountValue = 10000.00m,
            StartDate = DateTime.UtcNow.AddDays(-7),
            EndDate = DateTime.UtcNow.AddDays(30),
            IsActive = true
        };

        if (!await context.Customers.IgnoreQueryFilters().AnyAsync(c => c.TenantId == defaultTenantId && (c.CustomerCode == "KH-0001" || c.Phone == "0903112233")))
        {
            await context.Customers.AddAsync(customerVip);
            await context.SaveChangesAsync();
        }
        if (!await context.Promotions.IgnoreQueryFilters().AnyAsync(p => p.TenantId == defaultTenantId && p.PromotionCode == "KM-KHAITRUONG-10K"))
        {
            await context.Promotions.AddAsync(promoOpening);
            await context.SaveChangesAsync();
        }

        // =========================================================================
        // PHÂN HỆ 7: ĐƠN HÀNG BÁN LẺ & GIAO DỊCH POS (Orders & Pre-Orders)
        // =========================================================================
        try
        {
            await SeedOrdersAndPreOrdersAsync(context);
        }
        catch (Exception ex)
        {
            context.ChangeTracker.Clear();
            logger?.LogWarning(ex, "[MT-GRMS] Bỏ qua một phần dữ liệu mẫu SeedOrdersAndPreOrdersAsync.");
        }

        // =========================================================================
        // PHÂN HỆ 8: TÀI CHÍNH, SỔ QUỸ & BÁO CÁO THUẾ (Tax & Finance)
        // =========================================================================
        try
        {
            await SeedTaxAndFinanceAsync(context, logger);
        }
        catch (Exception ex)
        {
            context.ChangeTracker.Clear();
            logger?.LogWarning(ex, "[MT-GRMS] Bỏ qua một phần dữ liệu mẫu SeedTaxAndFinanceAsync.");
        }

        // =========================================================================
        // PHÂN HỆ 9: KỊCH BẢN CHUỖI CỬA HÀNG MỚI (Store Owner An Nhiên)
        // =========================================================================
        await SeedNewStoreOwnerAnNhienAsync(context);

        // Backfill đơn vị tính cơ bản (BaseUnit, IsPurchaseUnit = true) cho mọi sản phẩm chưa có ProductUnit
        await EnsureAllProductsHaveBaseUnitAsync(context);
    }

    private static async Task EnsureAllProductsHaveBaseUnitAsync(AppDbContext context)
    {
        var allProducts = await context.Products.IgnoreQueryFilters().ToListAsync();
        if (!allProducts.Any()) return;

        var existingBaseProductIdList = await context.ProductUnits.IgnoreQueryFilters()
            .Where(u => u.IsBaseUnit && !u.IsDeleted)
            .Select(u => u.ProductId)
            .ToListAsync();
        var existingBaseProductIds = existingBaseProductIdList.ToHashSet();

        var missingBaseUnits = new List<ProductUnit>();
        foreach (var prod in allProducts)
        {
            if (!existingBaseProductIds.Contains(prod.Id))
            {
                missingBaseUnits.Add(new ProductUnit(
                    prod.Id,
                    string.IsNullOrWhiteSpace(prod.BaseUnit) ? "Cái" : prod.BaseUnit,
                    1.00m,
                    prod.SellingPrice,
                    prod.Barcode,
                    isBaseUnit: true,
                    isPurchaseUnit: true)
                {
                    TenantId = prod.TenantId
                });
                existingBaseProductIds.Add(prod.Id);
            }
        }

        if (missingBaseUnits.Any())
        {
            await context.ProductUnits.AddRangeAsync(missingBaseUnits);
            await context.SaveChangesAsync();
        }
    }

    public static async Task SeedOrdersAndPreOrdersAsync(AppDbContext context)
    {
        var tenantId = "tenant_mt_mart_01";

        // 1. Đảm bảo có đủ 3 chi nhánh trong hệ thống
        var branchHN = await context.Branches.IgnoreQueryFilters().FirstOrDefaultAsync(b => b.TenantId == tenantId && b.BranchCode == "BR-HN-01");
        var branchQ1 = await context.Branches.IgnoreQueryFilters().FirstOrDefaultAsync(b => b.TenantId == tenantId && b.BranchCode == "BR-HCM-01");
        var branchTX = await context.Branches.IgnoreQueryFilters().FirstOrDefaultAsync(b => b.TenantId == tenantId && b.BranchCode == "BR-HN-02");

        if (branchHN == null)
        {
            branchHN = new Branch
            {
                TenantId = tenantId,
                BranchCode = "BR-HN-01",
                Name = "MT-Mart Chi Nhánh Cầu Giấy",
                Address = "Số 88 Cầu Giấy, Quan Hoa, Cầu Giấy",
                Phone = "024 3766 8899",
                Latitude = 21.0333m,
                Longitude = 105.7833m,
                CheckinRadiusMeters = 150,
                IsActive = true
            };
            await context.Branches.AddAsync(branchHN);
            await context.SaveChangesAsync();
        }

        if (branchQ1 == null)
        {
            branchQ1 = new Branch
            {
                TenantId = tenantId,
                BranchCode = "BR-HCM-01",
                Name = "MT-Mart Chi Nhánh Quận 1",
                Address = "Số 45 Lê Duẩn, Bến Nghé, Quận 1",
                Phone = "028 3822 5566",
                Latitude = 10.7769m,
                Longitude = 106.7009m,
                CheckinRadiusMeters = 100,
                IsActive = true
            };
            await context.Branches.AddAsync(branchQ1);
            await context.SaveChangesAsync();
        }

        if (branchTX == null)
        {
            branchTX = new Branch
            {
                TenantId = tenantId,
                BranchCode = "BR-HN-02",
                Name = "MT-Mart Chi Nhánh Thanh Xuân",
                Address = "Số 129 Nguyễn Trãi, Thượng Đình, Thanh Xuân",
                Phone = "024 3858 6677",
                Latitude = 20.9998m,
                Longitude = 105.8144m,
                CheckinRadiusMeters = 100,
                IsActive = true
            };
            await context.Branches.AddAsync(branchTX);
            await context.SaveChangesAsync();
        }

        // Đảm bảo tồn kho cho cả 3 chi nhánh
        var q1Stocks = await context.BranchStocks.Where(s => s.BranchId == branchQ1.Id).ToListAsync();
        if (q1Stocks.Any())
        {
            if (!await context.BranchStocks.IgnoreQueryFilters().AnyAsync(s => s.BranchId == branchHN.Id))
            {
                foreach (var s in q1Stocks)
                {
                    context.BranchStocks.Add(new BranchStock
                    {
                        TenantId = tenantId,
                        BranchId = branchHN.Id,
                        ProductId = s.ProductId,
                        SupplierId = s.SupplierId,
                        BatchCode = s.BatchCode,
                        ManufactureDate = s.ManufactureDate,
                        ExpiryDate = s.ExpiryDate,
                        QuantityOnHand = s.QuantityOnHand,
                        QuantityReserved = 0,
                        LandedCostPerUnit = s.LandedCostPerUnit,
                        ShelfLocation = s.ShelfLocation,
                        ZoneArea = s.ZoneArea
                    });
                }
            }

            if (!await context.BranchStocks.IgnoreQueryFilters().AnyAsync(s => s.BranchId == branchTX.Id))
            {
                foreach (var s in q1Stocks)
                {
                    context.BranchStocks.Add(new BranchStock
                    {
                        TenantId = tenantId,
                        BranchId = branchTX.Id,
                        ProductId = s.ProductId,
                        SupplierId = s.SupplierId,
                        BatchCode = s.BatchCode,
                        ManufactureDate = s.ManufactureDate,
                        ExpiryDate = s.ExpiryDate,
                        QuantityOnHand = s.QuantityOnHand,
                        QuantityReserved = 0,
                        LandedCostPerUnit = s.LandedCostPerUnit,
                        ShelfLocation = s.ShelfLocation,
                        ZoneArea = s.ZoneArea
                    });
                }
            }
            await context.SaveChangesAsync();
        }

        // Kiểm tra nếu đã có orders thì cập nhật phân bổ chuẩn theo 3 chi nhánh
        if (await context.Orders.IgnoreQueryFilters().AnyAsync(o => o.TenantId == tenantId))
        {
            var ord1 = await context.Orders.IgnoreQueryFilters().FirstOrDefaultAsync(o => o.OrderCode == "HD-20261101-001");
            if (ord1 != null) ord1.BranchId = branchHN.Id;

            var po1 = await context.Orders.IgnoreQueryFilters().FirstOrDefaultAsync(o => o.OrderCode == "PO-20261101-001");
            if (po1 != null) po1.BranchId = branchHN.Id;

            var ord2 = await context.Orders.IgnoreQueryFilters().FirstOrDefaultAsync(o => o.OrderCode == "HD-20261101-002");
            if (ord2 != null) ord2.BranchId = branchTX.Id;

            var po2 = await context.Orders.IgnoreQueryFilters().FirstOrDefaultAsync(o => o.OrderCode == "PO-20261101-002");
            if (po2 != null) po2.BranchId = branchTX.Id;

            var ord3 = await context.Orders.IgnoreQueryFilters().FirstOrDefaultAsync(o => o.OrderCode == "HD-20261101-003");
            if (ord3 != null) ord3.BranchId = branchQ1.Id;

            var ord4 = await context.Orders.IgnoreQueryFilters().FirstOrDefaultAsync(o => o.OrderCode == "HD-20261101-004");
            if (ord4 != null) ord4.BranchId = branchQ1.Id;

            var po3 = await context.Orders.IgnoreQueryFilters().FirstOrDefaultAsync(o => o.OrderCode == "PO-20261031-003");
            if (po3 != null) po3.BranchId = branchQ1.Id;

            await context.SaveChangesAsync();
            return;
        }

        // Đảm bảo có các khách hàng mẫu thân thiết
        var custHung = await context.Customers.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.TenantId == tenantId && (c.Phone == "0903112233" || c.CustomerCode == "KH-0001"));
        if (custHung == null)
        {
            custHung = new Customer
            {
                TenantId = tenantId,
                CustomerCode = "KH-0001",
                Name = "Nguyễn Văn Hưng",
                Phone = "0903112233",
                Email = "hung.nguyen@gmail.com",
                Address = "24 Đường số 5, Phường Đa Kao, Quận 1",
                MembershipTier = "Gold",
                RewardPoints = 250,
                TotalSpending = 12500000m,
                AllowDebt = true,
                TotalDebt = 0m,
            };
            await context.Customers.AddAsync(custHung);
        }

        var custKhach = await context.Customers.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.TenantId == tenantId && (c.Phone == "0912345678" || c.CustomerCode == "KH-POS-001"));
        if (custKhach == null)
        {
            custKhach = new Customer
            {
                TenantId = tenantId,
                CustomerCode = "KH-POS-001",
                Name = "Nguyễn Văn Khách (Thân Thiết)",
                Phone = "0912345678",
                Email = "khach.pos@gmail.com",
                Address = "25 Cầu Giấy, Hà Nội",
                MembershipTier = "Gold",
                RewardPoints = 250,
                TotalSpending = 5500000m,
                AllowDebt = true,
                TotalDebt = 350000m,
            };
            await context.Customers.AddAsync(custKhach);
        }

        var custLan = await context.Customers.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.TenantId == tenantId && (c.Phone == "0987654321" || c.CustomerCode == "KH-POS-002"));
        if (custLan == null)
        {
            custLan = new Customer
            {
                TenantId = tenantId,
                CustomerCode = "KH-POS-002",
                Name = "Chị Lan Tạp Hóa Xóm",
                Phone = "0987654321",
                Email = "lan.taphoa@gmail.com",
                Address = "88 Nguyễn Trãi, Thanh Xuân, Hà Nội",
                MembershipTier = "VIP",
                RewardPoints = 580,
                TotalSpending = 18500000m,
                AllowDebt = true,
                TotalDebt = 2100000m,
            };
            await context.Customers.AddAsync(custLan);
        }

        await context.SaveChangesAsync();

        // Lấy sản phẩm có trong kho
        var products = await context.Products.Where(p => p.TenantId == tenantId).ToListAsync();
        var prodGao = products.FirstOrDefault(p => p.Barcode == "893850123401") ?? products.FirstOrDefault();
        var prodSua = products.FirstOrDefault(p => p.Barcode == "893850123402") ?? products.Skip(1).FirstOrDefault() ?? prodGao;
        var prodNuocMam = products.FirstOrDefault(p => p.Barcode == "893850123403") ?? products.Skip(2).FirstOrDefault() ?? prodGao;
        var prodDauAn = products.FirstOrDefault(p => p.Barcode == "893850123405") ?? products.Skip(3).FirstOrDefault() ?? prodGao;

        var now = DateTime.UtcNow;

        // 1. HÓA ĐƠN TIỀN MẶT ĐÃ HOÀN THÀNH (Chi nhánh Cầu Giấy)
        var order1 = new Order
        {
            TenantId = tenantId,
            BranchId = branchHN.Id,
            CustomerId = custHung.Id,
            OrderCode = "HD-20261101-001",
            OrderDate = now.AddHours(-3),
            TotalAmount = 66500m,
            DiscountAmount = 0m,
            FinalAmount = 66500m,
            DepositAmount = 100000m, // tiền khách đưa
            PaymentMethod = "CASH",
            PaymentStatus = "PAID",
            Status = "Completed",
            Note = "Thanh toán đủ tiền mặt tại quầy"
        };
        if (prodSua != null)
        {
            order1.Details.Add(new OrderDetail
            {
                OrderId = order1.Id,
                ProductId = prodSua.Id,
                LineNumber = 1,
                Qty = 1,
                UnitPrice = 38000m,
                DiscountAmount = 0m,
                ActualPrice = 38000m,
                AppliedVatRate = 0.01m,
                VatAmount = 380m
            });
        }
        if (prodNuocMam != null)
        {
            order1.Details.Add(new OrderDetail
            {
                OrderId = order1.Id,
                ProductId = prodNuocMam.Id,
                LineNumber = 2,
                Qty = 1,
                UnitPrice = 28500m,
                DiscountAmount = 0m,
                ActualPrice = 28500m,
                AppliedVatRate = 0.01m,
                VatAmount = 285m
            });
        }

        // 2. HÓA ĐƠN VIETQR ĐÃ HOÀN THÀNH (Chi nhánh Thanh Xuân)
        var order2 = new Order
        {
            TenantId = tenantId,
            BranchId = branchTX.Id,
            CustomerId = custKhach.Id,
            OrderCode = "HD-20261101-002",
            OrderDate = now.AddHours(-2),
            TotalAmount = 260000m,
            DiscountAmount = 0m,
            FinalAmount = 260000m,
            DepositAmount = 260000m,
            PaymentMethod = "VIETQR",
            PaymentStatus = "PAID",
            Status = "Completed",
            Note = "Quét VietQR chuyển khoản thành công"
        };
        if (prodGao != null)
        {
            order2.Details.Add(new OrderDetail
            {
                OrderId = order2.Id,
                ProductId = prodGao.Id,
                LineNumber = 1,
                Qty = 1,
                UnitPrice = 195000m,
                DiscountAmount = 0m,
                ActualPrice = 195000m,
                AppliedVatRate = 0.01m,
                VatAmount = 1950m
            });
        }
        if (prodDauAn != null)
        {
            order2.Details.Add(new OrderDetail
            {
                OrderId = order2.Id,
                ProductId = prodDauAn.Id,
                LineNumber = 2,
                Qty = 1,
                UnitPrice = 65000m,
                DiscountAmount = 0m,
                ActualPrice = 65000m,
                AppliedVatRate = 0.01m,
                VatAmount = 650m
            });
        }

        // 3. HÓA ĐƠN BÁN NỢ CHO CHỊ LAN (Chi nhánh Quận 1)
        var order3 = new Order
        {
            TenantId = tenantId,
            BranchId = branchQ1.Id,
            CustomerId = custLan.Id,
            OrderCode = "HD-20261101-003",
            OrderDate = now.AddMinutes(-45),
            TotalAmount = 522500m,
            DiscountAmount = 0m,
            FinalAmount = 522500m,
            DepositAmount = 0m,
            PaymentMethod = "DEBT",
            PaymentStatus = "UNPAID",
            Status = "Completed",
            Note = "Chủ tiệm duyệt bán nợ ghi sổ tạp hóa"
        };
        if (prodSua != null)
        {
            order3.Details.Add(new OrderDetail
            {
                OrderId = order3.Id,
                ProductId = prodSua.Id,
                LineNumber = 1,
                Qty = 10,
                UnitPrice = 38000m,
                DiscountAmount = 0m,
                ActualPrice = 38000m,
                AppliedVatRate = 0.01m,
                VatAmount = 3800m
            });
        }
        if (prodNuocMam != null)
        {
            order3.Details.Add(new OrderDetail
            {
                OrderId = order3.Id,
                ProductId = prodNuocMam.Id,
                LineNumber = 2,
                Qty = 5,
                UnitPrice = 28500m,
                DiscountAmount = 0m,
                ActualPrice = 28500m,
                AppliedVatRate = 0.01m,
                VatAmount = 1425m
            });
        }

        // 4. HÓA ĐƠN ĐÃ HỦY (Chi nhánh Quận 1)
        var order4 = new Order
        {
            TenantId = tenantId,
            BranchId = branchQ1.Id,
            CustomerId = null,
            OrderCode = "HD-20261101-004",
            OrderDate = now.AddMinutes(-20),
            TotalAmount = 38000m,
            DiscountAmount = 0m,
            FinalAmount = 38000m,
            DepositAmount = 0m,
            PaymentMethod = "CASH",
            PaymentStatus = "UNPAID",
            Status = "Cancelled",
            Note = "Khách đổi ý trả lại hàng"
        };
        if (prodSua != null)
        {
            order4.Details.Add(new OrderDetail
            {
                OrderId = order4.Id,
                ProductId = prodSua.Id,
                LineNumber = 1,
                Qty = 1,
                UnitPrice = 38000m,
                DiscountAmount = 0m,
                ActualPrice = 38000m
            });
        }

        // 5. ĐƠN ĐẶT TRƯỚC GIỮ KHO 1 (Chi nhánh Cầu Giấy)
        var preOrder1 = new Order
        {
            TenantId = tenantId,
            BranchId = branchHN.Id,
            CustomerId = custLan.Id,
            OrderCode = "PO-20261101-001",
            OrderDate = now.AddHours(-4),
            TotalAmount = 1735000m,
            DiscountAmount = 0m,
            FinalAmount = 1735000m,
            DepositAmount = 500000m, // Đã đặt cọc 500k
            PaymentMethod = "VIETQR",
            PaymentStatus = "PARTIAL",
            Status = "PreOrder",
            Note = "Hẹn nhận hàng lúc 17:30 chiều nay tại quầy siêu thị"
        };
        if (prodGao != null)
        {
            preOrder1.Details.Add(new OrderDetail
            {
                OrderId = preOrder1.Id,
                ProductId = prodGao.Id,
                LineNumber = 1,
                Qty = 5,
                UnitPrice = 195000m,
                ActualPrice = 195000m
            });
        }
        if (prodSua != null)
        {
            preOrder1.Details.Add(new OrderDetail
            {
                OrderId = preOrder1.Id,
                ProductId = prodSua.Id,
                LineNumber = 2,
                Qty = 20,
                UnitPrice = 38000m,
                ActualPrice = 38000m
            });
        }

        // 6. ĐƠN ĐẶT TRƯỚC GIỮ KHO 2 (Chi nhánh Thanh Xuân)
        var preOrder2 = new Order
        {
            TenantId = tenantId,
            BranchId = branchTX.Id,
            CustomerId = custHung.Id,
            OrderCode = "PO-20261101-002",
            OrderDate = now.AddHours(-1),
            TotalAmount = 850000m,
            DiscountAmount = 0m,
            FinalAmount = 850000m,
            DepositAmount = 300000m, // Đã cọc 300k
            PaymentMethod = "CASH",
            PaymentStatus = "PARTIAL",
            Status = "PreOrder",
            Note = "Hẹn nhận lúc 18:00 tối nay. Soạn giỏ quà tặng thực phẩm sạch."
        };
        if (prodNuocMam != null)
        {
            preOrder2.Details.Add(new OrderDetail
            {
                OrderId = preOrder2.Id,
                ProductId = prodNuocMam.Id,
                LineNumber = 1,
                Qty = 10,
                UnitPrice = 28500m,
                ActualPrice = 28500m
            });
        }
        if (prodDauAn != null)
        {
            preOrder2.Details.Add(new OrderDetail
            {
                OrderId = preOrder2.Id,
                ProductId = prodDauAn.Id,
                LineNumber = 2,
                Qty = 5,
                UnitPrice = 65000m,
                ActualPrice = 65000m
            });
        }

        // 7. ĐƠN ĐẶT TRƯỚC ĐÃ ĐẾN NHẬN VÀ HOÀN TẤT (Chi nhánh Quận 1)
        var preOrder3 = new Order
        {
            TenantId = tenantId,
            BranchId = branchQ1.Id,
            CustomerId = custKhach.Id,
            OrderCode = "PO-20261031-003",
            OrderDate = now.AddDays(-1),
            TotalAmount = 585000m,
            DiscountAmount = 0m,
            FinalAmount = 585000m,
            DepositAmount = 200000m,
            PaymentMethod = "CASH",
            PaymentStatus = "PAID",
            Status = "Completed",
            Note = "Khách đã đến quầy nhận hàng đủ hôm qua"
        };
        if (prodGao != null)
        {
            preOrder3.Details.Add(new OrderDetail
            {
                OrderId = preOrder3.Id,
                ProductId = prodGao.Id,
                LineNumber = 1,
                Qty = 3,
                UnitPrice = 195000m,
                ActualPrice = 195000m
            });
        }

        await context.Orders.AddRangeAsync(order1, order2, order3, order4, preOrder1, preOrder2, preOrder3);
        await context.SaveChangesAsync();
    }

    public static async Task SeedNewStoreOwnerAnNhienAsync(AppDbContext context)
    {
        var tenantId = "tenant_an_nhien_02";

        // 1. Kiểm tra / Tạo mới Tenant "An Nhiên Mart"
        var tenant = await context.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == tenantId);
        if (tenant == null)
        {
            tenant = new Tenant
            {
                Id = tenantId,
                Name = "Cửa Hàng Thực Phẩm Sạch An Nhiên (An Nhiên Mart)",
                Subdomain = "annhien",
                Code = "ANNHIEN",
                TaxCode = "0109887766",
                Hotline = "0918889999",
                Address = "Số 120 Hoàng Hoa Thám, Phường Thụy Khuê, Quận Tây Hồ",
                City = "Hà Nội",
                Status = "ACTIVE",
                CurrentPlan = "PRO"
            };
            await context.Tenants.AddAsync(tenant);
            await context.SaveChangesAsync();
        }

        static void EnsureDemoCredential(User u)
        {
            if (!PasswordHasher.Verify(u.PasswordHash, "123456", out var rehash) || rehash)
            {
                u.PasswordHash = PasswordHasher.Hash("123456");
            }
            u.IsActive = true;
            u.IsEmailVerified = true;
            u.AccessFailedCount = 0;
            u.LockoutEnd = null;
        }

        // 2. Kiểm tra / Tạo mới Users & TenantUsers (STORE_OWNER và STAFF)
        var ownerUser = await context.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == "usr_owner_annhien_02" || u.UserName == "0918889999");
        if (ownerUser == null)
        {
            ownerUser = new User
            {
                Id = "usr_owner_annhien_02",
                UserName = "0918889999",
                PasswordHash = PasswordHasher.Hash("123456"),
                FullName = "Trần Quốc An (Chủ tiệm An Nhiên)",
                Email = "chucuahang.annhien@gmail.com",
                Phone = "0918889999",
                IsActive = true,
                IsEmailVerified = true
            };
            await context.Users.AddAsync(ownerUser);
            await context.SaveChangesAsync();
        }
        else
        {
            EnsureDemoCredential(ownerUser);
            ownerUser.Phone = "0918889999";
        }

        var staffUser = await context.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == "usr_employee_annhien_02" || u.UserName == "0917778888");
        if (staffUser == null)
        {
            staffUser = new User
            {
                Id = "usr_employee_annhien_02",
                UserName = "0917778888",
                PasswordHash = PasswordHasher.Hash("123456"),
                FullName = "Phạm Thị Yến (Nhân viên Ba Đình)",
                Email = "nhanvien.annhien@gmail.com",
                Phone = "0917778888",
                IsActive = true,
                IsEmailVerified = true
            };
            await context.Users.AddAsync(staffUser);
            await context.SaveChangesAsync();
        }
        else
        {
            EnsureDemoCredential(staffUser);
            staffUser.Phone = "0917778888";
        }

        var staffUser4 = await context.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == "usr_employee_annhien_05" || u.UserName == "0914445555");
        if (staffUser4 == null)
        {
            staffUser4 = new User
            {
                Id = "usr_employee_annhien_05",
                UserName = "0914445555",
                PasswordHash = PasswordHasher.Hash("123456"),
                FullName = "Trần Minh Quân (Nhân viên Ba Đình)",
                Email = "quan.tran@annhienmart.vn",
                Phone = "0914445555",
                IsActive = true,
                IsEmailVerified = true
            };
            await context.Users.AddAsync(staffUser4);
            await context.SaveChangesAsync();
        }
        else
        {
            EnsureDemoCredential(staffUser4);
            staffUser4.Phone = "0914445555";
        }

        // Đảm bảo gán quyền TenantUser đúng vai trò STORE_OWNER và STAFF
        var tuOwner = await context.TenantUsers.IgnoreQueryFilters().FirstOrDefaultAsync(tu => tu.TenantId == tenantId && tu.UserId == ownerUser.Id);
        if (tuOwner == null)
        {
            await context.TenantUsers.AddAsync(new TenantUser
            {
                TenantId = tenantId,
                UserId = ownerUser.Id,
                Role = "STORE_OWNER",
                IsActive = true
            });
        }
        else
        {
            tuOwner.Role = "STORE_OWNER";
            tuOwner.IsActive = true;
        }

        var tuStaff = await context.TenantUsers.IgnoreQueryFilters().FirstOrDefaultAsync(tu => tu.TenantId == tenantId && tu.UserId == staffUser.Id);
        if (tuStaff == null)
        {
            await context.TenantUsers.AddAsync(new TenantUser
            {
                TenantId = tenantId,
                UserId = staffUser.Id,
                Role = SystemRoles.Staff,
                IsActive = true
            });
        }
        else
        {
            tuStaff.Role = SystemRoles.Staff;
            tuStaff.IsActive = true;
        }

        var tuStaff4 = await context.TenantUsers.IgnoreQueryFilters().FirstOrDefaultAsync(tu => tu.TenantId == tenantId && tu.UserId == staffUser4.Id);
        if (tuStaff4 == null)
        {
            await context.TenantUsers.AddAsync(new TenantUser
            {
                TenantId = tenantId,
                UserId = staffUser4.Id,
                Role = SystemRoles.Staff,
                IsActive = true
            });
        }
        else
        {
            tuStaff4.Role = SystemRoles.Staff;
            tuStaff4.IsActive = true;
        }
        await context.SaveChangesAsync();

        // 3. Các chi nhánh của An Nhiên Mart (Đa chi nhánh: Ba Đình, Cầu Giấy, Đống Đa)
        var branch = await context.Branches.IgnoreQueryFilters().FirstOrDefaultAsync(b => b.TenantId == tenantId && b.BranchCode == "BR-AN-01");
        if (branch == null)
        {
            branch = new Branch
            {
                TenantId = tenantId,
                BranchCode = "BR-AN-01",
                Name = "An Nhiên Mart - Chi nhánh Ba Đình",
                Address = "120 Hoàng Hoa Thám, Tây Hồ, Hà Nội",
                Phone = "024 3822 9988",
                Latitude = 21.0422m,
                Longitude = 105.8194m,
                CheckinRadiusMeters = 100,
                IsActive = true
            };
            await context.Branches.AddAsync(branch);
            await context.SaveChangesAsync();
        }

        var branchCauGiay = await context.Branches.IgnoreQueryFilters().FirstOrDefaultAsync(b => b.TenantId == tenantId && b.BranchCode == "BR-AN-02");
        if (branchCauGiay == null)
        {
            branchCauGiay = new Branch
            {
                TenantId = tenantId,
                BranchCode = "BR-AN-02",
                Name = "An Nhiên Mart - Chi nhánh Cầu Giấy",
                Address = "268 Cầu Giấy, Q. Cầu Giấy, Hà Nội",
                Phone = "024 3833 7766",
                Latitude = 21.0333m,
                Longitude = 105.7950m,
                CheckinRadiusMeters = 100,
                IsActive = true
            };
            await context.Branches.AddAsync(branchCauGiay);
            await context.SaveChangesAsync();
        }

        var branchDongDa = await context.Branches.IgnoreQueryFilters().FirstOrDefaultAsync(b => b.TenantId == tenantId && b.BranchCode == "BR-AN-03");
        if (branchDongDa == null)
        {
            branchDongDa = new Branch
            {
                TenantId = tenantId,
                BranchCode = "BR-AN-03",
                Name = "An Nhiên Mart - Chi nhánh Đống Đa",
                Address = "85 Chùa Bộc, Q. Đống Đa, Hà Nội",
                Phone = "024 3855 6655",
                Latitude = 21.0080m,
                Longitude = 105.8280m,
                CheckinRadiusMeters = 100,
                IsActive = true
            };
            await context.Branches.AddAsync(branchDongDa);
            await context.SaveChangesAsync();
        }

        // Hồ sơ nhân viên all-time 1 tại Chi nhánh Ba Đình: Phạm Thị Yến (0917778888)
        var empYen = await context.Employees.IgnoreQueryFilters().FirstOrDefaultAsync(e => e.TenantId == tenantId && (e.EmployeeCode == "NV-AN-01" || e.CitizenId == "001198007788"));
        if (empYen == null)
        {
            empYen = new Employee
            {
                TenantId = tenantId,
                BranchId = branch.Id,
                UserId = staffUser.Id,
                EmployeeCode = "NV-AN-01",
                FullName = "Phạm Thị Yến (Nhân viên Ba Đình)",
                CitizenId = "001198007788",
                Phone = "0917778888",
                Email = "nhanvien.annhien@gmail.com",
                SalaryType = SalaryType.Monthly,
                BaseRate = 7500000m,
                Status = EmployeeStatus.Active
            };
            await context.Employees.AddAsync(empYen);
        }
        else
        {
            empYen.BranchId = branch.Id;
            empYen.UserId = staffUser.Id;
            empYen.Status = EmployeeStatus.Active;
            empYen.IsDeleted = false;
            if (string.IsNullOrEmpty(empYen.CitizenId))
            {
                empYen.CitizenId = "001198007788";
            }
        }

        // Hồ sơ nhân viên all-time 2 cùng Chi nhánh Ba Đình: Trần Minh Quân (0914445555)
        var empQuan = await context.Employees.IgnoreQueryFilters().FirstOrDefaultAsync(e => e.TenantId == tenantId && (e.EmployeeCode == "NV-AN-04" || e.CitizenId == "001198004455"));
        if (empQuan == null)
        {
            empQuan = new Employee
            {
                TenantId = tenantId,
                BranchId = branch.Id,
                UserId = staffUser4.Id,
                EmployeeCode = "NV-AN-04",
                FullName = "Trần Minh Quân (Nhân viên Ba Đình)",
                CitizenId = "001198004455",
                Phone = "0914445555",
                Email = "quan.tran@annhienmart.vn",
                SalaryType = SalaryType.Monthly,
                BaseRate = 7500000m,
                Status = EmployeeStatus.Active
            };
            await context.Employees.AddAsync(empQuan);
        }
        else
        {
            empQuan.BranchId = branch.Id;
            empQuan.UserId = staffUser4.Id;
            empQuan.Status = EmployeeStatus.Active;
            empQuan.IsDeleted = false;
            if (string.IsNullOrEmpty(empQuan.CitizenId))
            {
                empQuan.CitizenId = "001198004455";
            }
        }
        await context.SaveChangesAsync();

        // Nhân viên Cầu Giấy: Lê Hoàng Nam gắn cố định tại Chi nhánh Cầu Giấy (BR-AN-02)
        var staffUser2 = await context.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == "usr_employee_annhien_03" || u.UserName == "0916667777");
        if (staffUser2 == null)
        {
            staffUser2 = new User
            {
                Id = "usr_employee_annhien_03",
                UserName = "0916667777",
                PasswordHash = PasswordHasher.Hash("123456"),
                FullName = "Lê Hoàng Nam (Nhân viên Cầu Giấy)",
                Email = "nam.le@annhienmart.vn",
                Phone = "0916667777",
                IsActive = true,
                IsEmailVerified = true
            };
            await context.Users.AddAsync(staffUser2);
            await context.SaveChangesAsync();
        }
        else
        {
            EnsureDemoCredential(staffUser2);
        }

        var tuStaff2 = await context.TenantUsers.IgnoreQueryFilters().FirstOrDefaultAsync(tu => tu.TenantId == tenantId && tu.UserId == staffUser2.Id);
        if (tuStaff2 == null)
        {
            await context.TenantUsers.AddAsync(new TenantUser
            {
                TenantId = tenantId,
                UserId = staffUser2.Id,
                Role = SystemRoles.Staff,
                IsActive = true
            });
        }
        else
        {
            tuStaff2.Role = SystemRoles.Staff;
            tuStaff2.IsActive = true;
        }
        await context.SaveChangesAsync();

        var empNam = await context.Employees.IgnoreQueryFilters().FirstOrDefaultAsync(e => e.TenantId == tenantId && (e.EmployeeCode == "NV-AN-02" || e.CitizenId == "001197006655"));
        if (empNam == null)
        {
            empNam = new Employee
            {
                TenantId = tenantId,
                BranchId = branchCauGiay.Id,
                UserId = staffUser2.Id,
                EmployeeCode = "NV-AN-02",
                FullName = "Lê Hoàng Nam (Nhân viên Cầu Giấy)",
                CitizenId = "001197006655",
                Phone = "0916667777",
                Email = "nam.le@annhienmart.vn",
                SalaryType = SalaryType.Monthly,
                BaseRate = 7500000m,
                Status = EmployeeStatus.Active
            };
            await context.Employees.AddAsync(empNam);
        }
        else
        {
            empNam.BranchId = branchCauGiay.Id;
            empNam.UserId = staffUser2.Id;
            if (string.IsNullOrEmpty(empNam.CitizenId))
            {
                empNam.CitizenId = "001197006655";
            }
        }

        // Nhân viên đối chứng ngoài ca tại Chi nhánh Ba Đình: Nguyễn Văn Minh (0915556666 - không phân ca)
        var staffUser3 = await context.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == "usr_employee_annhien_04" || u.UserName == "0915556666");
        if (staffUser3 == null)
        {
            staffUser3 = new User
            {
                Id = "usr_employee_annhien_04",
                UserName = "0915556666",
                PasswordHash = PasswordHasher.Hash("123456"),
                FullName = "Nguyễn Văn Minh (Nhân viên Ba Đình)",
                Email = "minh.nguyen@annhienmart.vn",
                Phone = "0915556666",
                IsActive = true,
                IsEmailVerified = true
            };
            await context.Users.AddAsync(staffUser3);
            await context.SaveChangesAsync();
        }
        else
        {
            EnsureDemoCredential(staffUser3);
        }

        var tuStaff3 = await context.TenantUsers.IgnoreQueryFilters().FirstOrDefaultAsync(tu => tu.TenantId == tenantId && tu.UserId == staffUser3.Id);
        if (tuStaff3 == null)
        {
            await context.TenantUsers.AddAsync(new TenantUser
            {
                TenantId = tenantId,
                UserId = staffUser3.Id,
                Role = SystemRoles.Staff,
                IsActive = true
            });
        }
        else
        {
            tuStaff3.Role = SystemRoles.Staff;
            tuStaff3.IsActive = true;
        }

        var empMinh = await context.Employees.IgnoreQueryFilters().FirstOrDefaultAsync(e => e.TenantId == tenantId && (e.EmployeeCode == "NV-AN-03" || e.CitizenId == "001199005566" || e.CitizenId == "001199005544"));
        if (empMinh == null)
        {
            empMinh = new Employee
            {
                TenantId = tenantId,
                BranchId = branch.Id,
                UserId = staffUser3.Id,
                EmployeeCode = "NV-AN-03",
                FullName = "Nguyễn Văn Minh (Nhân viên Ba Đình)",
                CitizenId = "001199005566",
                Phone = "0915556666",
                Email = "minh.nguyen@annhienmart.vn",
                SalaryType = SalaryType.Monthly,
                BaseRate = 7500000m,
                Status = EmployeeStatus.Active
            };
            await context.Employees.AddAsync(empMinh);
        }
        else
        {
            empMinh.BranchId = branch.Id;
            empMinh.UserId = staffUser3.Id;
            if (string.IsNullOrEmpty(empMinh.CitizenId))
            {
                empMinh.CitizenId = "001199005566";
            }
        }
        await context.SaveChangesAsync();

        // 3b. Cấu hình ca làm việc All-time 24/7 (00:00:00 - 23:59:59) & Lịch phân ca cho cả 2 nhân viên Ba Đình
        var shiftConfigSang = await context.ShiftConfigs.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.TenantId == tenantId && s.ShiftCode == "CA-AN-FULL");
        if (shiftConfigSang == null)
        {
            shiftConfigSang = new ShiftConfig
            {
                TenantId = tenantId,
                BranchId = branch.Id,
                ShiftCode = "CA-AN-FULL",
                ShiftName = "Ca Trực Bán Hàng Toàn Thời Gian 24/7",
                StartTime = TimeSpan.Zero,
                EndTime = new TimeSpan(23, 59, 59),
                DurationHours = 24.00m,
                AllowedCheckInStart = TimeSpan.Zero,
                AllowedCheckInEnd = new TimeSpan(23, 59, 59),
                IsActive = true
            };
            await context.ShiftConfigs.AddAsync(shiftConfigSang);
            await context.SaveChangesAsync();
        }
        else
        {
            shiftConfigSang.BranchId = branch.Id;
            shiftConfigSang.ShiftName = "Ca Trực Bán Hàng Toàn Thời Gian 24/7";
            shiftConfigSang.StartTime = TimeSpan.Zero;
            shiftConfigSang.EndTime = new TimeSpan(23, 59, 59);
            shiftConfigSang.DurationHours = 24.00m;
            shiftConfigSang.AllowedCheckInStart = TimeSpan.Zero;
            shiftConfigSang.AllowedCheckInEnd = new TimeSpan(23, 59, 59);
            shiftConfigSang.IsActive = true;
            shiftConfigSang.IsDeleted = false;
            await context.SaveChangesAsync();
        }

        // Upsert lịch phân ca 24/7 (-7 đến +30 ngày) cho cả Phạm Thị Yến (0917778888) và Trần Minh Quân (0914445555) tại Ba Đình
        // Lưu ý: Không phân ca cho Nguyễn Văn Minh (0915556666) để giữ kịch bản đối chứng chặn ngoài ca
        var todayVn = DateTime.UtcNow.AddHours(7).Date;
        var allTimeBaDinhEmployees = new[] { empYen, empQuan };
        for (int dayOffset = -7; dayOffset <= 30; dayOffset++)
        {
            var targetDate = todayVn.AddDays(dayOffset);
            foreach (var targetEmp in allTimeBaDinhEmployees)
            {
                var existingSchedule = await context.EmployeeSchedules.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.EmployeeId == targetEmp.Id && s.BranchId == branch.Id && s.WorkDate == targetDate);

                if (existingSchedule == null)
                {
                    await context.EmployeeSchedules.AddAsync(new EmployeeSchedule
                    {
                        TenantId = tenantId,
                        BranchId = branch.Id,
                        EmployeeId = targetEmp.Id,
                        ShiftConfigId = shiftConfigSang.Id,
                        WorkDate = targetDate,
                        ShiftCode = shiftConfigSang.ShiftCode,
                        ShiftName = shiftConfigSang.ShiftName,
                        PlannedStartTime = TimeSpan.Zero,
                        PlannedEndTime = new TimeSpan(23, 59, 59),
                        DurationHours = 24.00m,
                        AllowedCheckInStart = TimeSpan.Zero,
                        AllowedCheckInEnd = new TimeSpan(23, 59, 59),
                        Status = ScheduleStatus.Scheduled
                    });
                }
                else
                {
                    existingSchedule.IsDeleted = false;
                    existingSchedule.ShiftConfigId = shiftConfigSang.Id;
                    existingSchedule.ShiftCode = shiftConfigSang.ShiftCode;
                    existingSchedule.ShiftName = shiftConfigSang.ShiftName;
                    existingSchedule.PlannedStartTime = TimeSpan.Zero;
                    existingSchedule.PlannedEndTime = new TimeSpan(23, 59, 59);
                    existingSchedule.DurationHours = 24.00m;
                    existingSchedule.AllowedCheckInStart = TimeSpan.Zero;
                    existingSchedule.AllowedCheckInEnd = new TimeSpan(23, 59, 59);
                    if (existingSchedule.Status == ScheduleStatus.Cancelled || existingSchedule.Status == ScheduleStatus.Draft)
                    {
                        existingSchedule.Status = ScheduleStatus.Scheduled;
                    }
                }
            }
        }
        await context.SaveChangesAsync();

        // 4. Danh mục sản phẩm của An Nhiên Mart
        var catRau = await context.Categories.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.TenantId == tenantId && c.CategoryCode == "CAT-AN-RAU");
        if (catRau == null)
        {
            catRau = new Category
            {
                TenantId = tenantId,
                CategoryCode = "CAT-AN-RAU",
                Name = "Rau Củ & Trái Cây Sạch",
                Description = "Rau củ quả tươi sạch hữu cơ tiêu chuẩn VietGAP"
            };
            await context.Categories.AddAsync(catRau);
        }

        var catThucPham = await context.Categories.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.TenantId == tenantId && c.CategoryCode == "CAT-AN-THUC-PHAM");
        if (catThucPham == null)
        {
            catThucPham = new Category
            {
                TenantId = tenantId,
                CategoryCode = "CAT-AN-THUC-PHAM",
                Name = "Thực Phẩm & Đồ Uống Hữu Cơ",
                Description = "Thực phẩm khô organic, gia vị và sữa tươi sạch"
            };
            await context.Categories.AddAsync(catThucPham);
        }
        await context.SaveChangesAsync();

        // 5. Sản phẩm cơ bản của cửa hàng An Nhiên (tra cứu đúng TenantId để không trùng Barcode với tenant khác)
        async Task<Product> UpsertAnNhienProductAsync(
            string code,
            string barcode,
            Guid categoryId,
            string name,
            string baseUnit,
            decimal costPrice,
            decimal sellingPrice,
            decimal minStock,
            decimal maxStock,
            decimal vatRate,
            bool hasBatches,
            bool isFavorite)
        {
            var prod = await context.Products.IgnoreQueryFilters()
                .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.ProductCode == code);

            var barcodeTakenByOther = await context.Products.IgnoreQueryFilters()
                .AnyAsync(p => !p.IsDeleted && p.Barcode == barcode && (prod == null || p.Id != prod.Id));
            var safeBarcode = barcodeTakenByOther ? $"{barcode}9" : barcode;

            if (prod == null)
            {
                prod = new Product
                {
                    TenantId = tenantId,
                    CategoryId = categoryId,
                    ProductCode = code,
                    Barcode = safeBarcode,
                    Name = name,
                    BaseUnit = baseUnit,
                    CostPrice = costPrice,
                    SellingPrice = sellingPrice,
                    MinStock = minStock,
                    MaxStock = maxStock,
                    VatRate = vatRate,
                    ProductType = "STANDARD",
                    Status = ProductStatus.Active,
                    HasBatches = hasBatches,
                    IsFavorite = isFavorite
                };
                await context.Products.AddAsync(prod);
            }
            else
            {
                prod.IsDeleted = false;
                prod.Status = ProductStatus.Active;
                prod.CategoryId = categoryId;
                if (string.IsNullOrWhiteSpace(prod.Barcode))
                {
                    prod.Barcode = safeBarcode;
                }
            }

            return prod;
        }

        var prodGao = await UpsertAnNhienProductAsync(
            "AN-GAO-01", "8936099880011", catThucPham.Id,
            "Gạo lứt huyết rồng organic 2kg", "Túi", 38000m, 55000m, 10m, 200m, 0.05m, true, true);
        var prodTrung = await UpsertAnNhienProductAsync(
            "AN-TRUNG-01", "8936099880028", catThucPham.Id,
            "Trứng gà thảo mộc vỉ 10 quả", "Vỉ", 32000m, 45000m, 20m, 300m, 0.05m, true, true);
        var prodMam = await UpsertAnNhienProductAsync(
            "AN-MAM-01", "8936099880035", catThucPham.Id,
            "Nước mắm cá cơm cốt nhĩ 500ml", "Chai", 62000m, 85000m, 15m, 150m, 0.08m, false, false);
        var prodTao = await UpsertAnNhienProductAsync(
            "AN-TAO-01", "8936099880042", catRau.Id,
            "Táo Envy New Zealand Organic", "Kg", 90000m, 125000m, 10m, 100m, 0.05m, true, true);
        var prodSua = await UpsertAnNhienProductAsync(
            "AN-SUA-01", "8936099880059", catThucPham.Id,
            "Sữa tươi thanh trùng Mộc Châu 900ml", "Chai", 28000m, 39000m, 20m, 150m, 0.08m, true, true);
        await context.SaveChangesAsync();

        // 6. Tồn kho BranchStock cho từng sản phẩm theo từng chi nhánh (đảm bảo luôn có lô tồn FEFO khả dụng)
        var stockSeeds = new (Guid BranchId, Guid ProductId, string BatchCode, DateTime ExpiryDate, decimal Qty, decimal Reserved, decimal Cost)[]
        {
            // BR-AN-01 (Ba Đình - 5 SP)
            (branch.Id, prodGao.Id, "BATCH-AN-GAO-2611", DateTime.UtcNow.AddMonths(12), 80m, 2m, 38000m),
            (branch.Id, prodTrung.Id, "BATCH-AN-TRUNG-2611", DateTime.UtcNow.AddDays(25), 120m, 0m, 32000m),
            (branch.Id, prodMam.Id, "BATCH-AN-MAM-2611", DateTime.UtcNow.AddYears(2), 65m, 0m, 62000m),
            (branch.Id, prodTao.Id, "BATCH-AN-TAO-2611", DateTime.UtcNow.AddDays(18), 50m, 2m, 90000m),
            (branch.Id, prodSua.Id, "BATCH-AN-SUA-2611", DateTime.UtcNow.AddDays(10), 90m, 0m, 28000m),

            // BR-AN-02 (Cầu Giấy - 5 SP)
            (branchCauGiay.Id, prodGao.Id, "BATCH-AN-GAO-2611-CG", DateTime.UtcNow.AddMonths(10), 60m, 0m, 38000m),
            (branchCauGiay.Id, prodTrung.Id, "BATCH-AN-TRUNG-2611-CG", DateTime.UtcNow.AddDays(20), 85m, 0m, 32000m),
            (branchCauGiay.Id, prodMam.Id, "BATCH-AN-MAM-2611-CG", DateTime.UtcNow.AddYears(2), 40m, 0m, 62000m),
            (branchCauGiay.Id, prodTao.Id, "BATCH-AN-TAO-2611-CG", DateTime.UtcNow.AddDays(14), 35m, 0m, 90000m),
            (branchCauGiay.Id, prodSua.Id, "BATCH-AN-SUA-2611-CAN-DATE", DateTime.UtcNow.AddDays(5), 30m, 0m, 28000m),
            (branchCauGiay.Id, prodSua.Id, "BATCH-AN-SUA-2611-MOI", DateTime.UtcNow.AddDays(25), 50m, 0m, 28000m),

            // BR-AN-03 (Đống Đa - 3 SP)
            (branchDongDa.Id, prodGao.Id, "BATCH-AN-GAO-2611-DD", DateTime.UtcNow.AddMonths(11), 45m, 0m, 38000m),
            (branchDongDa.Id, prodTao.Id, "BATCH-AN-TAO-2611-DD", DateTime.UtcNow.AddDays(16), 25m, 0m, 90000m),
            (branchDongDa.Id, prodSua.Id, "BATCH-AN-SUA-2611-DD", DateTime.UtcNow.AddDays(18), 40m, 0m, 28000m)
        };

        var existingAnStocks = await context.BranchStocks.IgnoreQueryFilters()
            .Where(s => s.TenantId == tenantId && !s.IsDeleted)
            .ToListAsync();

        var stockChanged = false;
        foreach (var seed in stockSeeds)
        {
            var hasBatch = existingAnStocks.Any(s => s.BranchId == seed.BranchId && s.ProductId == seed.ProductId && s.BatchCode == seed.BatchCode);
            if (!hasBatch)
            {
                await context.BranchStocks.AddAsync(new BranchStock
                {
                    TenantId = tenantId,
                    BranchId = seed.BranchId,
                    ProductId = seed.ProductId,
                    BatchCode = seed.BatchCode,
                    ExpiryDate = seed.ExpiryDate,
                    QuantityOnHand = seed.Qty,
                    QuantityReserved = seed.Reserved,
                    LandedCostPerUnit = seed.Cost
                });
                stockChanged = true;
            }
        }
        if (stockChanged)
        {
            await context.SaveChangesAsync();
        }

        // 6d. Danh mục sản phẩm gán theo từng chi nhánh (BranchProducts): BR-AN-01 (5 SP), BR-AN-02 (5 SP), BR-AN-03 (3 SP)
        var branchProductMap = new (Guid BranchId, Guid ProductId)[]
        {
            (branch.Id, prodGao.Id),
            (branch.Id, prodTrung.Id),
            (branch.Id, prodMam.Id),
            (branch.Id, prodTao.Id),
            (branch.Id, prodSua.Id),
            (branchCauGiay.Id, prodGao.Id),
            (branchCauGiay.Id, prodTrung.Id),
            (branchCauGiay.Id, prodMam.Id),
            (branchCauGiay.Id, prodTao.Id),
            (branchCauGiay.Id, prodSua.Id),
            (branchDongDa.Id, prodGao.Id),
            (branchDongDa.Id, prodTao.Id),
            (branchDongDa.Id, prodSua.Id)
        };

        var validPairs = branchProductMap.ToHashSet();
        var anNhienBranchIds = new[] { branch.Id, branchCauGiay.Id, branchDongDa.Id };
        var existingAnNhienBranchProducts = await context.BranchProducts.IgnoreQueryFilters()
            .Where(bp => bp.TenantId == tenantId && anNhienBranchIds.Contains(bp.BranchId))
            .ToListAsync();

        var bpChanged = false;
        // Dọn dẹp các liên kết BranchProduct cũ bị trỏ nhầm sang ProductId của tenant khác
        var validAnNhienProductIds = new HashSet<Guid> { prodGao.Id, prodTrung.Id, prodMam.Id, prodTao.Id, prodSua.Id };
        foreach (var oldBp in existingAnNhienBranchProducts.Where(bp => !bp.IsDeleted && !validAnNhienProductIds.Contains(bp.ProductId)))
        {
            oldBp.IsDeleted = true;
            bpChanged = true;
        }

        foreach (var (targetBranchId, targetProductId) in validPairs)
        {
            var existingBp = existingAnNhienBranchProducts.FirstOrDefault(
                bp => bp.BranchId == targetBranchId && bp.ProductId == targetProductId);
            if (existingBp == null)
            {
                await context.BranchProducts.AddAsync(new BranchProduct
                {
                    TenantId = tenantId,
                    BranchId = targetBranchId,
                    ProductId = targetProductId,
                    Status = "Active"
                });
                bpChanged = true;
            }
            else if (existingBp.IsDeleted || !string.Equals(existingBp.Status, "Active", StringComparison.OrdinalIgnoreCase))
            {
                existingBp.IsDeleted = false;
                existingBp.Status = "Active";
                bpChanged = true;
            }
        }
        if (bpChanged)
        {
            await context.SaveChangesAsync();
        }

        // 7. Khách hàng của An Nhiên Mart
        var custMinh = await context.Customers.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.TenantId == tenantId && c.Phone == "0934567890");
        if (custMinh == null)
        {
            custMinh = new Customer
            {
                TenantId = tenantId,
                CustomerCode = "KH-AN-001",
                Name = "Bác Minh (Khách quen Ba Đình)",
                Phone = "0934567890",
                Email = "minh.bac@gmail.com",
                Address = "15 Hoàng Hoa Thám, Ba Đình, Hà Nội",
                MembershipTier = "Gold",
                RewardPoints = 150,
                TotalSpending = 4500000m,
                AllowDebt = true,
                TotalDebt = 0m
            };
            await context.Customers.AddAsync(custMinh);
        }

        var custHa = await context.Customers.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.TenantId == tenantId && c.Phone == "0976543210");
        if (custHa == null)
        {
            custHa = new Customer
            {
                TenantId = tenantId,
                CustomerCode = "KH-AN-002",
                Name = "Chị Thu Hà (Khách đặt trước)",
                Phone = "0976543210",
                Email = "thuha.hn@gmail.com",
                Address = "28 Ngọc Hà, Ba Đình, Hà Nội",
                MembershipTier = "Silver",
                RewardPoints = 80,
                TotalSpending = 2800000m,
                AllowDebt = true,
                TotalDebt = 250000m
            };
            await context.Customers.AddAsync(custHa);
        }
        await context.SaveChangesAsync();

        // 8. Đơn hàng mẫu của An Nhiên Mart
        if (await context.Orders.IgnoreQueryFilters().AnyAsync(o => o.TenantId == tenantId)) return;

        var now = DateTime.UtcNow;

        // Đơn 1: Bán lẻ tại quầy đã thanh toán tiền mặt
        var order1 = new Order
        {
            TenantId = tenantId,
            BranchId = branch.Id,
            CustomerId = custMinh.Id,
            OrderCode = "HD-AN-20261102-001",
            OrderDate = now.AddHours(-2),
            TotalAmount = 84000m,
            DiscountAmount = 0m,
            FinalAmount = 84000m,
            DepositAmount = 100000m,
            PaymentMethod = "CASH",
            PaymentStatus = "PAID",
            Status = "Completed",
            Note = "Khách thanh toán tiền mặt tại quầy An Nhiên Mart"
        };
        order1.Details.Add(new OrderDetail
        {
            OrderId = order1.Id,
            ProductId = prodSua.Id,
            LineNumber = 1,
            Qty = 1,
            UnitPrice = 39000m,
            ActualPrice = 39000m
        });
        order1.Details.Add(new OrderDetail
        {
            OrderId = order1.Id,
            ProductId = prodTrung.Id,
            LineNumber = 2,
            Qty = 1,
            UnitPrice = 45000m,
            ActualPrice = 45000m
        });

        // Đơn 2: Đơn đặt trước giữ kho FEFO, đã cọc 100k VietQR
        var preOrder1 = new Order
        {
            TenantId = tenantId,
            BranchId = branch.Id,
            CustomerId = custHa.Id,
            OrderCode = "PO-AN-20261102-001",
            OrderDate = now.AddHours(-1),
            TotalAmount = 360000m,
            DiscountAmount = 0m,
            FinalAmount = 360000m,
            DepositAmount = 100000m,
            PaymentMethod = "VIETQR",
            PaymentStatus = "PARTIAL",
            Status = "PreOrder",
            Note = "Hẹn nhận hàng 18:30 tối nay. Đã cọc 100k qua VietQR SePay."
        };
        preOrder1.Details.Add(new OrderDetail
        {
            OrderId = preOrder1.Id,
            ProductId = prodGao.Id,
            LineNumber = 1,
            Qty = 2,
            UnitPrice = 55000m,
            ActualPrice = 55000m
        });
        preOrder1.Details.Add(new OrderDetail
        {
            OrderId = preOrder1.Id,
            ProductId = prodTao.Id,
            LineNumber = 2,
            Qty = 2,
            UnitPrice = 125000m,
            ActualPrice = 125000m
        });

        await context.Orders.AddRangeAsync(order1, preOrder1);
        await context.SaveChangesAsync();

        // 13. Seed Ca làm việc POS (ShiftZReport) và phiếu thu chi lẻ (ShiftCashMovement)
        var shift1Id = Guid.Parse("01944321-ca01-7000-8000-000000000001");
        var shift2Id = Guid.Parse("01944321-ca02-7000-8000-000000000002");
        var tillId = Guid.Parse("01a19664-6210-7b12-a9eb-6fa16ca5b9db");
        var staffGuid = Guid.TryParse(staffUser.Id, out var parsedStaffGuid) ? parsedStaffGuid : Guid.NewGuid();

        if (!await context.ShiftZReports.IgnoreQueryFilters().AnyAsync(s => s.Id == shift1Id))
        {
            var shift1 = new ShiftZReport
            {
                Id = shift1Id,
                TenantId = tenantId,
                BranchId = branch.Id,
                TillId = tillId,
                ShiftCode = "CA-20261105-01",
                ShiftName = "Ca Sáng 05/11",
                CashierUserId = staffGuid,
                ReceiverUserId = "usr_employee_annhien_03",
                OpenedAtUtc = now.AddDays(-1).AddHours(-8),
                ClosedAtUtc = now.AddDays(-1).AddHours(-1),
                OpeningFloatExpected = 1500000m,
                OpeningFloat = 1500000m,
                OpeningDiscrepancy = 0m,
                TotalCashIn = 500000m,
                TotalCashOut = 200000m,
                TotalBankRevenue = 360000m,
                TotalOrdersCount = 5,
                ExpectedCash = 2800000m,
                ActualCashCounted = 2800000m,
                Discrepancy = 0m,
                RetainedFloatAmount = 1500000m,
                DroppedToSafeAmount = 1300000m,
                HandoverStatus = HandoverStatus.CONFIRMED,
                Status = ShiftState.Closed,
                Notes = "Bàn giao ca đầy đủ, không chênh lệch."
            };

            shift1.CashMovements.Add(new ShiftCashMovement
            {
                TenantId = tenantId,
                ShiftId = shift1Id,
                MovementType = CashMovementType.IN,
                Category = CashMovementCategory.CASH_DEPOSIT,
                Amount = 500000m,
                Reason = "Nạp tiền lẻ đầu ca",
                CreatedByName = "Phạm Thị Yến",
                OccurredAtUtc = now.AddDays(-1).AddHours(-7)
            });

            shift1.CashMovements.Add(new ShiftCashMovement
            {
                TenantId = tenantId,
                ShiftId = shift1Id,
                MovementType = CashMovementType.OUT,
                Category = CashMovementCategory.EXPENSE,
                Amount = 200000m,
                Reason = "Chi tiền rác và túi đựng",
                CreatedByName = "Phạm Thị Yến",
                OccurredAtUtc = now.AddDays(-1).AddHours(-4)
            });

            var shift2 = new ShiftZReport
            {
                Id = shift2Id,
                TenantId = tenantId,
                BranchId = branch.Id,
                TillId = tillId,
                ShiftCode = "CA-20261106-01",
                ShiftName = "Ca Sáng 06/11",
                CashierUserId = staffGuid,
                PrevShiftId = shift1Id,
                OpenedAtUtc = now.AddHours(-3),
                OpeningFloatExpected = 1500000m,
                OpeningFloat = 1500000m,
                OpeningDiscrepancy = 0m,
                TotalCashIn = 0m,
                TotalCashOut = 0m,
                TotalBankRevenue = 0m,
                TotalOrdersCount = 1,
                ExpectedCash = 1584000m,
                RetainedFloatAmount = 0m,
                DroppedToSafeAmount = 0m,
                HandoverStatus = HandoverStatus.PENDING,
                Status = ShiftState.Open,
                Notes = "Ca bán hàng đang mở."
            };

            order1.ShiftId = shift2Id;

            await context.ShiftZReports.AddRangeAsync(shift1, shift2);
            await context.SaveChangesAsync();
        }
    }

    // =========================================================================
    // PHÂN HỆ TAX, FINANCE, PO MATRIX & POS RETAIL (HỢP NHẤT TỪ DatabaseSeeder)
    // =========================================================================
    private const string DefaultTenantId = "tenant_mt_mart_01";

    public static async Task SeedTaxAndFinanceAsync(AppDbContext context, ILogger? logger = null, CancellationToken ct = default)
    {
        try
        {
            // Migration FE-10: Bổ sung cột CostPriceAtSale và Backfill giá vốn đơn hàng
            try
            {
                await context.Database.ExecuteSqlRawAsync(
                    "ALTER TABLE IF EXISTS \"OrderDetails\" ADD COLUMN IF NOT EXISTS \"CostPriceAtSale\" numeric(18,4) NOT NULL DEFAULT 0.0000;",
                    ct);

                await context.Database.ExecuteSqlRawAsync(
                    "UPDATE \"OrderDetails\" od " +
                    "SET \"CostPriceAtSale\" = COALESCE(p.\"CostPrice\", od.\"UnitPrice\" * 0.70) " +
                    "FROM \"Products\" p " +
                    "WHERE od.\"ProductID\" = p.\"ProductID\" AND od.\"CostPriceAtSale\" = 0;",
                    ct);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "CostPriceAtSale migration check passed or already applied.");
            }

            // 0. Seed SaaS Tenant & Default Branch for Grocery Store
            if (!await context.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Id == DefaultTenantId, ct))
            {
                logger?.LogInformation("Seeding SaaS Tenant for Grocery Store...");
                var groceryTenant = new Domain.Entities.SaaS.Tenant
                {
                    Id = DefaultTenantId,
                    Name = "Hộ Kinh Doanh Tạp Hóa Siêu Thị MT-GRMS",
                    Code = "GROCERY-001",
                    Subdomain = "grocery001",
                    TaxCode = "0318999888",
                    Status = "ACTIVE",
                    Address = "12 Nguyễn Huệ, Phường Bến Nghé, Quận 1, TP.HCM",
                    City = "Hồ Chí Minh",
                    CurrentPlan = "PRO",
                    SubscriptionExpiresAt = DateTime.UtcNow.AddYears(1)
                };
                await context.Tenants.AddAsync(groceryTenant, ct);
                await context.SaveChangesAsync(ct);
            }

            var groceryBranchId = Guid.Parse("cd5e8d1b-b08b-448d-beff-b85ea5873d52");
            if (!await context.Branches.IgnoreQueryFilters().AnyAsync(b => b.Id == groceryBranchId, ct))
            {
                logger?.LogInformation("Seeding Default Branch for Grocery Store...");
                var branch = new Domain.Entities.SaaS.Branch
                {
                    Id = groceryBranchId,
                    TenantId = DefaultTenantId,
                    BranchCode = "BR-Q1-GROCERY",
                    Name = "Cửa hàng Tạp hóa Chi nhánh Quận 1",
                    Address = "12 Nguyễn Huệ, Quận 1, TP.HCM",
                    IsActive = true
                };
                await context.Branches.AddAsync(branch, ct);
                await context.SaveChangesAsync(ct);
            }

            // 1. Seed Users (Chủ tiệm với mã PIN 1234 đã băm PBKDF2)
            if (!await context.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == "user-owner-001", ct))
            {
                logger?.LogInformation("Seeding Users...");
                var saltBytes = RandomNumberGenerator.GetBytes(16);
                var pinHashBytes = Rfc2898DeriveBytes.Pbkdf2(
                    "1234",
                    saltBytes,
                    600_000,
                    HashAlgorithmName.SHA256,
                    32
                );

                var passwordHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("Owner@123456"))).ToLowerInvariant();
                var staffPasswordHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("Staff@123456"))).ToLowerInvariant();

                var owner = new User
                {
                    Id = "user-owner-001",
                    UserName = "store_owner",
                    FullName = "Chủ Cửa Hàng",
                    Email = "owner@mtgrms.vn",
                    Password = passwordHash,
                    SecurityPinSalt = Convert.ToBase64String(saltBytes),
                    SecurityPinHash = Convert.ToBase64String(pinHashBytes),
                    FailedPinAttempts = 0
                };

                var cashier = new User
                {
                    Id = "user-cashier-001",
                    UserName = "staff_cashier",
                    FullName = "Nhân Viên Thu Ngân",
                    Email = "cashier@mtgrms.vn",
                    Password = staffPasswordHash
                };

                var employee = new User
                {
                    Id = "user-employee-001",
                    UserName = "staff_employee",
                    FullName = "Nhân Viên Cửa Hàng (Employee)",
                    Email = "employee@mtgrms.vn",
                    Password = staffPasswordHash
                };

                await context.Users.AddRangeAsync(new[] { owner, cashier, employee }, ct);
                await context.SaveChangesAsync(ct);
            }

            // 2. Seed Categories
            if (!await context.Categories.IgnoreQueryFilters().AnyAsync(c => c.Id == Guid.Parse("11111111-1111-1111-1111-111111111111"), ct))
            {
                logger?.LogInformation("Seeding Categories...");
                var fmcgCategory = new Category
                {
                    Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    TenantId = DefaultTenantId,
                    CategoryCode = "FMCG",
                    CategoryName = "Hàng tiêu dùng nhanh (Bánh kẹo, mì gói, nước ngọt)"
                };

                var alcoholCategory = new Category
                {
                    Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                    TenantId = DefaultTenantId,
                    CategoryCode = "ALCOHOL",
                    CategoryName = "Bia, Rượu, Đồ uống có cồn (Thuế TTĐB)"
                };

                var tobaccoCategory = new Category
                {
                    Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                    TenantId = DefaultTenantId,
                    CategoryCode = "TOBACCO",
                    CategoryName = "Thuốc lá, Xì gà (Thuế TTĐB)"
                };

                await context.Categories.AddRangeAsync(new[] { fmcgCategory, alcoholCategory, tobaccoCategory }, ct);
                await context.SaveChangesAsync(ct);
            }

            // 3. Seed Products (Gắn TaxCategory theo NQ 204: STANDARD_1PCT vs NO_REDUCTION_1PCT)
            if (!await context.Products.IgnoreQueryFilters().AnyAsync(p => p.Id == Guid.Parse("aaaaaaaa-1111-1111-1111-111111111111"), ct))
            {
                logger?.LogInformation("Seeding Products with TaxCategory...");
                var products = new List<Product>
                {
                    new Product
                    {
                        Id = Guid.Parse("aaaaaaaa-1111-1111-1111-111111111111"),
                        TenantId = DefaultTenantId,
                        SKU = "FMCG-001",
                        Barcode = "893603601001",
                        ProductName = "Bánh Chocopie Orion hộp 12 cái (360g)",
                        CategoryId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                        BasePrice = 65000m,
                        CostPrice = 48000m,
                        TaxCategory = TaxCategoryConstants.Standard1Pct, // Được giảm GTGT còn 0.8%
                        UnitName = "Hộp"
                    },
                    new Product
                    {
                        Id = Guid.Parse("aaaaaaaa-2222-2222-2222-222222222222"),
                        TenantId = DefaultTenantId,
                        SKU = "FMCG-002",
                        Barcode = "893456302002",
                        ProductName = "Mì tôm Hảo Hảo tôm chua cay thùng 30 gói",
                        CategoryId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                        BasePrice = 135000m,
                        CostPrice = 110000m,
                        TaxCategory = TaxCategoryConstants.Standard1Pct, // Được giảm GTGT còn 0.8%
                        UnitName = "Thùng"
                    },
                    new Product
                    {
                        Id = Guid.Parse("aaaaaaaa-3333-3333-3333-333333333333"),
                        TenantId = DefaultTenantId,
                        SKU = "FMCG-003",
                        Barcode = "893504950003",
                        ProductName = "Nước ngọt Coca-Cola lốc 6 lon 320ml",
                        CategoryId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                        BasePrice = 58000m,
                        CostPrice = 45000m,
                        TaxCategory = TaxCategoryConstants.Standard1Pct, // Được giảm GTGT còn 0.8%
                        UnitName = "Lốc"
                    },
                    new Product
                    {
                        Id = Guid.Parse("bbbbbbbb-1111-1111-1111-111111111111"),
                        TenantId = DefaultTenantId,
                        SKU = "ALC-001",
                        Barcode = "893482221004",
                        ProductName = "Bia Tiger lon thùng 24 lon 330ml",
                        CategoryId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                        BasePrice = 385000m,
                        CostPrice = 340000m,
                        TaxCategory = TaxCategoryConstants.NoReduction1Pct, // Hàng TTĐB: Giữ nguyên 1.0%
                        UnitName = "Thùng"
                    },
                    new Product
                    {
                        Id = Guid.Parse("bbbbbbbb-2222-2222-2222-222222222222"),
                        TenantId = DefaultTenantId,
                        SKU = "ALC-002",
                        Barcode = "893482221005",
                        ProductName = "Bia Heineken bạc thùng 24 lon 330ml",
                        CategoryId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                        BasePrice = 440000m,
                        CostPrice = 395000m,
                        TaxCategory = TaxCategoryConstants.NoReduction1Pct, // Hàng TTĐB: Giữ nguyên 1.0%
                        UnitName = "Thùng"
                    },
                    new Product
                    {
                        Id = Guid.Parse("cccccccc-1111-1111-1111-111111111111"),
                        TenantId = DefaultTenantId,
                        SKU = "TOB-001",
                        Barcode = "893601111006",
                        ProductName = "Thuốc lá Craven A đỏ bao mềm",
                        CategoryId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                        BasePrice = 32000m,
                        CostPrice = 28000m,
                        TaxCategory = TaxCategoryConstants.NoReduction1Pct, // Hàng TTĐB: Giữ nguyên 1.0%
                        UnitName = "Gói"
                    }
                };

                var existingProductIds = await context.Products.IgnoreQueryFilters().Select(p => p.Id).ToListAsync(ct);
                var existingBarcodes = await context.Products.IgnoreQueryFilters().Where(p => p.Barcode != null).Select(p => p.Barcode!).ToListAsync(ct);
                var productsToAdd = products
                    .Where(p => !existingProductIds.Contains(p.Id) && (p.Barcode == null || !existingBarcodes.Contains(p.Barcode)))
                    .ToList();

                if (productsToAdd.Any())
                {
                    await context.Products.AddRangeAsync(productsToAdd, ct);
                    await context.SaveChangesAsync(ct);
                }
            }

            // Resolve default branch for Orders & Expenses foreign key
            var defaultBranch = await context.Branches.IgnoreQueryFilters().FirstOrDefaultAsync(ct);
            var branchId = defaultBranch?.Id ?? Guid.Parse("99999999-9999-9999-9999-999999999999");
            if (defaultBranch == null)
            {
                defaultBranch = new Domain.Entities.SaaS.Branch
                {
                    Id = branchId,
                    TenantId = DefaultTenantId,
                    BranchCode = "BR-DEFAULT",
                    Name = "Chi Nhánh Mặc Định",
                    IsActive = true
                };
                await context.Branches.AddAsync(defaultBranch, ct);
                await context.SaveChangesAsync(ct);
            }

            // 4. Seed Orders & OrderItems (Phát sinh trong Q3/2026: 01/07/2026 - 30/09/2026)
            if (!await context.Orders.IgnoreQueryFilters().AnyAsync(o => o.Id == Guid.Parse("f0000001-1111-1111-1111-111111111111"), ct))
            {
                logger?.LogInformation("Seeding Orders and OrderItems...");

                // Đơn hàng 1: Ngày 15/07/2026 - Hàng thường (Được giảm GTGT 0.8%)
                var order1 = new Order
                {
                    Id = Guid.Parse("f0000001-1111-1111-1111-111111111111"),
                    TenantId = DefaultTenantId,
                    OrderCode = "ORD-20260715-001",
                    BranchId = branchId,
                    SubTotal = 465000m,
                    DiscountAmount = 0m,
                    TotalAmount = 465000m,
                    PaymentMethod = "CASH",
                    PaymentStatus = "PAID",
                    CreatedAt = new DateTime(2026, 7, 15, 10, 30, 0, DateTimeKind.Utc)
                };

                order1.Items.Add(new OrderItem
                {
                    Id = Guid.NewGuid(),
                    OrderId = order1.Id,
                    ProductId = Guid.Parse("aaaaaaaa-2222-2222-2222-222222222222"), // Mì Hảo Hảo
                    ProductName = "Mì tôm Hảo Hảo tôm chua cay thùng 30 gói",
                    UnitPrice = 135000m,
                    Quantity = 2m,
                    TotalLineAmount = 270000m,
                    AppliedVatRate = 0.008m, // Giảm 20%
                    VatAmount = 2160m, // 270.000 * 0.8%
                    VatDiscountAmount = 540m
                });

                order1.Items.Add(new OrderItem
                {
                    Id = Guid.NewGuid(),
                    OrderId = order1.Id,
                    ProductId = Guid.Parse("aaaaaaaa-1111-1111-1111-111111111111"), // Chocopie
                    ProductName = "Bánh Chocopie Orion hộp 12 cái (360g)",
                    UnitPrice = 65000m,
                    Quantity = 3m,
                    TotalLineAmount = 195000m,
                    AppliedVatRate = 0.008m, // Giảm 20%
                    VatAmount = 1560m,
                    VatDiscountAmount = 390m
                });

                // Đơn hàng 2: Ngày 20/08/2026 - Hỗn hợp (Hàng thường 0.8% + Bia/Thuốc lá 1.0%)
                var order2 = new Order
                {
                    Id = Guid.Parse("f0000002-2222-2222-2222-222222222222"),
                    TenantId = DefaultTenantId,
                    OrderCode = "ORD-20260820-002",
                    BranchId = branchId,
                    SubTotal = 565000m,
                    DiscountAmount = 0m,
                    TotalAmount = 565000m,
                    PaymentMethod = "BANK_TRANSFER",
                    PaymentStatus = "PAID",
                    CreatedAt = new DateTime(2026, 8, 20, 15, 45, 0, DateTimeKind.Utc)
                };

                order2.Items.Add(new OrderItem
                {
                    Id = Guid.NewGuid(),
                    OrderId = order2.Id,
                    ProductId = Guid.Parse("aaaaaaaa-3333-3333-3333-333333333333"), // Coca
                    ProductName = "Nước ngọt Coca-Cola lốc 6 lon 320ml",
                    UnitPrice = 58000m,
                    Quantity = 2m,
                    TotalLineAmount = 116000m,
                    AppliedVatRate = 0.008m, // Giảm 20%
                    VatAmount = 928m,
                    VatDiscountAmount = 232m
                });

                order2.Items.Add(new OrderItem
                {
                    Id = Guid.NewGuid(),
                    OrderId = order2.Id,
                    ProductId = Guid.Parse("bbbbbbbb-1111-1111-1111-111111111111"), // Bia Tiger
                    ProductName = "Bia Tiger lon thùng 24 lon 330ml",
                    UnitPrice = 385000m,
                    Quantity = 1m,
                    TotalLineAmount = 385000m,
                    AppliedVatRate = 0.010m, // Hàng TTĐB: 1.0%
                    VatAmount = 3850m,
                    VatDiscountAmount = 0m
                });

                order2.Items.Add(new OrderItem
                {
                    Id = Guid.NewGuid(),
                    OrderId = order2.Id,
                    ProductId = Guid.Parse("cccccccc-1111-1111-1111-111111111111"), // Thuốc lá
                    ProductName = "Thuốc lá Craven A đỏ bao mềm",
                    UnitPrice = 32000m,
                    Quantity = 2m,
                    TotalLineAmount = 64000m,
                    AppliedVatRate = 0.010m, // Hàng TTĐB: 1.0%
                    VatAmount = 640m,
                    VatDiscountAmount = 0m
                });

                // Đơn hàng 3: Ngày 10/09/2026 - Doanh số bán buôn giá trị cao
                var order3 = new Order
                {
                    Id = Guid.Parse("f0000003-3333-3333-3333-333333333333"),
                    TenantId = DefaultTenantId,
                    OrderCode = "ORD-20260910-003",
                    BranchId = branchId,
                    SubTotal = 385000000m, // 385 triệu doanh thu
                    DiscountAmount = 0m,
                    TotalAmount = 385000000m,
                    PaymentMethod = "BANK_TRANSFER",
                    PaymentStatus = "PAID",
                    CreatedAt = new DateTime(2026, 9, 10, 9, 0, 0, DateTimeKind.Utc)
                };

                order3.Items.Add(new OrderItem
                {
                    Id = Guid.NewGuid(),
                    OrderId = order3.Id,
                    ProductId = Guid.Parse("bbbbbbbb-1111-1111-1111-111111111111"), // Bia Tiger
                    ProductName = "Bia Tiger lon thùng 24 lon 330ml",
                    UnitPrice = 385000m,
                    Quantity = 1000m,
                    TotalLineAmount = 385000000m,
                    AppliedVatRate = 0.010m,
                    VatAmount = 3850000m,
                    VatDiscountAmount = 0m
                });

                await context.Orders.AddRangeAsync(new[] { order1, order2, order3 }, ct);
                await context.SaveChangesAsync(ct);
            }

            // 5. Seed Expense Vouchers (Phiếu chi chi phí mặt bằng, điện nước cho sổ chi phí S2c, S2e)
            if (!await context.ExpenseVouchers.IgnoreQueryFilters().AnyAsync(e => e.VoucherCode == "PC-202607-001", ct))
            {
                logger?.LogInformation("Seeding Expense Vouchers...");
                var expenses = new List<ExpenseVoucher>
                {
                    new ExpenseVoucher
                    {
                        Id = Guid.NewGuid(),
                        TenantId = DefaultTenantId,
                        VoucherCode = "PC-202607-001",
                        Category = "Tiền thuê mặt bằng kinh doanh T7/2026",
                        Note = "Thanh toán tiền thuê mặt bằng tạp hóa tháng 7/2026",
                        Amount = 15000000m,
                        BeneficiaryName = "Nguyễn Văn Chủ Nhà",
                        BranchId = branchId,
                        Status = ExpenseVoucherStatus.Approved,
                        IsTaxDeductible = true,
                        ExpenseDate = new DateTime(2026, 7, 5, 8, 0, 0, DateTimeKind.Utc),
                        CreatedAt = new DateTime(2026, 7, 5, 8, 0, 0, DateTimeKind.Utc)
                    },
                    new ExpenseVoucher
                    {
                        Id = Guid.NewGuid(),
                        TenantId = DefaultTenantId,
                        VoucherCode = "PC-202608-001",
                        Category = "Tiền thuê mặt bằng kinh doanh T8/2026",
                        Note = "Thanh toán tiền thuê mặt bằng tạp hóa tháng 8/2026",
                        Amount = 15000000m,
                        BeneficiaryName = "Nguyễn Văn Chủ Nhà",
                        BranchId = branchId,
                        Status = ExpenseVoucherStatus.Approved,
                        IsTaxDeductible = true,
                        ExpenseDate = new DateTime(2026, 8, 5, 8, 0, 0, DateTimeKind.Utc),
                        CreatedAt = new DateTime(2026, 8, 5, 8, 0, 0, DateTimeKind.Utc)
                    },
                    new ExpenseVoucher
                    {
                        Id = Guid.NewGuid(),
                        TenantId = DefaultTenantId,
                        VoucherCode = "PC-202609-001",
                        Category = "Tiền thuê mặt bằng kinh doanh T9/2026",
                        Note = "Thanh toán tiền thuê mặt bằng tạp hóa tháng 9/2026",
                        Amount = 15000000m,
                        BeneficiaryName = "Nguyễn Văn Chủ Nhà",
                        BranchId = branchId,
                        Status = ExpenseVoucherStatus.Approved,
                        IsTaxDeductible = true,
                        ExpenseDate = new DateTime(2026, 9, 5, 8, 0, 0, DateTimeKind.Utc),
                        CreatedAt = new DateTime(2026, 9, 5, 8, 0, 0, DateTimeKind.Utc)
                    },
                    new ExpenseVoucher
                    {
                        Id = Guid.NewGuid(),
                        TenantId = DefaultTenantId,
                        VoucherCode = "PC-202609-002",
                        Category = "Tiền điện, nước, Internet Q3/2026",
                        Note = "Hóa đơn EVN & VNPT chi nhánh",
                        Amount = 6500000m,
                        BeneficiaryName = "Điện lực & Viễn thông",
                        BranchId = branchId,
                        Status = ExpenseVoucherStatus.Approved,
                        IsTaxDeductible = true,
                        ExpenseDate = new DateTime(2026, 9, 25, 14, 0, 0, DateTimeKind.Utc),
                        CreatedAt = new DateTime(2026, 9, 25, 14, 0, 0, DateTimeKind.Utc)
                    }
                };

                await context.ExpenseVouchers.AddRangeAsync(expenses, ct);
                await context.SaveChangesAsync(ct);
            }
            // 6. Seed Promotions and Vouchers
            await SeedPromotionsAsync(context, DefaultTenantId, logger, ct);
            if (DefaultTenantId != "default-tenant")
            {
                await SeedPromotionsAsync(context, "default-tenant", logger, ct);
            }


            // 6. Seed Default FinanceAccounts (Till, Safe, Bank) for Tenant if not present
            if (!await context.FinanceAccounts.IgnoreQueryFilters().AnyAsync(t => t.TenantId == DefaultTenantId, ct))
            {
                logger?.LogInformation("Seeding FinanceAccounts...");
                var till = new FinanceAccount
                {
                    Id = Guid.Parse("01a19664-6210-7b12-a9eb-6fa16ca5b9db"),
                    TenantId = DefaultTenantId,
                    BranchId = branchId,
                    AccountType = FinanceAccountType.Till,
                    AccountName = "Két tiền mặt quầy chính",
                    CurrentBalance = 12500000m,
                    IsActive = true
                };
                var safe = new FinanceAccount
                {
                    Id = Guid.Parse("01a19664-6492-7d10-9a82-54801db4f342"),
                    TenantId = DefaultTenantId,
                    BranchId = branchId,
                    AccountType = FinanceAccountType.Safe,
                    AccountName = "Két sắt chủ shop (Safe Deposit)",
                    CurrentBalance = 150000000m,
                    IsActive = true
                };
                var bank1 = new FinanceAccount
                {
                    Id = Guid.Parse("01a19664-6741-7f3d-9be7-cb4c7b876559"),
                    TenantId = DefaultTenantId,
                    AccountType = FinanceAccountType.Bank,
                    AccountName = "Vietcombank (VCB)",
                    BankName = "Vietcombank (VCB)",
                    AccountNumber = "0071001234567",
                    AccountHolderName = "CHỦ HỘ KINH DOANH",
                    CurrentBalance = 145800000m,
                    IsActive = true
                };
                var bank2 = new FinanceAccount
                {
                    Id = Guid.Parse("01a19664-6761-78c3-b60a-cc460c68c51c"),
                    TenantId = DefaultTenantId,
                    AccountType = FinanceAccountType.Bank,
                    AccountName = "Techcombank (TCB)",
                    BankName = "Techcombank (TCB)",
                    AccountNumber = "19036789123011",
                    AccountHolderName = "CHỦ HỘ KINH DOANH",
                    CurrentBalance = 32450000m,
                    IsActive = true
                };
                await context.FinanceAccounts.AddRangeAsync(new[] { till, safe, bank1, bank2 }, ct);
                await context.SaveChangesAsync(ct);
            }

            // 7. Seed Customers, CustomerDebtProfiles & CustomerDebtInvoices (Công nợ FIFO & Phân loại nợ)
            var customer1Id = Guid.Parse("11111111-aaaa-aaaa-aaaa-111111111111");
            var customer2Id = Guid.Parse("22222222-aaaa-aaaa-aaaa-222222222222");
            var customer3Id = Guid.Parse("33333333-aaaa-aaaa-aaaa-333333333333");
            var customer4Id = Guid.Parse("44444444-aaaa-aaaa-aaaa-444444444444");

            if (!await context.Customers.IgnoreQueryFilters().AnyAsync(c => c.Id == customer1Id, ct))
            {
                logger?.LogInformation("Seeding Customers & Debt Profiles...");
                var customers = new List<MT_GRMS.Domain.Entities.Sales.Customer>
                {
                    new MT_GRMS.Domain.Entities.Sales.Customer
                    {
                        Id = customer1Id,
                        TenantId = DefaultTenantId,
                        CustomerCode = "KH001",
                        Name = "Nguyễn Văn An (Quán Ăn Gia Đình)",
                        Phone = "0909111222",
                        Email = "an.nguyen@example.com",
                        Address = "12 Nguyễn Huệ, Phường Bến Nghé, Quận 1, TP.HCM",
                        MembershipTier = "Gold",
                        RewardPoints = 1250,
                        TotalSpending = 45000000m,
                        TotalDebt = 8000000m,
                        DebtLimit = 20000000m,
                        AllowDebt = true,
                        IsBlockedForDebt = false
                    },
                    new MT_GRMS.Domain.Entities.Sales.Customer
                    {
                        Id = customer2Id,
                        TenantId = DefaultTenantId,
                        CustomerCode = "KH002",
                        Name = "Trần Thị Bích (Tạp Hóa Đầu Hẻm)",
                        Phone = "0909222333",
                        Email = "bich.tran@example.com",
                        Address = "45/3 Hai Bà Trưng, Phường Đa Kao, Quận 1, TP.HCM",
                        MembershipTier = "Silver",
                        RewardPoints = 540,
                        TotalSpending = 18000000m,
                        TotalDebt = 12000000m,
                        DebtLimit = 10000000m,
                        AllowDebt = true,
                        IsBlockedForDebt = true
                    },
                    new MT_GRMS.Domain.Entities.Sales.Customer
                    {
                        Id = customer3Id,
                        TenantId = DefaultTenantId,
                        CustomerCode = "KH003",
                        Name = "Lê Hoàng Long (Tiệm Cà Phê Long)",
                        Phone = "0909333444",
                        Email = "long.le@example.com",
                        Address = "88 Lê Lợi, Phường Bến Thành, Quận 1, TP.HCM",
                        MembershipTier = "Platinum",
                        RewardPoints = 3200,
                        TotalSpending = 98000000m,
                        TotalDebt = 0m,
                        DebtLimit = 15000000m,
                        AllowDebt = true,
                        IsBlockedForDebt = false
                    },
                    new MT_GRMS.Domain.Entities.Sales.Customer
                    {
                        Id = customer4Id,
                        TenantId = DefaultTenantId,
                        CustomerCode = "KH004",
                        Name = "Phạm Minh Đức (Khách Lẻ Tiềm Năng)",
                        Phone = "0909444555",
                        Email = "duc.pham@example.com",
                        Address = "102 Pasteur, Phường Bến Nghé, Quận 1, TP.HCM",
                        MembershipTier = "Standard",
                        RewardPoints = 50,
                        TotalSpending = 520000m,
                        TotalDebt = 0m,
                        DebtLimit = 0m,
                        AllowDebt = false,
                        IsBlockedForDebt = false
                    }
                };

                foreach (var cust in customers)
                {
                    if (!await context.Customers.IgnoreQueryFilters().AnyAsync(c => c.TenantId == DefaultTenantId && (c.Id == cust.Id || c.CustomerCode == cust.CustomerCode || c.Phone == cust.Phone), ct))
                    {
                        await context.Customers.AddAsync(cust, ct);
                    }
                }
                await context.SaveChangesAsync(ct);

                var debtInvoices = new List<CustomerDebtInvoice>
                {
                    new CustomerDebtInvoice
                    {
                        Id = Guid.NewGuid(),
                        TenantId = DefaultTenantId,
                        CustomerId = customer1Id,
                        OrderId = Guid.NewGuid(),
                        OrderCode = "HD-20261001-001",
                        BranchId = branchId,
                        TotalDebtAmount = 5000000m,
                        RemainingDebtAmount = 5000000m,
                        DueDate = new DateTime(2026, 10, 31, 23, 59, 59, DateTimeKind.Utc),
                        Status = DebtInvoiceStatus.Unpaid,
                        Notes = "Đơn hàng bán nợ sỉ ngày 01/10/2026 (FIFO 1)"
                    },
                    new CustomerDebtInvoice
                    {
                        Id = Guid.NewGuid(),
                        TenantId = DefaultTenantId,
                        CustomerId = customer1Id,
                        OrderId = Guid.NewGuid(),
                        OrderCode = "HD-20261015-002",
                        BranchId = branchId,
                        TotalDebtAmount = 3000000m,
                        RemainingDebtAmount = 3000000m,
                        DueDate = new DateTime(2026, 11, 15, 23, 59, 59, DateTimeKind.Utc),
                        Status = DebtInvoiceStatus.Unpaid,
                        Notes = "Đơn hàng bán nợ sỉ ngày 15/10/2026 (FIFO 2)"
                    },
                    new CustomerDebtInvoice
                    {
                        Id = Guid.NewGuid(),
                        TenantId = DefaultTenantId,
                        CustomerId = customer2Id,
                        OrderId = Guid.NewGuid(),
                        OrderCode = "HD-20260901-003",
                        BranchId = branchId,
                        TotalDebtAmount = 12000000m,
                        RemainingDebtAmount = 12000000m,
                        DueDate = new DateTime(2026, 9, 30, 23, 59, 59, DateTimeKind.Utc),
                        Status = DebtInvoiceStatus.Unpaid,
                        Notes = "Đơn hàng quá hạn nợ"
                    }
                };

                await context.CustomerDebtInvoices.AddRangeAsync(debtInvoices, ct);
                await context.SaveChangesAsync(ct);
            }

            // 8. Seed Additional Products & Supplier Linkages for Order Matrix
            if (!await context.SupplierProducts.IgnoreQueryFilters().AnyAsync(sp => sp.SupplierProductCode == "SP-MI-HAOHAO-TC", ct))
            {
                logger?.LogInformation("Seeding 20 Additional Grocery Products with Suppliers and Stocks...");

                // Lấy hoặc tạo các Nhà Cung Cấp
                var supAcecook = await context.Suppliers.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Code == "SUP-ACECOOK", ct);
                if (supAcecook == null)
                {
                    supAcecook = new Domain.Entities.Supplier.Supplier
                    {
                        TenantId = DefaultTenantId,
                        Code = "SUP-ACECOOK",
                        Name = "Công Ty Cổ Phần Acecook Việt Nam",
                        SupplierType = SupplierType.Goods,
                        Status = SupplierStatus.Active,
                        TaxCode = "0300808687",
                        Phone = "02838154064",
                        Email = "sales@acecookvietnam.vn",
                        Address = "Lô II-3, Đường số 11, KCN Tân Bình, Tây Thạnh, Tân Phú, TP.HCM",
                        ContactPerson = "Nguyễn Văn Hậu",
                        PaymentTermDays = 15,
                        CreditLimit = 150000000m
                    };
                    await context.Suppliers.AddAsync(supAcecook, ct);
                }

                var supMasan = await context.Suppliers.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Code == "SUP-MASAN", ct);
                if (supMasan == null)
                {
                    supMasan = new Domain.Entities.Supplier.Supplier
                    {
                        TenantId = DefaultTenantId,
                        Code = "SUP-MASAN",
                        Name = "Công Ty Cổ Phần Hàng Tiêu Dùng Masan (Masan Consumer)",
                        SupplierType = SupplierType.Goods,
                        Status = SupplierStatus.Active,
                        TaxCode = "0302017440",
                        Phone = "02862555660",
                        Email = "contact@masangroup.com",
                        Address = "Tầng 12, Tòa nhà MPlaza Saigon, 39 Lê Duẩn, Bến Nghé, Quận 1, TP.HCM",
                        ContactPerson = "Trần Thị Mai",
                        PaymentTermDays = 30,
                        CreditLimit = 250000000m
                    };
                    await context.Suppliers.AddAsync(supMasan, ct);
                }

                var supOrion = await context.Suppliers.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Code == "SUP-ORION", ct);
                if (supOrion == null)
                {
                    supOrion = new Domain.Entities.Supplier.Supplier
                    {
                        TenantId = DefaultTenantId,
                        Code = "SUP-ORION",
                        Name = "Công Ty TNHH Thực Phẩm Orion Vina",
                        SupplierType = SupplierType.Goods,
                        Status = SupplierStatus.Active,
                        TaxCode = "3700685000",
                        Phone = "02743787999",
                        Email = "orionvina@orionworld.com",
                        Address = "KCN Mỹ Phước II, Bến Cát, Bình Dương",
                        ContactPerson = "Lê Hoàng Long",
                        PaymentTermDays = 20,
                        CreditLimit = 180000000m
                    };
                    await context.Suppliers.AddAsync(supOrion, ct);
                }

                var supUnilever = await context.Suppliers.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Code == "SUP-UNILEVER", ct);
                if (supUnilever == null)
                {
                    supUnilever = new Domain.Entities.Supplier.Supplier
                    {
                        TenantId = DefaultTenantId,
                        Code = "SUP-UNILEVER",
                        Name = "Công Ty TNHH Quốc Tế Unilever Việt Nam",
                        SupplierType = SupplierType.Goods,
                        Status = SupplierStatus.Active,
                        TaxCode = "0303123456",
                        Phone = "02854135686",
                        Email = "cskh@unilever.com",
                        Address = "Lô A2-3 KCN Tây Bắc Củ Chi, TP.HCM",
                        ContactPerson = "Phạm Thu Hương",
                        PaymentTermDays = 30,
                        CreditLimit = 300000000m
                    };
                    await context.Suppliers.AddAsync(supUnilever, ct);
                }

                await context.SaveChangesAsync(ct);

                // Cấu hình Delivery Config cho các NCC (lưu trực tiếp trên Supplier)
                var allSups = new[] { supAcecook, supMasan, supOrion, supUnilever };
                foreach (var sup in allSups)
                {
                    sup.CutoffTime = "17:00";
                    sup.DeliveryWeekdaysMask = 127;
                    sup.DeliveryDaysJson = "[\"Mon\",\"Tue\",\"Wed\",\"Thu\",\"Fri\",\"Sat\"]";
                }

                // Lấy các Chi nhánh
                var branches = await context.Branches.IgnoreQueryFilters().ToListAsync(ct);
                foreach (var sup in allSups)
                {
                    foreach (var br in branches)
                    {
                        if (!await context.SupplierBranchAssignments.IgnoreQueryFilters().AnyAsync(sba => sba.SupplierId == sup.Id && sba.BranchId == br.Id, ct))
                        {
                            await context.SupplierBranchAssignments.AddAsync(new SupplierBranchAssignment
                            {
                                TenantId = DefaultTenantId,
                                SupplierId = sup.Id,
                                BranchId = br.Id,
                                Status = SupplierStatus.Active
                            }, ct);
                        }
                    }
                }

                var fmcgCat = await context.Categories.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.CategoryCode == "FMCG", ct);
                var fmcgCatId = fmcgCat?.Id ?? Guid.Parse("11111111-1111-1111-1111-111111111111");


                var alcCat = await context.Categories.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.CategoryCode == "ALCOHOL", ct);
                var alcCatId = alcCat?.Id ?? Guid.Parse("22222222-2222-2222-2222-222222222222");

                var extraProducts = new List<(Product prod, Guid supplierId, decimal wholesalePrice, int moq)>
                {
                    (
                        new Product
                        {
                            TenantId = DefaultTenantId,
                            CategoryId = fmcgCatId,
                            ProductCode = "SP-MI-HAOHAO-TC",
                            Barcode = "8934563020021",
                            Name = "Mì Hảo Hảo Tôm Chua Cay (Thùng 30 gói)",
                            BaseUnit = "Thùng",
                            CostPrice = 110000m,
                            SellingPrice = 135000m,
                            MinStock = 10m,
                            MaxStock = 200m,
                            Status = ProductStatus.Active
                        },
                        supAcecook.Id, 108000m, 5
                    ),
                    (
                        new Product
                        {
                            TenantId = DefaultTenantId,
                            CategoryId = fmcgCatId,
                            ProductCode = "SP-MI-OMACHI-BBQ",
                            Barcode = "8935049501234",
                            Name = "Mì Omachi Xốt Bò Hầm (Thùng 30 gói)",
                            BaseUnit = "Thùng",
                            CostPrice = 195000m,
                            SellingPrice = 235000m,
                            MinStock = 5m,
                            MaxStock = 150m,
                            Status = ProductStatus.Active
                        },
                        supMasan.Id, 192000m, 3
                    ),
                    (
                        new Product
                        {
                            TenantId = DefaultTenantId,
                            CategoryId = fmcgCatId,
                            ProductCode = "SP-ORION-CHOCOPIE",
                            Barcode = "8936036010015",
                            Name = "Bánh Chocopie Orion Hộp 12 cái (360g)",
                            BaseUnit = "Hộp",
                            CostPrice = 48000m,
                            SellingPrice = 62000m,
                            MinStock = 15m,
                            MaxStock = 300m,
                            Status = ProductStatus.Active
                        },
                        supOrion.Id, 46000m, 10
                    ),
                    (
                        new Product
                        {
                            TenantId = DefaultTenantId,
                            CategoryId = fmcgCatId,
                            ProductCode = "SP-ORION-CUSTAS",
                            Barcode = "8936036010022",
                            Name = "Bánh Trứng Custas Orion Hộp 12 cái (282g)",
                            BaseUnit = "Hộp",
                            CostPrice = 52000m,
                            SellingPrice = 68000m,
                            MinStock = 12m,
                            MaxStock = 250m,
                            Status = ProductStatus.Active
                        },
                        supOrion.Id, 50000m, 10
                    ),
                    (
                        new Product
                        {
                            TenantId = DefaultTenantId,
                            CategoryId = fmcgCatId,
                            ProductCode = "SP-NUOC-COCA-320",
                            Barcode = "8935049500038",
                            Name = "Nước Ngọt Coca-Cola Sleek Lon 320ml (Lốc 6)",
                            BaseUnit = "Lốc",
                            CostPrice = 45000m,
                            SellingPrice = 56000m,
                            MinStock = 15m,
                            MaxStock = 300m,
                            Status = ProductStatus.Active
                        },
                        supAcecook.Id, 43000m, 5
                    ),
                    (
                        new Product
                        {
                            TenantId = DefaultTenantId,
                            CategoryId = fmcgCatId,
                            ProductCode = "SP-NUOC-PEPSI-320",
                            Barcode = "8935049500045",
                            Name = "Nước Ngọt Pepsi Không Calo Lon 320ml (Lốc 6)",
                            BaseUnit = "Lốc",
                            CostPrice = 44000m,
                            SellingPrice = 55000m,
                            MinStock = 15m,
                            MaxStock = 300m,
                            Status = ProductStatus.Active
                        },
                        supMasan.Id, 42000m, 5
                    ),
                    (
                        new Product
                        {
                            TenantId = DefaultTenantId,
                            CategoryId = alcCatId,
                            ProductCode = "SP-BIA-TIGER-330",
                            Barcode = "8934822210041",
                            Name = "Bia Tiger Nâu Thùng 24 lon 330ml",
                            BaseUnit = "Thùng",
                            CostPrice = 345000m,
                            SellingPrice = 390000m,
                            MinStock = 10m,
                            MaxStock = 100m,
                            Status = ProductStatus.Active
                        },
                        supAcecook.Id, 340000m, 2
                    ),
                    (
                        new Product
                        {
                            TenantId = DefaultTenantId,
                            CategoryId = alcCatId,
                            ProductCode = "SP-BIA-HEINEKEN-BAC",
                            Barcode = "8934822210058",
                            Name = "Bia Heineken Bạc Silver Thùng 24 lon 330ml",
                            BaseUnit = "Thùng",
                            CostPrice = 398000m,
                            SellingPrice = 450000m,
                            MinStock = 10m,
                            MaxStock = 100m,
                            Status = ProductStatus.Active
                        },
                        supAcecook.Id, 395000m, 2
                    ),
                    (
                        new Product
                        {
                            TenantId = DefaultTenantId,
                            CategoryId = fmcgCatId,
                            ProductCode = "SP-SUA-TH-180",
                            Barcode = "8936036010077",
                            Name = "Sữa Tươi Tiệt Trùng TH True Milk Ít Đường 180ml (Lốc 4)",
                            BaseUnit = "Lốc",
                            CostPrice = 31000m,
                            SellingPrice = 39000m,
                            MinStock = 20m,
                            MaxStock = 400m,
                            Status = ProductStatus.Active
                        },
                        supOrion.Id, 30000m, 8
                    ),
                    (
                        new Product
                        {
                            TenantId = DefaultTenantId,
                            CategoryId = fmcgCatId,
                            ProductCode = "SP-SUA-MILO-180",
                            Barcode = "8936036010084",
                            Name = "Thức Uống Lúa Mạch Nestlé Milo 180ml (Lốc 4 hộp)",
                            BaseUnit = "Lốc",
                            CostPrice = 28000m,
                            SellingPrice = 35000m,
                            MinStock = 20m,
                            MaxStock = 400m,
                            Status = ProductStatus.Active
                        },
                        supOrion.Id, 27000m, 8
                    ),
                    (
                        new Product
                        {
                            TenantId = DefaultTenantId,
                            CategoryId = fmcgCatId,
                            ProductCode = "SP-DAUAN-NEPTUNE-1L",
                            Barcode = "8936036010091",
                            Name = "Dầu Ăn Thượng Hạng Neptune Light Chai 1L",
                            BaseUnit = "Chai",
                            CostPrice = 48000m,
                            SellingPrice = 58000m,
                            MinStock = 12m,
                            MaxStock = 200m,
                            Status = ProductStatus.Active
                        },
                        supMasan.Id, 46000m, 6
                    ),
                    (
                        new Product
                        {
                            TenantId = DefaultTenantId,
                            CategoryId = fmcgCatId,
                            ProductCode = "SP-DAUAN-SIMPLY-1L",
                            Barcode = "8936036010107",
                            Name = "Dầu Đậu Nành Tinh Khiết Simply Chai 1L",
                            BaseUnit = "Chai",
                            CostPrice = 53000m,
                            SellingPrice = 65000m,
                            MinStock = 12m,
                            MaxStock = 200m,
                            Status = ProductStatus.Active
                        },
                        supMasan.Id, 51000m, 6
                    ),
                    (
                        new Product
                        {
                            TenantId = DefaultTenantId,
                            CategoryId = fmcgCatId,
                            ProductCode = "SP-NUOCMAM-CHINSU-500",
                            Barcode = "8936036010114",
                            Name = "Nước Mắm Chinsu Cá Hồi Chai 500ml",
                            BaseUnit = "Chai",
                            CostPrice = 39000m,
                            SellingPrice = 48000m,
                            MinStock = 15m,
                            MaxStock = 250m,
                            Status = ProductStatus.Active
                        },
                        supMasan.Id, 37500m, 6
                    ),
                    (
                        new Product
                        {
                            TenantId = DefaultTenantId,
                            CategoryId = fmcgCatId,
                            ProductCode = "SP-HATNEM-KNORR-900",
                            Barcode = "8936036010121",
                            Name = "Hạt Nêm Knorr Thịt Thăn Xương Ống Gói 900g",
                            BaseUnit = "Gói",
                            CostPrice = 64000m,
                            SellingPrice = 79000m,
                            MinStock = 10m,
                            MaxStock = 200m,
                            Status = ProductStatus.Active
                        },
                        supUnilever.Id, 62000m, 5
                    ),
                    (
                        new Product
                        {
                            TenantId = DefaultTenantId,
                            CategoryId = fmcgCatId,
                            ProductCode = "SP-BOTGIAT-OMO-3KG",
                            Barcode = "8936036010138",
                            Name = "Nước Giặt OMO Matic Cửa Trên Túi 3.1kg",
                            BaseUnit = "Túi",
                            CostPrice = 162000m,
                            SellingPrice = 198000m,
                            MinStock = 8m,
                            MaxStock = 100m,
                            Status = ProductStatus.Active
                        },
                        supUnilever.Id, 158000m, 3
                    ),
                    (
                        new Product
                        {
                            TenantId = DefaultTenantId,
                            CategoryId = fmcgCatId,
                            ProductCode = "SP-NUOCXAVAI-COMFORT-3L",
                            Barcode = "8936036010145",
                            Name = "Nước Xả Vải Comfort Hương Ban Mai Túi 3.2L",
                            BaseUnit = "Túi",
                            CostPrice = 175000m,
                            SellingPrice = 215000m,
                            MinStock = 8m,
                            MaxStock = 100m,
                            Status = ProductStatus.Active
                        },
                        supUnilever.Id, 170000m, 3
                    ),
                    (
                        new Product
                        {
                            TenantId = DefaultTenantId,
                            CategoryId = fmcgCatId,
                            ProductCode = "SP-NUOCRUACHEN-SUNLIGHT-750",
                            Barcode = "8936036010152",
                            Name = "Nước Rửa Chén Sunlight Chanh Chai 750g",
                            BaseUnit = "Chai",
                            CostPrice = 26000m,
                            SellingPrice = 33000m,
                            MinStock = 20m,
                            MaxStock = 300m,
                            Status = ProductStatus.Active
                        },
                        supUnilever.Id, 25000m, 10
                    ),
                    (
                        new Product
                        {
                            TenantId = DefaultTenantId,
                            CategoryId = fmcgCatId,
                            ProductCode = "SP-KEMDANHRANG-PS-200",
                            Barcode = "8936036010169",
                            Name = "Kem Đánh Răng P/S Trà Xanh Ống 180g",
                            BaseUnit = "Hộp",
                            CostPrice = 32000m,
                            SellingPrice = 42000m,
                            MinStock = 20m,
                            MaxStock = 300m,
                            Status = ProductStatus.Active
                        },
                        supUnilever.Id, 31000m, 12
                    ),
                    (
                        new Product
                        {
                            TenantId = DefaultTenantId,
                            CategoryId = fmcgCatId,
                            ProductCode = "SP-SNACK-LAY-NATURAL",
                            Barcode = "8936036010176",
                            Name = "Snack Khoai Tây Lay's Tự Nhiên Classic Gói 95g",
                            BaseUnit = "Gói",
                            CostPrice = 16500m,
                            SellingPrice = 22000m,
                            MinStock = 30m,
                            MaxStock = 500m,
                            Status = ProductStatus.Active
                        },
                        supOrion.Id, 16000m, 15
                    ),
                    (
                        new Product
                        {
                            TenantId = DefaultTenantId,
                            CategoryId = fmcgCatId,
                            ProductCode = "SP-XUCXICH-CP-500",
                            Barcode = "8936036010183",
                            Name = "Xúc Xích Tiệt Trùng Heo C.P Gói 500g (10 cây)",
                            BaseUnit = "Gói",
                            CostPrice = 42000m,
                            SellingPrice = 54000m,
                            MinStock = 15m,
                            MaxStock = 200m,
                            Status = ProductStatus.Active
                        },
                        supAcecook.Id, 40000m, 10
                    )
                };

                foreach (var item in extraProducts)
                {
                    var existingProd = await context.Products.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.ProductCode == item.prod.ProductCode || (!string.IsNullOrEmpty(item.prod.Barcode) && p.Barcode == item.prod.Barcode), ct);
                    var targetProd = existingProd ?? item.prod;
                    if (existingProd == null)
                    {
                        await context.Products.AddAsync(item.prod, ct);
                    }

                    if (!await context.SupplierProducts.IgnoreQueryFilters().AnyAsync(sp => sp.ProductId == targetProd.Id && sp.SupplierId == item.supplierId, ct))
                    {
                        await context.SupplierProducts.AddAsync(new SupplierProduct
                        {
                            TenantId = DefaultTenantId,
                            ProductId = targetProd.Id,
                            SupplierId = item.supplierId,
                            SupplierProductCode = targetProd.ProductCode,
                            AgreedWholesalePrice = item.wholesalePrice,
                            DeliveryUoM = targetProd.BaseUnit,
                            MinimumOrderQuantity = item.moq,
                            IsPrimarySupplier = true,
                            Status = SupplierProductStatus.Active
                        }, ct);
                    }

                    foreach (var br in branches)
                    {
                        if (!await context.BranchStocks.IgnoreQueryFilters().AnyAsync(bs => bs.BranchId == br.Id && bs.ProductId == targetProd.Id, ct))
                        {
                            await context.BranchStocks.AddAsync(new BranchStock
                            {
                                TenantId = DefaultTenantId,
                                BranchId = br.Id,
                                ProductId = targetProd.Id,
                                SupplierId = item.supplierId,
                                BatchCode = $"LOT-202610-{targetProd.ProductCode}",
                                QuantityOnHand = targetProd.MinStock * 2,
                                QuantityReserved = 0,
                                LandedCostPerUnit = item.wholesalePrice,
                                ShelfLocation = "KHO-CHÍNH"
                            }, ct);
                        }
                    }
                }

                await context.SaveChangesAsync(ct);
            }

            // Đồng bộ TenantId của các sản phẩm mẫu thuộc DefaultTenantId bị lệch (không ghi đè dữ liệu của tenant_an_nhien_02)
            var misalignedProds = await context.Products.IgnoreQueryFilters().Where(p => p.TenantId != DefaultTenantId && p.TenantId != "tenant_an_nhien_02").ToListAsync(ct);
            foreach (var p in misalignedProds) { p.TenantId = DefaultTenantId; }

            var misalignedStocks = await context.BranchStocks.IgnoreQueryFilters().Where(s => s.TenantId != DefaultTenantId && s.TenantId != "tenant_an_nhien_02").ToListAsync(ct);
            foreach (var s in misalignedStocks) { s.TenantId = DefaultTenantId; }

            var misalignedSups = await context.Suppliers.IgnoreQueryFilters().Where(s => s.TenantId != DefaultTenantId && s.TenantId != "tenant_an_nhien_02").ToListAsync(ct);
            foreach (var s in misalignedSups) { s.TenantId = DefaultTenantId; }

            var misalignedSp = await context.SupplierProducts.IgnoreQueryFilters().Where(s => s.TenantId != DefaultTenantId && s.TenantId != "tenant_an_nhien_02").ToListAsync(ct);
            foreach (var s in misalignedSp) { s.TenantId = DefaultTenantId; }

            var misalignedSba = await context.SupplierBranchAssignments.IgnoreQueryFilters().Where(s => s.TenantId != DefaultTenantId && s.TenantId != "tenant_an_nhien_02").ToListAsync(ct);
            foreach (var s in misalignedSba) { s.TenantId = DefaultTenantId; }

            await context.SaveChangesAsync(ct);

            logger?.LogInformation("DatabaseSeeder finished successfully.");
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "An error occurred during DatabaseSeeder execution.");
            throw;
        }
    }
    private static async Task SeedPromotionsAsync(AppDbContext context, string tenantId, ILogger? logger = null, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        if (!await context.Promotions.IgnoreQueryFilters().AnyAsync(p => p.TenantId == tenantId && p.PromotionCode == "KM-GIAM10-TOIDA50K", ct))
        {
            logger?.LogInformation("Seeding Promotions and Vouchers for Tenant {TenantId}...", tenantId);

            var promotions = new List<Promotion>
            {
                new Promotion
                {
                    TenantId = tenantId,
                    PromotionCode = "KM-GIAM10-TOIDA50K",
                    PromotionName = "Giảm 10% cho đơn từ 200.000đ (Tối đa 50.000đ)",
                    Type = "ORDER_DISCOUNT",
                    DiscountValue = 10.00m,
                    MinOrderValue = 200000.00m,
                    MaxDiscountAmount = 50000.00m,
                    UsageLimit = 500,
                    UsedCount = 15,
                    BudgetLimit = 10000000.00m,
                    TotalDiscountGiven = 750000.00m,
                    StartDate = now.AddDays(-30),
                    EndDate = now.AddDays(180),
                    IsActive = true,
                    Description = "Chương trình tri ân khách hàng: Giảm 10% cho hóa đơn từ 200.000đ, trần chiết khấu 50.000đ.",
                    TargetBranches = "ALL"
                },
                new Promotion
                {
                    TenantId = tenantId,
                    PromotionCode = "KM-GIAM30K-DON150K",
                    PromotionName = "Giảm thẳng 30.000đ cho đơn từ 150.000đ",
                    Type = "FIXED_AMOUNT",
                    DiscountValue = 30000.00m,
                    MinOrderValue = 150000.00m,
                    MaxDiscountAmount = null,
                    UsageLimit = 200,
                    UsedCount = 28,
                    BudgetLimit = 6000000.00m,
                    TotalDiscountGiven = 840000.00m,
                    StartDate = now.AddDays(-15),
                    EndDate = now.AddDays(90),
                    IsActive = true,
                    Description = "Ưu đãi trực tiếp: Trừ ngay 30.000 VNĐ cho hóa đơn mua sắm từ 150.000 VNĐ.",
                    TargetBranches = "ALL"
                }
            };

            // Promotion 3: Mua 3 Chocopie tặng 1 Coca
            var p3 = new Promotion
            {
                TenantId = tenantId,
                PromotionCode = "KM-MUA3TANG1-COCA",
                PromotionName = "Mua 3 Bánh Chocopie tặng 1 Coca-Cola lon",
                Type = "BUY_X_GET_Y",
                DiscountValue = 0.00m,
                MinOrderValue = 0.00m,
                UsageLimit = 100,
                UsedCount = 8,
                StartDate = now.AddDays(-10),
                EndDate = now.AddDays(60),
                IsActive = true,
                Description = "Ưu đãi giải khát: Khi mua đủ 3 hộp Bánh Chocopie Orion, tặng ngay 1 lốc Coca-Cola lon 320ml.",
                TargetBranches = "ALL"
            };
            p3.Conditions.Add(new PromotionCondition
            {
                TenantId = tenantId,
                PromotionId = p3.Id,
                ProductId = Guid.Parse("aaaaaaaa-1111-1111-1111-111111111111"),
                ProductRole = "BUY",
                MinQty = 3.00m,
                Note = "Mua tối thiểu 3 hộp Bánh Chocopie Orion"
            });
            p3.Conditions.Add(new PromotionCondition
            {
                TenantId = tenantId,
                PromotionId = p3.Id,
                ProductId = Guid.Parse("aaaaaaaa-3333-3333-3333-333333333333"),
                ProductRole = "GIFT",
                MinQty = 1.00m,
                Note = "Tặng 1 lốc nước ngọt Coca-Cola 320ml"
            });
            promotions.Add(p3);

            // Promotion 4: Voucher VIP 50k
            var p4 = new Promotion
            {
                TenantId = tenantId,
                PromotionCode = "VOUCHER-VIP50K",
                PromotionName = "Chiến dịch Voucher VIP Giảm 50.000đ",
                Type = "VOUCHER",
                DiscountValue = 50000.00m,
                MinOrderValue = 250000.00m,
                MaxDiscountAmount = null,
                UsageLimit = 10,
                UsedCount = 1,
                BudgetLimit = 500000.00m,
                TotalDiscountGiven = 50000.00m,
                StartDate = now.AddDays(-20),
                EndDate = now.AddDays(120),
                IsActive = true,
                Description = "Mã voucher tri ân giảm 50.000đ cho khách hàng VIP đạt cấp độ Vàng/Kim Cương.",
                TargetBranches = "ALL",
                VoucherPrefix = "VIP50K",
                TotalVoucherQuantity = 4
            };
            p4.VoucherCodes.Add(new VoucherCode { TenantId = tenantId, PromotionId = p4.Id, Code = "VIP50K-TEST01", Status = "UNUSED" });
            p4.VoucherCodes.Add(new VoucherCode { TenantId = tenantId, PromotionId = p4.Id, Code = "VIP50K-TEST02", Status = "UNUSED" });
            p4.VoucherCodes.Add(new VoucherCode { TenantId = tenantId, PromotionId = p4.Id, Code = "VIP50K-TEST03", Status = "UNUSED" });
            p4.VoucherCodes.Add(new VoucherCode
            {
                TenantId = tenantId,
                PromotionId = p4.Id,
                Code = "VIP50K-DA-DUNG",
                Status = "USED",
                OrderCode = "ORD-20261015-001",
                OrderId = Guid.Parse("f0000001-1111-1111-1111-111111111111"),
                UsedAt = now.AddDays(-2)
            });
            promotions.Add(p4);

            // Promotion 5: Voucher Cuối Tuần 20%
            var p5 = new Promotion
            {
                TenantId = tenantId,
                PromotionCode = "VOUCHER-CUOITUAN20",
                PromotionName = "Voucher Giảm 20% Mua Sắm Cuối Tuần (Tối đa 40.000đ)",
                Type = "VOUCHER",
                DiscountValue = 20.00m,
                MinOrderValue = 100000.00m,
                MaxDiscountAmount = 40000.00m,
                UsageLimit = 20,
                UsedCount = 0,
                StartDate = now.AddDays(-5),
                EndDate = now.AddDays(90),
                IsActive = true,
                Description = "Mã voucher giảm 20% áp dụng cho khách hàng thanh toán tại quầy POS vào cuối tuần.",
                TargetBranches = "ALL",
                VoucherPrefix = "WEEKEND",
                TotalVoucherQuantity = 3
            };
            p5.VoucherCodes.Add(new VoucherCode { TenantId = tenantId, PromotionId = p5.Id, Code = "WEEKEND-20PCT01", Status = "UNUSED" });
            p5.VoucherCodes.Add(new VoucherCode { TenantId = tenantId, PromotionId = p5.Id, Code = "WEEKEND-20PCT02", Status = "UNUSED" });
            p5.VoucherCodes.Add(new VoucherCode { TenantId = tenantId, PromotionId = p5.Id, Code = "WEEKEND-20PCT03", Status = "UNUSED" });
            promotions.Add(p5);

            // Promotion 6: Hết hạn
            var p6 = new Promotion
            {
                TenantId = tenantId,
                PromotionCode = "KM-TET2025-HETHAN",
                PromotionName = "Khuyến mãi Tết 2025 (Đã hết hạn)",
                Type = "ORDER_DISCOUNT",
                DiscountValue = 15.00m,
                MinOrderValue = 100000.00m,
                MaxDiscountAmount = 30000.00m,
                UsageLimit = 100,
                UsedCount = 100,
                StartDate = now.AddDays(-400),
                EndDate = now.AddDays(-340),
                IsActive = false,
                Description = "Chương trình khuyến mãi mùa Tết năm cũ, đã hết hạn để test bộ lọc trạng thái.",
                TargetBranches = "ALL"
            };
            promotions.Add(p6);

            await context.Promotions.AddRangeAsync(promotions, ct);
            await context.SaveChangesAsync(ct);
        }

        // 7. Seed Dữ liệu bán lẻ POS (Hàng tạp hóa, Barcode, Tồn kho FEFO & Khách hàng công nợ)
        await SeedPosRetailDataAsync(context, logger, ct);
    }

    private static async Task SeedPosRetailDataAsync(AppDbContext context, ILogger? logger = null, CancellationToken ct = default)
    {
        // 1. Tạo hoặc lấy Branch POS chuẩn
        var branch = await context.Branches.IgnoreQueryFilters().FirstOrDefaultAsync(b => b.BranchCode == "BR-POS-01", ct);
        if (branch == null)
        {
            branch = new Domain.Entities.SaaS.Branch
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                TenantId = DefaultTenantId,
                BranchCode = "BR-POS-01",
                Name = "Siêu Thị Mini MT-Grocery (Cầu Giấy)",
                Address = "123 Xuân Thủy, Cầu Giấy, Hà Nội",
                Phone = "0987.654.321",
                IsActive = true
            };
            await context.Branches.AddAsync(branch, ct);
            await context.SaveChangesAsync(ct);
        }

        // 2. Thêm khách hàng mẫu phục vụ test bán hàng và bán nợ
        if (!await context.Customers.IgnoreQueryFilters().AnyAsync(c => c.CustomerCode == "KH-POS-001", ct))
        {
            logger?.LogInformation("Seeding POS Retail Customers...");
            var customers = new List<Domain.Entities.Sales.Customer>
            {
                new Domain.Entities.Sales.Customer
                {
                    Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                    TenantId = DefaultTenantId,
                    CustomerCode = "KH-POS-001",
                    Name = "Nguyễn Văn Khách (Thân Thiết)",
                    Phone = "0912345678",
                    Email = "khach.nguyen@gmail.com",
                    Address = "Số 15 Cầu Giấy, Hà Nội",
                    MembershipTier = "Gold",
                    RewardPoints = 250,
                    TotalSpending = 5500000m,
                    TotalDebt = 350000m,
                    DebtLimit = 5000000m,
                    AllowDebt = true,
                    IsBlockedForDebt = false
                },
                new Domain.Entities.Sales.Customer
                {
                    Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                    TenantId = DefaultTenantId,
                    CustomerCode = "KH-POS-002",
                    Name = "Chị Lan Tạp Hóa Xóm",
                    Phone = "0987654321",
                    Address = "Ngõ 165 Cầu Giấy, Hà Nội",
                    MembershipTier = "Platinum",
                    RewardPoints = 520,
                    TotalSpending = 18000000m,
                    TotalDebt = 2100000m,
                    DebtLimit = 10000000m,
                    AllowDebt = true,
                    IsBlockedForDebt = false
                },
                new Domain.Entities.Sales.Customer
                {
                    Id = Guid.Parse("44444444-4444-4444-4444-444444444444"),
                    TenantId = DefaultTenantId,
                    CustomerCode = "KH-POS-003",
                    Name = "Bác Ba (Bị Khóa Nợ)",
                    Phone = "0905123456",
                    Address = "Phố Dịch Vọng, Cầu Giấy, Hà Nội",
                    MembershipTier = "Standard",
                    RewardPoints = 20,
                    TotalSpending = 1200000m,
                    TotalDebt = 1200000m,
                    DebtLimit = 1000000m,
                    AllowDebt = false,
                    IsBlockedForDebt = true
                }
            };
            foreach (var cust in customers)
            {
                if (!await context.Customers.IgnoreQueryFilters().AnyAsync(c => c.TenantId == DefaultTenantId && (c.Id == cust.Id || c.CustomerCode == cust.CustomerCode || c.Phone == cust.Phone), ct))
                {
                    await context.Customers.AddAsync(cust, ct);
                }
            }
            await context.SaveChangesAsync(ct);
        }

        // 3. Thêm các sản phẩm tạp hóa có Barcode chuẩn
        var catFmcg = await context.Categories.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.CategoryCode == "FMCG", ct);
        var catId = catFmcg?.Id ?? Guid.Parse("11111111-1111-1111-1111-111111111111");

        var posProducts = new List<(Guid Id, string SKU, string Barcode, string Name, decimal Price, decimal CostPrice, string Unit, decimal Vat)>
        {
            (Guid.Parse("e0000001-0000-0000-0000-000000000001"), "HH-TOM-CAY", "8934563138164", "Mì Hảo Hảo tôm chua cay 75g", 4500m, 3600m, "Gói", 0.01m),
            (Guid.Parse("e0000002-0000-0000-0000-000000000002"), "COCA-320ML", "8935049500123", "Nước ngọt Coca-Cola lon 320ml", 10000m, 7800m, "Lon", 0.01m),
            (Guid.Parse("e0000003-0000-0000-0000-000000000003"), "REDBULL-250", "8850228000153", "Nước tăng lực Redbull lon 250ml", 12000m, 9500m, "Lon", 0.01m),
            (Guid.Parse("e0000004-0000-0000-0000-000000000004"), "LAVIE-500ML", "8935001700035", "Nước khoáng Lavie chai 500ml", 6000m, 4200m, "Chai", 0.01m),
            (Guid.Parse("e0000005-0000-0000-0000-000000000005"), "VNM-100PCT", "8934673123456", "Sữa tươi tiệt trùng Vinamilk 100% 180ml", 8500m, 6800m, "Hộp", 0.01m),
            (Guid.Parse("e0000006-0000-0000-0000-000000000006"), "KEN-330ML", "8934822212345", "Bia Heineken lon 330ml", 22000m, 18500m, "Lon", 0.01m),
            (Guid.Parse("e0000007-0000-0000-0000-000000000007"), "SIMPLY-1L", "8934561230018", "Dầu ăn đậu nành Simply chai 1 lít", 55000m, 45000m, "Chai", 0.01m),
            (Guid.Parse("e0000008-0000-0000-0000-000000000008"), "GAO-ST25-5K", "8935001780099", "Gạo ST25 Ông Cua túi 5kg Thượng Hạng", 180000m, 150000m, "Túi", 0.01m),
            (Guid.Parse("e0000009-0000-0000-0000-000000000009"), "SUNLIGHT-750", "8934561234567", "Nước rửa chén Sunlight Chanh túi 750g", 30000m, 24000m, "Túi", 0.01m)
        };

        // Tự động sửa barcode cho bản ghi LAVIE-500ML cũ nếu đã tồn tại để tránh xung đột với Bánh Custas (8936036010022)
        var oldLavie = await context.Products.IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => (p.ProductCode == "LAVIE-500ML" || p.Id == Guid.Parse("e0000004-0000-0000-0000-000000000004")) && p.Barcode == "8936036010022", ct);
        if (oldLavie != null)
        {
            oldLavie.Barcode = "8935001700035";
            await context.SaveChangesAsync(ct);
        }

        foreach (var item in posProducts)
        {
            var existingProd = await context.Products.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == item.Id || p.ProductCode == item.SKU || p.Barcode == item.Barcode, ct);
            if (existingProd == null)
            {
                var newP = new Product
                {
                    Id = item.Id,
                    TenantId = DefaultTenantId,
                    ProductCode = item.SKU,
                    Barcode = item.Barcode,
                    Name = item.Name,
                    CategoryId = catId,
                    SellingPrice = item.Price,
                    CostPrice = item.CostPrice,
                    BaseUnit = item.Unit,
                    VatRate = item.Vat,
                    TaxCategory = "STANDARD_1PCT",
                    Status = ProductStatus.Active
                };
                await context.Products.AddAsync(newP, ct);
            }
        }
        await context.SaveChangesAsync(ct);

        // 4. Tạo tồn kho chi nhánh BranchStocks theo FEFO cho các sản phẩm
        var pMap = await context.Products.IgnoreQueryFilters()
            .Where(p => posProducts.Select(x => x.Id).Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);

        foreach (var item in posProducts)
        {
            if (pMap.TryGetValue(item.Id, out var prod))
            {
                var hasStock = await context.BranchStocks.IgnoreQueryFilters()
                    .AnyAsync(s => s.BranchId == branch.Id && s.ProductId == prod.Id, ct);

                if (!hasStock)
                {
                    // Lô 1: Hết hạn sớm (trong 1-2 tháng tới)
                    var stockLotEarly = new Domain.Entities.Inventory.BranchStock
                    {
                        Id = Guid.NewGuid(),
                        TenantId = DefaultTenantId,
                        BranchId = branch.Id,
                        ProductId = prod.Id,
                        BatchCode = $"LOT-{item.SKU}-EARLY",
                        ManufactureDate = DateTime.UtcNow.AddMonths(-3),
                        ExpiryDate = DateTime.UtcNow.AddMonths(2),
                        QuantityOnHand = 25m,
                        LandedCostPerUnit = item.CostPrice,
                        ShelfLocation = "Quầy 01",
                        ZoneArea = "Khu Bán Lẻ"
                    };

                    // Lô 2: Hết hạn muộn (trong 12 tháng tới)
                    var stockLotLate = new Domain.Entities.Inventory.BranchStock
                    {
                        Id = Guid.NewGuid(),
                        TenantId = DefaultTenantId,
                        BranchId = branch.Id,
                        ProductId = prod.Id,
                        BatchCode = $"LOT-{item.SKU}-LATE",
                        ManufactureDate = DateTime.UtcNow.AddMonths(-1),
                        ExpiryDate = DateTime.UtcNow.AddMonths(12),
                        QuantityOnHand = 100m,
                        LandedCostPerUnit = item.CostPrice,
                        ShelfLocation = "Kho Phụ",
                        ZoneArea = "Kho Chi Nhánh"
                    };

                    await context.BranchStocks.AddRangeAsync(new[] { stockLotEarly, stockLotLate }, ct);
                }
            }
        }
        await context.SaveChangesAsync(ct);
        logger?.LogInformation("Seeded POS Retail Products and FEFO BranchStocks successfully.");

        // 5. Nạp dữ liệu mẫu cho Phiếu nhập kho (GRN) hoàn thành và Phiếu trả hàng NCC (Supplier Returns)
        await SeedSupplierReturnsAndGrnsAsync(context, logger, ct);
    }

    private static async Task SeedSupplierReturnsAndGrnsAsync(AppDbContext context, ILogger? logger = null, CancellationToken ct = default)
    {
        var branch = await context.Branches.IgnoreQueryFilters().FirstOrDefaultAsync(b => b.TenantId == DefaultTenantId, ct);
        var supAcecook = await context.Suppliers.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Code == "SUP-ACECOOK", ct);
        var supMasan = await context.Suppliers.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Code == "SUP-MASAN", ct);

        if (branch == null || supAcecook == null || supMasan == null) return;

        var prodHaoHao = await context.Products.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.ProductCode == "SP-MI-HAOHAO-TC", ct);
        var prodOmachi = await context.Products.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.ProductCode == "SP-MI-OMACHI-BBQ", ct);
        var prodHaoHaoGoi = await context.Products.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.ProductCode == "HH-TOM-CAY", ct);

        if (prodHaoHao == null || prodOmachi == null) return;

        // 1. Tạo 2 phiếu nhập kho hoàn thành (COMPLETED) làm nguồn cho trả hàng
        var grn1 = await context.GoodsReceipts.IgnoreQueryFilters()
            .Include(g => g.Lines)
            .FirstOrDefaultAsync(g => g.Code == "GRN-20261101-001", ct);
        if (grn1 == null)
        {
            var grn1Id = Guid.NewGuid();
            var line1Id = Guid.NewGuid();
            var line2Id = Guid.NewGuid();

            grn1 = new GoodsReceipt
            {
                Id = grn1Id,
                TenantId = DefaultTenantId,
                Code = "GRN-20261101-001",
                SupplierId = supAcecook.Id,
                BranchId = branch.Id,
                Status = "COMPLETED",
                InvoiceNo = "HD-AC-88991",
                ReceiverUserId = "user-owner-001",
                ReceivedAtUtc = DateTime.UtcNow.AddDays(-10),
                SubTotal = 5760000m,
                GrandTotal = 5760000m,
                PaymentMethod = "AP",
                Note = "Nhập hàng mì gói từ Acecook đã đối soát đủ",
                Lines = new List<GoodsReceiptLine>
                {
                    new GoodsReceiptLine
                    {
                        Id = line1Id,
                        TenantId = DefaultTenantId,
                        GoodsReceiptId = grn1Id,
                        ProductId = prodHaoHao.Id,
                        OrderedQty = 50m,
                        DeliveredQty = 50m,
                        AcceptedQty = 50m,
                        RejectedQty = 0m,
                        UnitPrice = 108000m,
                        LineAmount = 5400000m,
                        BatchCode = "LOT-HAOHAO-2026A",
                        ManufactureDate = DateTime.UtcNow.AddMonths(-5),
                        ExpiryDate = DateTime.UtcNow.AddDays(25),
                        QualityStatus = "ACCEPTED"
                    },
                    new GoodsReceiptLine
                    {
                        Id = line2Id,
                        TenantId = DefaultTenantId,
                        GoodsReceiptId = grn1Id,
                        ProductId = prodHaoHaoGoi != null ? prodHaoHaoGoi.Id : prodHaoHao.Id,
                        OrderedQty = 100m,
                        DeliveredQty = 100m,
                        AcceptedQty = 100m,
                        RejectedQty = 0m,
                        UnitPrice = 3600m,
                        LineAmount = 360000m,
                        BatchCode = "LOT-HH-TOM-CAY-EARLY",
                        ManufactureDate = DateTime.UtcNow.AddMonths(-3),
                        ExpiryDate = DateTime.UtcNow.AddDays(15),
                        QualityStatus = "ACCEPTED"
                    }
                }
            };
            await context.GoodsReceipts.AddAsync(grn1, ct);
        }

        var grn2 = await context.GoodsReceipts.IgnoreQueryFilters()
            .Include(g => g.Lines)
            .FirstOrDefaultAsync(g => g.Code == "GRN-20261102-002", ct);
        if (grn2 == null)
        {
            var grn2Id = Guid.NewGuid();
            var line3Id = Guid.NewGuid();

            grn2 = new GoodsReceipt
            {
                Id = grn2Id,
                TenantId = DefaultTenantId,
                Code = "GRN-20261102-002",
                SupplierId = supMasan.Id,
                BranchId = branch.Id,
                Status = "COMPLETED",
                InvoiceNo = "HD-MS-44120",
                ReceiverUserId = "user-owner-001",
                ReceivedAtUtc = DateTime.UtcNow.AddDays(-7),
                SubTotal = 7680000m,
                GrandTotal = 7680000m,
                PaymentMethod = "AP",
                Note = "Nhập hàng thực phẩm Masan Consumer",
                Lines = new List<GoodsReceiptLine>
                {
                    new GoodsReceiptLine
                    {
                        Id = line3Id,
                        TenantId = DefaultTenantId,
                        GoodsReceiptId = grn2Id,
                        ProductId = prodOmachi.Id,
                        OrderedQty = 40m,
                        DeliveredQty = 40m,
                        AcceptedQty = 40m,
                        RejectedQty = 0m,
                        UnitPrice = 192000m,
                        LineAmount = 7680000m,
                        BatchCode = "LOT-OMACHI-BBQ-26B",
                        ManufactureDate = DateTime.UtcNow.AddMonths(-2),
                        ExpiryDate = DateTime.UtcNow.AddMonths(6),
                        QualityStatus = "ACCEPTED"
                    }
                }
            };
            await context.GoodsReceipts.AddAsync(grn2, ct);
        }

        await context.SaveChangesAsync(ct);

        // 2. Nạp các phiếu trả hàng mẫu (Supplier Returns) theo từng trạng thái
        if (!await context.SupplierReturns.IgnoreQueryFilters().AnyAsync(r => r.TenantId == DefaultTenantId, ct))
        {
            var rtv1Line = grn1?.Lines?.FirstOrDefault(l => prodHaoHao != null && l.ProductId == prodHaoHao.Id)
                ?? grn1?.Lines?.FirstOrDefault()
                ?? await context.GoodsReceiptLines.IgnoreQueryFilters().FirstOrDefaultAsync(l => grn1 != null && l.GoodsReceiptId == grn1.Id, ct);

            var rtv2Line = grn2?.Lines?.FirstOrDefault(l => prodOmachi != null && l.ProductId == prodOmachi.Id)
                ?? grn2?.Lines?.FirstOrDefault()
                ?? await context.GoodsReceiptLines.IgnoreQueryFilters().FirstOrDefaultAsync(l => grn2 != null && l.GoodsReceiptId == grn2.Id, ct);

            var rtv3Line = grn1?.Lines?.FirstOrDefault(l => l.ProductId == (prodHaoHaoGoi != null ? prodHaoHaoGoi.Id : (prodHaoHao != null ? prodHaoHao.Id : Guid.Empty)))
                ?? grn1?.Lines?.FirstOrDefault()
                ?? await context.GoodsReceiptLines.IgnoreQueryFilters().FirstOrDefaultAsync(l => grn1 != null && l.GoodsReceiptId == grn1.Id, ct);

            if (grn1 != null && grn2 != null && rtv1Line != null && rtv2Line != null && rtv3Line != null && prodHaoHao != null && prodOmachi != null)
            {
                var rtv1 = new SupplierReturn
                {
                    Id = Guid.NewGuid(),
                    TenantId = DefaultTenantId,
                    Code = "RTV-20261103-001",
                    GoodsReceiptId = grn1.Id,
                    SupplierId = supAcecook.Id,
                    BranchId = branch.Id,
                    Status = "APPROVED",
                    Reason = "Hàng sắp hết hạn (Date cận < 30 ngày)",
                    ReasonNotes = "Lô mì Hảo Hảo còn dưới 25 ngày theo thỏa thuận đổi hàng cận date của Acecook",
                    SupplierResponseNote = "Acecook đồng ý đổi trả 10 thùng theo chính sách bảo hành date",
                    TotalRequestedQty = 10m,
                    TotalApprovedQty = 10m,
                    TotalHandedOverQty = 0m,
                    TotalEstimatedAmount = 1080000m,
                    SubmittedAt = DateTime.UtcNow.AddDays(-3),
                    RespondedAt = DateTime.UtcNow.AddDays(-2),
                    Lines = new List<SupplierReturnLine>
                    {
                        new SupplierReturnLine
                        {
                            Id = Guid.NewGuid(),
                            TenantId = DefaultTenantId,
                            ProductId = prodHaoHao.Id,
                            GoodsReceiptLineId = rtv1Line.Id,
                            BatchCode = rtv1Line.BatchCode,
                            ExpiryDate = rtv1Line.ExpiryDate,
                            RequestedQty = 10m,
                            ApprovedQty = 10m,
                            RejectedQty = 0m,
                            HandedOverQty = 0m,
                            UnitCost = rtv1Line.UnitPrice,
                            Notes = "Hàng date cận còn nguyên tem"
                        }
                    }
                };

                var rtv2 = new SupplierReturn
                {
                    Id = Guid.NewGuid(),
                    TenantId = DefaultTenantId,
                    Code = "RTV-20261104-002",
                    GoodsReceiptId = grn2.Id,
                    SupplierId = supMasan.Id,
                    BranchId = branch.Id,
                    Status = "PENDING_SUPPLIER",
                    Reason = "Hàng hư hỏng / Lỗi chất lượng",
                    ReasonNotes = "5 thùng Omachi bị móp méo rách vỏ do vận chuyển",
                    TotalRequestedQty = 5m,
                    TotalApprovedQty = 0m,
                    TotalHandedOverQty = 0m,
                    TotalEstimatedAmount = 960000m,
                    SubmittedAt = DateTime.UtcNow.AddDays(-1),
                    Lines = new List<SupplierReturnLine>
                    {
                        new SupplierReturnLine
                        {
                            Id = Guid.NewGuid(),
                            TenantId = DefaultTenantId,
                            ProductId = prodOmachi.Id,
                            GoodsReceiptLineId = rtv2Line.Id,
                            BatchCode = rtv2Line.BatchCode,
                            ExpiryDate = rtv2Line.ExpiryDate,
                            RequestedQty = 5m,
                            ApprovedQty = 0m,
                            RejectedQty = 0m,
                            HandedOverQty = 0m,
                            UnitCost = rtv2Line.UnitPrice,
                            Notes = "Thùng móp góc và rách băng keo niêm phong"
                        }
                    }
                };

                var rtv3 = new SupplierReturn
                {
                    Id = Guid.NewGuid(),
                    TenantId = DefaultTenantId,
                    Code = "RTV-20261105-003",
                    GoodsReceiptId = grn1.Id,
                    SupplierId = supAcecook.Id,
                    BranchId = branch.Id,
                    Status = "COMPLETED",
                    Reason = "Hàng tồn đọng thỏa thuận thu hồi",
                    ReasonNotes = "Thu hồi gói dùng thử theo chương trình khuyến mãi kết thúc",
                    SupplierResponseNote = "Đã nhận đủ và cấn trừ vào đợt giao tiếp theo",
                    TotalRequestedQty = 15m,
                    TotalApprovedQty = 15m,
                    TotalHandedOverQty = 15m,
                    TotalEstimatedAmount = 54000m,
                    SubmittedAt = DateTime.UtcNow.AddDays(-5),
                    RespondedAt = DateTime.UtcNow.AddDays(-4),
                    HandedOverAt = DateTime.UtcNow.AddDays(-2),
                    HandedOverBy = "Nguyễn Văn Kho",
                    Lines = new List<SupplierReturnLine>
                    {
                        new SupplierReturnLine
                        {
                            Id = Guid.NewGuid(),
                            TenantId = DefaultTenantId,
                            ProductId = rtv3Line.ProductId,
                            GoodsReceiptLineId = rtv3Line.Id,
                            BatchCode = rtv3Line.BatchCode,
                            ExpiryDate = rtv3Line.ExpiryDate,
                            RequestedQty = 15m,
                            ApprovedQty = 15m,
                            RejectedQty = 0m,
                            HandedOverQty = 15m,
                            UnitCost = rtv3Line.UnitPrice,
                            Notes = "Bàn giao cho tài xế xe tải Acecook 59C-12345"
                        }
                    }
                };

                var rtv4 = new SupplierReturn
                {
                    Id = Guid.NewGuid(),
                    TenantId = DefaultTenantId,
                    Code = "RTV-20261106-004",
                    GoodsReceiptId = grn2.Id,
                    SupplierId = supMasan.Id,
                    BranchId = branch.Id,
                    Status = "DRAFT",
                    Reason = "Sai quy cách đóng gói",
                    ReasonNotes = "Kiểm đếm phát hiện tem phụ tiếng Việt bị mờ",
                    TotalRequestedQty = 2m,
                    TotalApprovedQty = 0m,
                    TotalHandedOverQty = 0m,
                    TotalEstimatedAmount = 384000m,
                    Lines = new List<SupplierReturnLine>
                    {
                        new SupplierReturnLine
                        {
                            Id = Guid.NewGuid(),
                            TenantId = DefaultTenantId,
                            ProductId = prodOmachi.Id,
                            GoodsReceiptLineId = rtv2Line.Id,
                            BatchCode = rtv2Line.BatchCode,
                            ExpiryDate = rtv2Line.ExpiryDate,
                            RequestedQty = 2m,
                            ApprovedQty = 0m,
                            RejectedQty = 0m,
                            HandedOverQty = 0m,
                            UnitCost = rtv2Line.UnitPrice,
                            Notes = "Tem phụ mờ khó quét mã QR"
                        }
                    }
                };

                await context.SupplierReturns.AddRangeAsync(new[] { rtv1, rtv2, rtv3, rtv4 }, ct);
                await context.SaveChangesAsync(ct);
                logger?.LogInformation("Seeded completed GRNs and Supplier Returns sample data successfully.");
            }
        }
    }
}
