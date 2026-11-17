using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MT_GRMS.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261117120000_SimplifyCustomerDebt")]
public partial class SimplifyCustomerDebt : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TABLE IF EXISTS "CustomerDebtAllocations";
            DROP TABLE IF EXISTS "CustomerDebtInvoices";

            DROP INDEX IF EXISTS "IX_Customers_TenantID_DebtStatus";
            DROP INDEX IF EXISTS "IX_Customers_TenantId_DebtStatus";
            ALTER TABLE "Customers" DROP COLUMN IF EXISTS "AllowDebt";
            ALTER TABLE "Customers" DROP COLUMN IF EXISTS "DebtLimit";
            ALTER TABLE "Customers" DROP COLUMN IF EXISTS "PrepaymentBalance";
            ALTER TABLE "Customers" DROP COLUMN IF EXISTS "MaxDebtDays";
            ALTER TABLE "Customers" DROP COLUMN IF EXISTS "DebtStatus";
            ALTER TABLE "Customers" DROP COLUMN IF EXISTS "IsBlockedForDebt";
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        throw new NotSupportedException("Deleted development debt invoices cannot be reconstructed.");
    }
}
