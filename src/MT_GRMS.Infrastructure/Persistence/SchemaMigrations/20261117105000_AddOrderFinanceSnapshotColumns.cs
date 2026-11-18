using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MT_GRMS.Infrastructure.Persistence.Migrations;

/// <summary>
/// Adds business-day and payment-account snapshots consumed by finance reports.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261117105000_AddOrderFinanceSnapshotColumns")]
public partial class AddOrderFinanceSnapshotColumns : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE "Orders"
            ADD COLUMN IF NOT EXISTS "BusinessDate" date;

            UPDATE "Orders"
            SET "BusinessDate" = ("OrderDate" + interval '1 hour')::date
            WHERE "BusinessDate" IS NULL;

            ALTER TABLE "Orders"
            ALTER COLUMN "BusinessDate" SET NOT NULL;

            ALTER TABLE "Orders"
            ADD COLUMN IF NOT EXISTS "BusinessDayClosingId" uuid NULL;
            ALTER TABLE "Orders"
            ADD COLUMN IF NOT EXISTS "PaymentAccountId" uuid NULL;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE "Orders"
            DROP COLUMN IF EXISTS "PaymentAccountId";
            ALTER TABLE "Orders"
            DROP COLUMN IF EXISTS "BusinessDayClosingId";
            ALTER TABLE "Orders"
            DROP COLUMN IF EXISTS "BusinessDate";
            """);
    }
}
