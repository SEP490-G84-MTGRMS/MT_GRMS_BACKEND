using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MT_GRMS.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261122000000_AddExpenseVoucherDueDate")]
public sealed class AddExpenseVoucherDueDate : Migration
{
    internal const string UpSql = """
        ALTER TABLE "ExpenseVouchers" ADD COLUMN IF NOT EXISTS "DueDate" date NULL;
        CREATE INDEX IF NOT EXISTS "IX_ExpenseVouchers_DueDate"
            ON "ExpenseVouchers" ("DueDate") WHERE "DueDate" IS NOT NULL AND "IsDeleted" = false;
        """;

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(UpSql);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP INDEX IF EXISTS "IX_ExpenseVouchers_DueDate";
        ALTER TABLE "ExpenseVouchers" DROP COLUMN IF EXISTS "DueDate";
        """);
}
