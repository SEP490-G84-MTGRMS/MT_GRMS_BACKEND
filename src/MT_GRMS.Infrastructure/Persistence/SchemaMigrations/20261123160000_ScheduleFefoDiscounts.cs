using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MT_GRMS.Infrastructure.Persistence;

#nullable disable

namespace MT_GRMS.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261123160000_ScheduleFefoDiscounts")]
public sealed class ScheduleFefoDiscounts : Migration
{
    internal const string UpSql = """
        ALTER TABLE IF EXISTS "BranchStocks" ADD COLUMN IF NOT EXISTS "FefoDiscountStartAtUtc" timestamp with time zone NULL;
        ALTER TABLE IF EXISTS "BranchStocks" ADD COLUMN IF NOT EXISTS "FefoDiscountEndAtUtc" timestamp with time zone NULL;
        """;

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(UpSql);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE IF EXISTS "BranchStocks" DROP COLUMN IF EXISTS "FefoDiscountStartAtUtc";
        ALTER TABLE IF EXISTS "BranchStocks" DROP COLUMN IF EXISTS "FefoDiscountEndAtUtc";
        """);
}
