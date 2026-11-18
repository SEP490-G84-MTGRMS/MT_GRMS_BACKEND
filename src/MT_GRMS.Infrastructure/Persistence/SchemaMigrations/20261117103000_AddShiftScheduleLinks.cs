using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MT_GRMS.Infrastructure.Persistence.Migrations;

/// <summary>
/// Adds the optional schedule references selected with the shift entity.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261117103000_AddShiftScheduleLinks")]
public partial class AddShiftScheduleLinks : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE "ShiftZReports"
            ADD COLUMN IF NOT EXISTS "OpenedScheduleId" uuid NULL;
            ALTER TABLE "ShiftZReports"
            ADD COLUMN IF NOT EXISTS "ClosedScheduleId" uuid NULL;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE "ShiftZReports"
            DROP COLUMN IF EXISTS "ClosedScheduleId";
            ALTER TABLE "ShiftZReports"
            DROP COLUMN IF EXISTS "OpenedScheduleId";
            """);
    }
}
