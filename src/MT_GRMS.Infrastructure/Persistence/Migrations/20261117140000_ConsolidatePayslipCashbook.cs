using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MT_GRMS.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261117140000_ConsolidatePayslipCashbook")]
public partial class ConsolidatePayslipCashbook : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE "Payslips" ADD COLUMN IF NOT EXISTS "CashbookEntryId" uuid;

            UPDATE "Payslips" p
            SET "CashbookEntryId" = e."Id"
            FROM "CashbookEntries" e
            WHERE p."CashbookEntryId" IS NULL
              AND p."TenantId" = e."TenantId"
              AND e."SourceType" = 'SALARY'
              AND e."SourceId" = p."PayslipID"
              AND e."EntryCode" = p."CashbookVoucherCode";

            ALTER TABLE "Payslips" DROP CONSTRAINT IF EXISTS "FK_Payslips_CashbookTransactions_CashbookTransactionId";
            ALTER TABLE "Payslips" DROP CONSTRAINT IF EXISTS "FK_Payslips_CashbookTransaction_CashbookTransactionId";
            DROP INDEX IF EXISTS "IX_Payslips_CashbookTransactionId";
            ALTER TABLE "Payslips" DROP COLUMN IF EXISTS "CashbookTransactionId";
            CREATE INDEX IF NOT EXISTS "IX_Payslips_CashbookEntryId" ON "Payslips" ("CashbookEntryId");
            ALTER TABLE "Payslips" ADD CONSTRAINT "FK_Payslips_CashbookEntries_CashbookEntryId"
                FOREIGN KEY ("CashbookEntryId") REFERENCES "CashbookEntries" ("Id") ON DELETE SET NULL;

            DROP TABLE IF EXISTS "CashbookTransactions";
            DROP TABLE IF EXISTS "CashbookTransaction";
        """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        throw new NotSupportedException("The consolidated cashbook history cannot be reconstructed after dropping the duplicate table.");
    }
}
