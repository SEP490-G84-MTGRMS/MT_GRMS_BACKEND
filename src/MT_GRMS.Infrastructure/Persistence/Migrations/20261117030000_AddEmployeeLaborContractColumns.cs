using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MT_GRMS.Infrastructure.Persistence.Migrations;

/// <summary>
/// Repairs the Employee schema drift introduced when labor-contract fields were
/// added to the domain model without a corresponding database migration.
/// The guards keep this migration safe for databases where either column was
/// already added manually.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261117030000_AddEmployeeLaborContractColumns")]
public partial class AddEmployeeLaborContractColumns : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE "Employees"
            ADD COLUMN IF NOT EXISTS "HasLaborContract" boolean NOT NULL DEFAULT false;

            ALTER TABLE "Employees"
            ADD COLUMN IF NOT EXISTS "InsuranceBaseSalary" numeric(18,2) NULL;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE "Employees" DROP COLUMN IF EXISTS "InsuranceBaseSalary";
            ALTER TABLE "Employees" DROP COLUMN IF EXISTS "HasLaborContract";
            """);
    }
}
