using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MT_GRMS.Infrastructure.Persistence.Migrations;

/// <summary>
/// Adds batch traceability snapshots read by P&amp;L and household tax reports.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261117106000_AddOrderDetailBatchSnapshots")]
public partial class AddOrderDetailBatchSnapshots : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE "OrderDetails"
            ADD COLUMN IF NOT EXISTS "BatchID" uuid NULL;
            ALTER TABLE "OrderDetails"
            ADD COLUMN IF NOT EXISTS "BatchCode" character varying(100) NULL;
            ALTER TABLE "OrderDetails"
            ADD COLUMN IF NOT EXISTS "BatchExpiryDate" timestamp without time zone NULL;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE "OrderDetails"
            DROP COLUMN IF EXISTS "BatchExpiryDate";
            ALTER TABLE "OrderDetails"
            DROP COLUMN IF EXISTS "BatchCode";
            ALTER TABLE "OrderDetails"
            DROP COLUMN IF EXISTS "BatchID";
            """);
    }
}
