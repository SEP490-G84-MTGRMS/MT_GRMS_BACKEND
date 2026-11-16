using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MT_GRMS.Infrastructure.Persistence.Migrations;

/// <summary>
/// Adds the optional payment-account link used by consolidated finance accounts.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261117101000_AddFinanceAccountPaymentAccountId")]
public partial class AddFinanceAccountPaymentAccountId : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE "FinanceAccounts"
            ADD COLUMN IF NOT EXISTS "PaymentAccountId" uuid NULL;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE "FinanceAccounts"
            DROP COLUMN IF EXISTS "PaymentAccountId";
            """);
    }
}
