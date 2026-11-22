using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MT_GRMS.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261122010000_AddTaxFilingConfirmations")]
public sealed class AddTaxFilingConfirmations : Migration
{
    internal const string UpSql = """
        CREATE TABLE IF NOT EXISTS "TaxFilingConfirmations" (
            "SnapshotId" uuid PRIMARY KEY REFERENCES "HouseholdTaxSnapshots" ("Id") ON DELETE CASCADE,
            "TenantId" varchar(50) NOT NULL,
            "ConfirmedBy" varchar(64) NOT NULL,
            "ConfirmedAtUtc" timestamp without time zone NOT NULL
        );
        """;

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(UpSql);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("DROP TABLE IF EXISTS \"TaxFilingConfirmations\";");
}
