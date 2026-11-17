using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MT_GRMS.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261117170000_MoveTaxPolicyToSnapshot")]
public partial class MoveTaxPolicyToSnapshot : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("ALTER TABLE \"HouseholdTaxSnapshots\" ADD COLUMN IF NOT EXISTS \"TaxPolicyJson\" jsonb NOT NULL DEFAULT '{}'::jsonb;");
        migrationBuilder.Sql("DROP TABLE IF EXISTS \"HouseholdTaxYearSettings\";");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        throw new NotSupportedException("Deleted development tax settings cannot be reconstructed.");
    }
}
