using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MT_GRMS.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261118190000_DropLegacyPaymentAccountTables")]
public partial class DropLegacyPaymentAccountTables : Migration
{
    public const string DropSql = """
        DROP TABLE IF EXISTS "BranchPaymentAccounts" CASCADE;
        DROP TABLE IF EXISTS "PaymentAccounts" CASCADE;
        DROP TABLE IF EXISTS "StorePaymentConfigs" CASCADE;
        """;

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(DropSql);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        throw new NotSupportedException("Legacy payment account tables cannot be automatically restored. Restore from backup.");
    }
}
