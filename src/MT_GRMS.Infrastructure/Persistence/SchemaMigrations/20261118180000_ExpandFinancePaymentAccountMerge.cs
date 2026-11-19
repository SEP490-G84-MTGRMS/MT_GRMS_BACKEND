using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MT_GRMS.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261118180000_ExpandFinancePaymentAccountMerge")]
public partial class ExpandFinancePaymentAccountMerge : Migration
{
    public const string MergeSql = """
            ALTER TABLE "Branches" ADD COLUMN IF NOT EXISTS "CurrentBankFinanceAccountId" uuid;
            ALTER TABLE "FinanceAccounts" ADD COLUMN IF NOT EXISTS "BankCode" character varying(30);
            ALTER TABLE "FinanceAccounts" ADD COLUMN IF NOT EXISTS "Currency" character varying(3) NOT NULL DEFAULT 'VND';
            ALTER TABLE "FinanceAccounts" ADD COLUMN IF NOT EXISTS "IsDefaultForVietQr" boolean NOT NULL DEFAULT false;
            ALTER TABLE "FinanceAccounts" ADD COLUMN IF NOT EXISTS "SepayConnectionStatus" character varying(30) NOT NULL DEFAULT 'NotConfigured';
            ALTER TABLE "FinanceAccounts" ADD COLUMN IF NOT EXISTS "SepayApiKeyEncrypted" character varying(4000);
            ALTER TABLE "FinanceAccounts" ADD COLUMN IF NOT EXISTS "SepayWebhookSecret" character varying(200);
            ALTER TABLE "FinanceAccounts" ADD COLUMN IF NOT EXISTS "SepayLastVerifiedAtUtc" timestamp without time zone;
            ALTER TABLE "FinanceAccounts" ADD COLUMN IF NOT EXISTS "SepayLastError" character varying(500);
            ALTER TABLE "Tenants" ADD COLUMN IF NOT EXISTS "SepayApiKeyEncrypted" character varying(4000) NOT NULL DEFAULT '';
            ALTER TABLE "Tenants" ADD COLUMN IF NOT EXISTS "SepayWebhookSecret" character varying(200) NOT NULL DEFAULT '';
            ALTER TABLE "Tenants" ADD COLUMN IF NOT EXISTS "IsSepayEnabled" boolean NOT NULL DEFAULT false;
            ALTER TABLE "Tenants" ADD COLUMN IF NOT EXISTS "CashOutApprovalThreshold" numeric(18,2) NOT NULL DEFAULT 200000;
            ALTER TABLE "Tenants" ADD COLUMN IF NOT EXISTS "CashOutShiftTotalLimit" numeric(18,2) NOT NULL DEFAULT 0;
            ALTER TABLE "Orders" ADD COLUMN IF NOT EXISTS "FinanceAccountId" uuid;
            ALTER TABLE "PaymentTransactions" ADD COLUMN IF NOT EXISTS "FinanceAccountId" uuid;

            DO $$ BEGIN
                IF EXISTS (SELECT 1 FROM "StorePaymentConfigs" WHERE NOT "IsDeleted"
                    GROUP BY "TenantId" HAVING COUNT(*) > 1) THEN
                    RAISE EXCEPTION 'Multiple active store payment configs require manual reconciliation';
                END IF;
                IF EXISTS (SELECT 1 FROM "BranchPaymentAccounts" WHERE "IsActive" AND NOT "IsDeleted"
                    GROUP BY "PaymentAccountId" HAVING COUNT(DISTINCT "BranchId") > 1) THEN
                    RAISE EXCEPTION 'A payment account is assigned to multiple branches; reconcile before merge';
                END IF;
                IF EXISTS (SELECT 1 FROM "BranchPaymentAccounts" WHERE "IsActive" AND NOT "IsDeleted"
                    GROUP BY "TenantId", "BranchId" HAVING COUNT(*) > 1) THEN
                    RAISE EXCEPTION 'A branch has multiple active payment accounts; reconcile before merge';
                END IF;
                IF EXISTS (
                    SELECT 1 FROM (
                        SELECT "TenantId", "BranchID" AS "BranchId", "PaymentAccountId" FROM "Orders" WHERE "PaymentAccountId" IS NOT NULL
                        UNION
                        SELECT "TenantId", "BranchId", "PaymentAccountId" FROM "PaymentTransactions" WHERE "PaymentAccountId" IS NOT NULL
                        UNION
                        SELECT "TenantId", "BranchId", "PaymentAccountId" FROM "BranchPaymentAccounts" WHERE "IsActive" AND NOT "IsDeleted"
                    ) AS ownership GROUP BY "TenantId", "PaymentAccountId"
                    HAVING COUNT(DISTINCT "BranchId") > 1
                ) THEN
                    RAISE EXCEPTION 'A historical payment account belongs to multiple branches; reconcile before merge';
                END IF;
            END $$;

            UPDATE "Tenants" AS t SET
                "SepayApiKeyEncrypted" = c."SepayApiKeyEncrypted",
                "SepayWebhookSecret" = c."SepayWebhookSecret",
                "IsSepayEnabled" = c."IsActive",
                "CashOutApprovalThreshold" = c."CashOutApprovalThreshold",
                "CashOutShiftTotalLimit" = c."CashOutShiftTotalLimit"
            FROM "StorePaymentConfigs" AS c
            WHERE c."TenantId" = t."TenantID" AND NOT c."IsDeleted";

            UPDATE "FinanceAccounts" AS f SET
                "PaymentAccountId" = p."Id", "BranchId" = a."BranchId",
                "BankCode" = p."BankCode", "BankName" = p."BankName",
                "AccountNumber" = p."AccountNumber", "AccountHolderName" = p."AccountHolderName"
            FROM "PaymentAccounts" AS p JOIN "BranchPaymentAccounts" AS a
                ON a."PaymentAccountId" = p."Id" AND a."TenantId" = p."TenantId"
            WHERE f."TenantId" = p."TenantId" AND f."AccountType" = 3
                AND f."AccountNumber" = p."AccountNumber"
                AND (f."BankCode" = p."BankCode" OR f."BankCode" IS NULL)
                AND (f."BranchId" = a."BranchId" OR f."BranchId" IS NULL)
                AND a."IsActive" AND NOT a."IsDeleted" AND NOT p."IsDeleted";

            INSERT INTO "FinanceAccounts" (
                "Id", "TenantId", "BranchId", "PaymentAccountId", "AccountType",
                "AccountName", "CurrentBalance", "BankCode", "BankName", "AccountNumber",
                "AccountHolderName", "Currency", "IsDefaultForVietQr", "IsActive",
                "SepayConnectionStatus", "CreatedAt", "IsDeleted")
            SELECT gen_random_uuid(), p."TenantId", a."BranchId", p."Id", 3,
                p."BankName" || ' · ' || p."AccountNumber", 0,
                p."BankCode", p."BankName", p."AccountNumber", p."AccountHolderName",
                'VND', a."IsDefault", (p."IsActive" OR a."IsActive"), 'NotConfigured', NOW(), false
            FROM "PaymentAccounts" AS p JOIN "BranchPaymentAccounts" AS a
                ON a."PaymentAccountId" = p."Id" AND a."TenantId" = p."TenantId"
            WHERE a."IsActive" AND NOT a."IsDeleted" AND NOT p."IsDeleted"
              AND NOT EXISTS (SELECT 1 FROM "FinanceAccounts" AS f
                  WHERE f."TenantId" = p."TenantId" AND f."BranchId" = a."BranchId"
                    AND f."PaymentAccountId" = p."Id" AND f."AccountType" = 3);

            -- Các đơn/giao dịch cũ có thể trỏ tới tài khoản đã bỏ gán khỏi chi nhánh.
            -- Vẫn tạo dòng Bank lịch sử cho đúng chi nhánh, không gán lại làm tài khoản hiện hành.
            WITH historical AS (
                SELECT "TenantId", "BranchID" AS "BranchId", "PaymentAccountId" FROM "Orders" WHERE "PaymentAccountId" IS NOT NULL
                UNION
                SELECT "TenantId", "BranchId", "PaymentAccountId" FROM "PaymentTransactions" WHERE "PaymentAccountId" IS NOT NULL
            )
            UPDATE "FinanceAccounts" AS f SET "PaymentAccountId" = p."Id",
                "BankCode" = p."BankCode", "BankName" = p."BankName",
                "AccountNumber" = p."AccountNumber", "AccountHolderName" = p."AccountHolderName"
            FROM historical AS h JOIN "PaymentAccounts" AS p
                ON p."Id" = h."PaymentAccountId" AND p."TenantId" = h."TenantId"
            WHERE f."TenantId" = h."TenantId" AND f."BranchId" = h."BranchId"
                AND f."AccountType" = 3 AND f."AccountNumber" = p."AccountNumber"
                AND (f."BankCode" = p."BankCode" OR f."BankCode" IS NULL)
                AND f."PaymentAccountId" IS NULL;

            WITH historical AS (
                SELECT "TenantId", "BranchID" AS "BranchId", "PaymentAccountId" FROM "Orders" WHERE "PaymentAccountId" IS NOT NULL
                UNION
                SELECT "TenantId", "BranchId", "PaymentAccountId" FROM "PaymentTransactions" WHERE "PaymentAccountId" IS NOT NULL
            )
            INSERT INTO "FinanceAccounts" (
                "Id", "TenantId", "BranchId", "PaymentAccountId", "AccountType",
                "AccountName", "CurrentBalance", "BankCode", "BankName", "AccountNumber",
                "AccountHolderName", "Currency", "IsDefaultForVietQr", "IsActive",
                "SepayConnectionStatus", "CreatedAt", "IsDeleted")
            SELECT gen_random_uuid(), h."TenantId", h."BranchId", p."Id", 3,
                p."BankName" || ' · ' || p."AccountNumber", 0,
                p."BankCode", p."BankName", p."AccountNumber", p."AccountHolderName",
                'VND', false, p."IsActive", 'NotConfigured', NOW(), false
            FROM historical AS h JOIN "PaymentAccounts" AS p
                ON p."Id" = h."PaymentAccountId" AND p."TenantId" = h."TenantId"
            WHERE NOT EXISTS (SELECT 1 FROM "FinanceAccounts" AS f
                WHERE f."TenantId" = h."TenantId" AND f."BranchId" = h."BranchId"
                    AND f."PaymentAccountId" = p."Id" AND f."AccountType" = 3);

            UPDATE "Branches" AS b SET "CurrentBankFinanceAccountId" = f."Id"
            FROM "BranchPaymentAccounts" AS a JOIN "FinanceAccounts" AS f
                ON f."PaymentAccountId" = a."PaymentAccountId"
                AND f."BranchId" = a."BranchId" AND f."TenantId" = a."TenantId"
            WHERE b."BranchID" = a."BranchId" AND b."TenantID" = a."TenantId"
              AND a."IsActive" AND NOT a."IsDeleted";

            DO $$ BEGIN
                IF EXISTS (SELECT 1 FROM "FinanceAccounts"
                    WHERE "PaymentAccountId" IS NOT NULL AND "AccountType" = 3 AND NOT "IsDeleted"
                    GROUP BY "TenantId", "BranchId", "PaymentAccountId" HAVING COUNT(*) > 1) THEN
                    RAISE EXCEPTION 'Duplicate bank ledgers for a payment account; reconcile balances before merge';
                END IF;
            END $$;

            UPDATE "Orders" AS o SET "FinanceAccountId" = f."Id"
            FROM "FinanceAccounts" AS f
            WHERE o."PaymentAccountId" = f."PaymentAccountId"
              AND o."BranchID" = f."BranchId" AND o."TenantId" = f."TenantId";
            UPDATE "PaymentTransactions" AS tx SET "FinanceAccountId" = f."Id"
            FROM "FinanceAccounts" AS f
            WHERE tx."PaymentAccountId" = f."PaymentAccountId"
              AND tx."BranchId" = f."BranchId" AND tx."TenantId" = f."TenantId";

            DO $$ BEGIN
                IF EXISTS (SELECT 1 FROM "Orders" WHERE "PaymentAccountId" IS NOT NULL AND "FinanceAccountId" IS NULL)
                    OR EXISTS (SELECT 1 FROM "PaymentTransactions" WHERE "PaymentAccountId" IS NOT NULL AND "FinanceAccountId" IS NULL)
                THEN RAISE EXCEPTION 'Unmapped historical payment account; reconcile before merge'; END IF;
            END $$;

            CREATE INDEX IF NOT EXISTS "IX_Branches_CurrentBankFinanceAccountId"
                ON "Branches" ("CurrentBankFinanceAccountId");
            CREATE INDEX IF NOT EXISTS "IX_Orders_FinanceAccountId" ON "Orders" ("FinanceAccountId");
            CREATE INDEX IF NOT EXISTS "IX_PaymentTransactions_FinanceAccountId"
                ON "PaymentTransactions" ("FinanceAccountId");
            CREATE UNIQUE INDEX IF NOT EXISTS "UX_FinanceAccounts_Tenant_Bank_AccountNumber"
                ON "FinanceAccounts" ("TenantId", "BankCode", "AccountNumber")
                WHERE "AccountType" = 3 AND "BankCode" IS NOT NULL AND "AccountNumber" IS NOT NULL AND NOT "IsDeleted";
            DO $$ BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_Branches_CurrentBankFinanceAccount') THEN
                    ALTER TABLE "Branches" ADD CONSTRAINT "FK_Branches_CurrentBankFinanceAccount"
                        FOREIGN KEY ("CurrentBankFinanceAccountId") REFERENCES "FinanceAccounts" ("Id") ON DELETE RESTRICT;
                END IF;
                IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_Orders_FinanceAccount') THEN
                    ALTER TABLE "Orders" ADD CONSTRAINT "FK_Orders_FinanceAccount"
                        FOREIGN KEY ("FinanceAccountId") REFERENCES "FinanceAccounts" ("Id") ON DELETE RESTRICT;
                END IF;
                IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_PaymentTransactions_FinanceAccount') THEN
                    ALTER TABLE "PaymentTransactions" ADD CONSTRAINT "FK_PaymentTransactions_FinanceAccount"
                        FOREIGN KEY ("FinanceAccountId") REFERENCES "FinanceAccounts" ("Id") ON DELETE RESTRICT;
                END IF;
            END $$;
            """;

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(MergeSql);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        throw new NotSupportedException("Restore finance account mappings from a verified backup before rolling this migration back.");
    }
}
