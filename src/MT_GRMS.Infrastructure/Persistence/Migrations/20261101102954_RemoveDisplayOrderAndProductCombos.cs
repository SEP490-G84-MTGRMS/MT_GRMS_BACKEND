using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MT_GRMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveDisplayOrderAndProductCombos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Gỡ bỏ bảng ProductCombos và các cột không sử dụng trong Categories
            migrationBuilder.Sql("DROP TABLE IF EXISTS \"ProductCombos\";");
            migrationBuilder.Sql("ALTER TABLE \"Categories\" DROP COLUMN IF EXISTS \"DisplayOrder\";");
            migrationBuilder.Sql("ALTER TABLE \"Categories\" DROP COLUMN IF EXISTS \"ParentCategoryID\";");

            // 2. Chuyển Barcode sang Nullable và bổ sung ImageUrls text[] cho Products
            migrationBuilder.Sql("ALTER TABLE \"Products\" ALTER COLUMN \"Barcode\" DROP NOT NULL;");
            migrationBuilder.Sql("ALTER TABLE \"Products\" ADD COLUMN IF NOT EXISTS \"ImageUrls\" text[] DEFAULT '{}';");
            migrationBuilder.Sql("UPDATE \"Products\" SET \"ImageUrls\" = ARRAY[\"ImageUrl\"] WHERE \"ImageUrl\" IS NOT NULL AND (\"ImageUrls\" IS NULL OR \"ImageUrls\" = '{}');");

            // 3. Đảm bảo bảng liên kết chi nhánh nhà cung cấp supplier_branch_assignments tồn tại
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS supplier_branch_assignments (
                    ""Id"" uuid NOT NULL PRIMARY KEY,
                    ""BranchId"" uuid NOT NULL,
                    ""SupplierId"" uuid NOT NULL,
                    ""TenantId"" text NOT NULL,
                    ""CreatedBy"" uuid,
                    ""CreatedAt"" timestamp without time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    ""UpdatedAt"" timestamp without time zone,
                    ""IsDeleted"" boolean NOT NULL DEFAULT false,
                    CONSTRAINT ""FK_supplier_branch_assignments_Branches_BranchId"" FOREIGN KEY (""BranchId"") REFERENCES ""Branches"" (""BranchID"") ON DELETE RESTRICT,
                    CONSTRAINT ""FK_supplier_branch_assignments_Suppliers_SupplierId"" FOREIGN KEY (""SupplierId"") REFERENCES ""Suppliers"" (""Id"") ON DELETE CASCADE
                );
                CREATE INDEX IF NOT EXISTS ""IX_supplier_branch_assignments_BranchId"" ON supplier_branch_assignments (""BranchId"");
                CREATE INDEX IF NOT EXISTS ""IX_supplier_branch_assignments_SupplierId"" ON supplier_branch_assignments (""SupplierId"");
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_supplier_branch_assignments_TenantId_SupplierId_BranchId"" ON supplier_branch_assignments (""TenantId"", ""SupplierId"", ""BranchId"");
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS supplier_branch_assignments;");
            migrationBuilder.Sql("ALTER TABLE \"Products\" DROP COLUMN IF EXISTS \"ImageUrls\";");
        }
    }
}
