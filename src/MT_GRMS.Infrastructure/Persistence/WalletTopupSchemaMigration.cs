using Microsoft.EntityFrameworkCore;

namespace MT_GRMS.Infrastructure.Persistence;

public sealed class WalletTopupSchemaMigrationException(Exception innerException)
    : InvalidOperationException("Không thể hợp nhất dữ liệu nạp tiền ví. Kiểm tra dữ liệu đối soát trước khi khởi động lại.", innerException);

internal static class WalletTopupSchemaMigration
{
    internal static async Task ApplyAsync(AppDbContext context)
    {
        try
        {
            var strategy = context.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await context.Database.BeginTransactionAsync();
                await context.Database.ExecuteSqlRawAsync(Sql);
                await transaction.CommitAsync();
            });
        }
        catch (Exception ex)
        {
            throw new WalletTopupSchemaMigrationException(ex);
        }
    }

    private const string Sql = """
        SELECT pg_advisory_xact_lock(164711, 1);

        ALTER TABLE "WalletTransactions"
          ADD COLUMN IF NOT EXISTS "OrderCode" varchar(50),
          ADD COLUMN IF NOT EXISTS "RequestedAmount" numeric(18,2),
          ADD COLUMN IF NOT EXISTS "TransferContent" varchar(100),
          ADD COLUMN IF NOT EXISTS "TopupDetails" text,
          ADD COLUMN IF NOT EXISTS "ExpiresAt" timestamptz,
          ADD COLUMN IF NOT EXISTS "CompletedAt" timestamptz,
          ADD COLUMN IF NOT EXISTS "SepayTransactionId" bigint,
          ADD COLUMN IF NOT EXISTS "ReconciliationOrderCode" varchar(50);

        UPDATE "WalletTransactions"
        SET "SepayTransactionId" = substring("TransactionCode" from '-([1-9][0-9]*)$')::bigint
        WHERE "Type" = 'TOPUP' AND "Status" = 'SUCCESS' AND "SepayTransactionId" IS NULL
          AND "TransactionCode" ~ '^WAL-([0-9]+-)?[1-9][0-9]*$';

        DO $wallet$
        DECLARE
          bad_order text;
        BEGIN
          IF NOT EXISTS (
            SELECT FROM pg_constraint
            WHERE conrelid = '"WalletTransactions"'::regclass
              AND conname = 'FK_WalletTransactions_Tenants_TenantId'
          ) THEN
            ALTER TABLE "WalletTransactions" ADD CONSTRAINT "FK_WalletTransactions_Tenants_TenantId"
              FOREIGN KEY ("TenantId") REFERENCES "Tenants" ("TenantID") ON DELETE RESTRICT;
          END IF;

          IF to_regclass('"WalletTopupOrders"') IS NULL THEN
            RETURN;
          END IF;

          LOCK TABLE "WalletTopupOrders" IN ACCESS EXCLUSIVE MODE;

          SELECT o."OrderCode" INTO bad_order
          FROM "WalletTopupOrders" o
          WHERE o."Status" = 'COMPLETED' AND (
            SELECT count(*) FROM "WalletTransactions" t
            WHERE t."TenantId" = o."TenantId" AND t."Type" = 'TOPUP' AND t."Status" = 'SUCCESS'
              AND t."ReferenceCode" = NULLIF(btrim(o."ReferenceCode"), '')
          ) <> 1
          LIMIT 1;
          IF FOUND THEN
            RAISE EXCEPTION 'Wallet top-up % has no unique posted transaction', bad_order;
          END IF;

          SELECT min(o."OrderCode") INTO bad_order
          FROM "WalletTopupOrders" o
          WHERE o."Status" = 'COMPLETED'
          GROUP BY o."TenantId", btrim(o."ReferenceCode")
          HAVING count(*) > 1
          LIMIT 1;
          IF FOUND THEN
            RAISE EXCEPTION 'Wallet top-up % shares its posted transaction with another order', bad_order;
          END IF;

          UPDATE "WalletTransactions" t
          SET "OrderCode" = o."OrderCode", "RequestedAmount" = o."Amount",
              "TransferContent" = o."TransferContent", "ExpiresAt" = o."ExpiresAt",
              "CompletedAt" = COALESCE(o."CompletedAt", t."CreatedAt"),
              "TopupDetails" = json_build_object(
                'bankCode', o."BankCode", 'bankName', o."BankName", 'accountNumber', o."AccountNumber",
                'accountName', o."AccountName", 'qrUrl', o."QrUrl")::text
          FROM "WalletTopupOrders" o
          WHERE o."Status" = 'COMPLETED' AND t."TenantId" = o."TenantId"
            AND t."Type" = 'TOPUP' AND t."Status" = 'SUCCESS'
            AND t."ReferenceCode" = NULLIF(btrim(o."ReferenceCode"), '');

          INSERT INTO "WalletTransactions" (
            "Id", "TenantId", "TransactionCode", "Type", "Amount", "BalanceBefore", "BalanceAfter",
            "Description", "PaymentMethod", "Status", "ReferenceCode", "CreatedAt", "UpdatedAt",
            "IsDeleted", "OrderCode", "RequestedAmount", "TransferContent", "TopupDetails", "ExpiresAt", "CompletedAt"
          )
          SELECT o."Id", o."TenantId", 'WAL-ORDER-' || md5(o."Id"), 'TOPUP', o."Amount", 0, 0,
            'Yêu cầu nạp ví VietQR', 'VietQR', o."Status", o."ReferenceCode", o."CreatedAt",
            o."UpdatedAt", o."IsDeleted", o."OrderCode", o."Amount", o."TransferContent",
            json_build_object('bankCode', o."BankCode", 'bankName', o."BankName", 'accountNumber', o."AccountNumber",
              'accountName', o."AccountName", 'qrUrl', o."QrUrl")::text, o."ExpiresAt", o."CompletedAt"
          FROM "WalletTopupOrders" o
          WHERE o."Status" <> 'COMPLETED';

          IF EXISTS (
            SELECT FROM "WalletTopupOrders" o
            WHERE NOT EXISTS (
              SELECT FROM "WalletTransactions" t
              WHERE t."TenantId" = o."TenantId" AND t."OrderCode" = o."OrderCode"
            )
          ) THEN
            RAISE EXCEPTION 'Wallet top-up migration did not preserve all order codes';
          END IF;

          DROP TABLE "WalletTopupOrders";
        END;
        $wallet$;

        CREATE UNIQUE INDEX IF NOT EXISTS "IX_WalletTransactions_OrderCode"
          ON "WalletTransactions" ("OrderCode");
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_WalletTransactions_SepayTransactionId"
          ON "WalletTransactions" ("SepayTransactionId");
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_WalletTransactions_SepayReference"
          ON "WalletTransactions" ("ReferenceCode")
          WHERE "Type" = 'TOPUP' AND "Status" IN ('SUCCESS', 'REVIEW') AND "ReferenceCode" IS NOT NULL AND "ReferenceCode" <> '';
        """;
}
