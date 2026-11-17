using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MT_GRMS.Infrastructure.Persistence.Migrations;

/// <summary>
/// Adds the configured business-day boundary used by branch and finance queries.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261117100000_AddBranchBusinessDayStartTime")]
public partial class AddBranchBusinessDayStartTime : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE "Branches"
            ADD COLUMN IF NOT EXISTS "BusinessDayStartTime" interval NOT NULL DEFAULT interval '6 hours';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE "Branches"
            DROP COLUMN IF EXISTS "BusinessDayStartTime";
            """);
    }
}
