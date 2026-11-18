using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MT_GRMS.Infrastructure.Persistence.Migrations;

/// <summary>
/// Adds optional cashbook posting metadata to shift cash movements.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261117104000_AddShiftCashMovementPostingColumns")]
public partial class AddShiftCashMovementPostingColumns : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE "ShiftCashMovements"
            ADD COLUMN IF NOT EXISTS "PostedCashbookEntryId" uuid NULL;
            ALTER TABLE "ShiftCashMovements"
            ADD COLUMN IF NOT EXISTS "PostedAtUtc" timestamp without time zone NULL;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE "ShiftCashMovements"
            DROP COLUMN IF EXISTS "PostedAtUtc";
            ALTER TABLE "ShiftCashMovements"
            DROP COLUMN IF EXISTS "PostedCashbookEntryId";
            """);
    }
}
