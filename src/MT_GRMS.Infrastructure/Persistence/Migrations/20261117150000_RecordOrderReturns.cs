using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MT_GRMS.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261117150000_RecordOrderReturns")]
public partial class RecordOrderReturns : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("ALTER TABLE \"Orders\" ADD COLUMN IF NOT EXISTS \"ReturnedAmount\" numeric(18,2) NOT NULL DEFAULT 0;");
        migrationBuilder.Sql("ALTER TABLE \"Orders\" ADD COLUMN IF NOT EXISTS \"DebtOffsetAmount\" numeric(18,2) NOT NULL DEFAULT 0;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("ALTER TABLE \"Orders\" DROP COLUMN IF EXISTS \"ReturnedAmount\";");
        migrationBuilder.Sql("ALTER TABLE \"Orders\" DROP COLUMN IF EXISTS \"DebtOffsetAmount\";");
    }
}
