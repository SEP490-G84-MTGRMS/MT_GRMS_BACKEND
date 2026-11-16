using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MT_GRMS.Infrastructure.Persistence.Migrations;

/// <summary>
/// Adds the optional processed-price snapshot used by product and report queries.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261117107000_AddProcessedProductPrice")]
public partial class AddProcessedProductPrice : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE "Products"
            ADD COLUMN IF NOT EXISTS "ProcessedSellingPricePerKg" numeric(18,2) NULL;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE "Products"
            DROP COLUMN IF EXISTS "ProcessedSellingPricePerKg";
            """);
    }
}
