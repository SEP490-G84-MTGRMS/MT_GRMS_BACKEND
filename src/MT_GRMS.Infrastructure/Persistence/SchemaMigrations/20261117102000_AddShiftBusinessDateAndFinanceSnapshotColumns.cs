using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MT_GRMS.Infrastructure.Persistence.Migrations;

/// <summary>
/// Repairs shift columns used by finance overview and business-day reporting.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261117102000_AddShiftBusinessDateAndFinanceSnapshotColumns")]
public partial class AddShiftBusinessDateAndFinanceSnapshotColumns : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE "ShiftZReports"
            ADD COLUMN IF NOT EXISTS "BusinessDate" date;

            UPDATE "ShiftZReports"
            SET "BusinessDate" = ("OpenedAtUtc" + interval '1 hour')::date
            WHERE "BusinessDate" IS NULL;

            ALTER TABLE "ShiftZReports"
            ALTER COLUMN "BusinessDate" SET NOT NULL;

            ALTER TABLE "ShiftZReports"
            ADD COLUMN IF NOT EXISTS "DiscrepancyReason" character varying(500) NULL;
            ALTER TABLE "ShiftZReports"
            ADD COLUMN IF NOT EXISTS "ClosingVersion" bigint NOT NULL DEFAULT 1;
            ALTER TABLE "ShiftZReports"
            ADD COLUMN IF NOT EXISTS "DestinationFundId" uuid NULL;
            ALTER TABLE "ShiftZReports"
            ADD COLUMN IF NOT EXISTS "TotalCashSales" numeric(18,2) NOT NULL DEFAULT 0;
            ALTER TABLE "ShiftZReports"
            ADD COLUMN IF NOT EXISTS "SnapshotLocked" boolean NOT NULL DEFAULT false;
            ALTER TABLE "ShiftZReports"
            ADD COLUMN IF NOT EXISTS "CashbookPostingStatus" integer NOT NULL DEFAULT 1;
            ALTER TABLE "ShiftZReports"
            ADD COLUMN IF NOT EXISTS "BusinessDayClosingId" uuid NULL;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE "ShiftZReports"
            DROP COLUMN IF EXISTS "BusinessDayClosingId";
            ALTER TABLE "ShiftZReports"
            DROP COLUMN IF EXISTS "CashbookPostingStatus";
            ALTER TABLE "ShiftZReports"
            DROP COLUMN IF EXISTS "SnapshotLocked";
            ALTER TABLE "ShiftZReports"
            DROP COLUMN IF EXISTS "TotalCashSales";
            ALTER TABLE "ShiftZReports"
            DROP COLUMN IF EXISTS "DestinationFundId";
            ALTER TABLE "ShiftZReports"
            DROP COLUMN IF EXISTS "ClosingVersion";
            ALTER TABLE "ShiftZReports"
            DROP COLUMN IF EXISTS "DiscrepancyReason";
            ALTER TABLE "ShiftZReports"
            DROP COLUMN IF EXISTS "BusinessDate";
            """);
    }
}
