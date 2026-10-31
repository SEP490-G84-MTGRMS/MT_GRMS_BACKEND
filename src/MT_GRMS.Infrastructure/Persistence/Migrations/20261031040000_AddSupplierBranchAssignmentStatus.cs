using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MT_GRMS.Infrastructure.Persistence.Migrations
{
    [Migration("20261031040000_AddSupplierBranchAssignmentStatus")]
    public partial class AddSupplierBranchAssignmentStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE supplier_branch_assignments
                ADD COLUMN IF NOT EXISTS "Status" integer NOT NULL DEFAULT 1;
                ALTER TABLE supplier_branch_assignments
                ADD COLUMN IF NOT EXISTS "InactiveReason" character varying(500);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE supplier_branch_assignments DROP COLUMN IF EXISTS "InactiveReason";
                ALTER TABLE supplier_branch_assignments DROP COLUMN IF EXISTS "Status";
                """);
        }
    }
}
