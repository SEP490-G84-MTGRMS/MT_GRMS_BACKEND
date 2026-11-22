using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MT_GRMS.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261122170000_OneActiveCashFundAndShiftPerBranch")]
public partial class OneActiveCashFundAndShiftPerBranch : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$
            BEGIN
                IF EXISTS (
                    SELECT 1 FROM "FinanceAccounts"
                    WHERE "BranchId" IS NOT NULL AND "IsActive" = TRUE AND NOT "IsDeleted" AND "AccountType" IN (1, 2)
                    GROUP BY "TenantId", "BranchId", "AccountType" HAVING COUNT(*) > 1
                ) THEN
                    RAISE EXCEPTION 'Multiple active tills or safes exist in one branch; reconcile balances before applying one-fund rule';
                END IF;
                IF EXISTS (
                    SELECT 1 FROM "ShiftZReports"
                    WHERE "Status" IN (1, 3) AND NOT "IsDeleted"
                    GROUP BY "TenantId", "BranchId" HAVING COUNT(*) > 1
                ) THEN
                    RAISE EXCEPTION 'Multiple active shifts exist in one branch; close or reconcile shifts before applying one-shift rule';
                END IF;
            END $$;

            CREATE UNIQUE INDEX IF NOT EXISTS "IX_FinanceAccounts_OneActiveCashFundPerBranch"
            ON "FinanceAccounts" ("TenantId", "BranchId", "AccountType")
            WHERE "BranchId" IS NOT NULL AND "IsActive" = TRUE AND NOT "IsDeleted" AND "AccountType" IN (1, 2);

            CREATE UNIQUE INDEX IF NOT EXISTS "IX_ShiftZReports_OneActiveShiftPerBranch"
            ON "ShiftZReports" ("TenantId", "BranchId")
            WHERE "Status" IN (1, 3) AND NOT "IsDeleted";
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP INDEX IF EXISTS "IX_ShiftZReports_OneActiveShiftPerBranch";
            DROP INDEX IF EXISTS "IX_FinanceAccounts_OneActiveCashFundPerBranch";
            """);
    }
}
