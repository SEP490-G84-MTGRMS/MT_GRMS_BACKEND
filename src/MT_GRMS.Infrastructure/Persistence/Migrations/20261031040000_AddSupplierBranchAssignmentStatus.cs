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
                DO $$
                DECLARE tbl text;
                BEGIN
                    IF to_regclass('public."SupplierBranch"') IS NOT NULL THEN
                        tbl := '"SupplierBranch"';
                    ELSIF to_regclass('public.supplier_branch_assignments') IS NOT NULL THEN
                        tbl := 'supplier_branch_assignments';
                    ELSE
                        RETURN;
                    END IF;
                    EXECUTE format('ALTER TABLE %s ADD COLUMN IF NOT EXISTS "Status" integer NOT NULL DEFAULT 1', tbl);
                    EXECUTE format('ALTER TABLE %s ADD COLUMN IF NOT EXISTS "InactiveReason" character varying(500)', tbl);
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                DECLARE tbl text;
                BEGIN
                    IF to_regclass('public."SupplierBranch"') IS NOT NULL THEN
                        tbl := '"SupplierBranch"';
                    ELSIF to_regclass('public.supplier_branch_assignments') IS NOT NULL THEN
                        tbl := 'supplier_branch_assignments';
                    ELSE
                        RETURN;
                    END IF;
                    EXECUTE format('ALTER TABLE %s DROP COLUMN IF EXISTS "InactiveReason"', tbl);
                    EXECUTE format('ALTER TABLE %s DROP COLUMN IF EXISTS "Status"', tbl);
                END $$;
                """);
        }
    }
}
