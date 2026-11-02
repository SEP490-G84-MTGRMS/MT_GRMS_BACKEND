using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MT_GRMS.Infrastructure.Persistence.Migrations
{
    [Migration("20261031225600_RenameSupplierBranchAssignmentsToSupplierBranch")]
    public partial class RenameSupplierBranchAssignmentsToSupplierBranch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF to_regclass('public.supplier_branch_assignments') IS NOT NULL
                       AND to_regclass('public."SupplierBranch"') IS NULL THEN
                        ALTER TABLE supplier_branch_assignments RENAME TO "SupplierBranch";
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF to_regclass('public."SupplierBranch"') IS NOT NULL
                       AND to_regclass('public.supplier_branch_assignments') IS NULL THEN
                        ALTER TABLE "SupplierBranch" RENAME TO supplier_branch_assignments;
                    END IF;
                END $$;
                """);
        }
    }
}
