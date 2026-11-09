using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MT_GRMS.Infrastructure.Persistence.Migrations;

/// <summary>
/// Catalog gán SP ↔ CN cho màn /store-owner/stores/products.
/// Backfill từ Distinct(BranchId, ProductId) trên BranchStocks.
/// </summary>
[Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(AppDbContext))]
[Migration("20261103230000_AddBranchProductsCatalog")]
public partial class AddBranchProductsCatalog : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE TABLE IF NOT EXISTS "BranchProducts" (
                "Id" uuid NOT NULL,
                "TenantId" character varying(50) NOT NULL,
                "BranchId" uuid NOT NULL,
                "ProductId" uuid NOT NULL,
                "SellingPriceOverride" numeric(18,2) NULL,
                "Status" character varying(20) NOT NULL DEFAULT 'Active',
                "ShelfLocation" character varying(50) NULL,
                "CreatedAt" timestamp with time zone NOT NULL DEFAULT (NOW() AT TIME ZONE 'UTC'),
                "UpdatedAt" timestamp with time zone NULL,
                "IsDeleted" boolean NOT NULL DEFAULT FALSE,
                "CreatedBy" uuid NULL,
                CONSTRAINT "PK_BranchProducts" PRIMARY KEY ("Id"),
                CONSTRAINT "FK_BranchProducts_Branches_BranchId" FOREIGN KEY ("BranchId") REFERENCES "Branches" ("BranchID") ON DELETE RESTRICT,
                CONSTRAINT "FK_BranchProducts_Products_ProductId" FOREIGN KEY ("ProductId") REFERENCES "Products" ("ProductID") ON DELETE RESTRICT
            );

            CREATE UNIQUE INDEX IF NOT EXISTS "IX_BranchProducts_TenantId_BranchId_ProductId"
                ON "BranchProducts" ("TenantId", "BranchId", "ProductId")
                WHERE "IsDeleted" = FALSE;

            CREATE INDEX IF NOT EXISTS "IX_BranchProducts_BranchId" ON "BranchProducts" ("BranchId");
            CREATE INDEX IF NOT EXISTS "IX_BranchProducts_ProductId" ON "BranchProducts" ("ProductId");

            -- Backfill: mỗi cặp CN–SP từ tồn kho hiện có → 1 dòng catalog.
            -- SellingPriceOverride = NULL (mặc định dùng giá bán Master); Status = 'Active'.
            INSERT INTO "BranchProducts" (
                "Id", "TenantId", "BranchId", "ProductId", "SellingPriceOverride",
                "Status", "ShelfLocation", "CreatedAt", "UpdatedAt", "IsDeleted", "CreatedBy"
            )
            SELECT
                gen_random_uuid(),
                src."TenantId",
                src."BranchId",
                src."ProductId",
                NULL,
                'Active',
                src."ShelfLocation",
                COALESCE(src."AssignedAt", NOW() AT TIME ZONE 'UTC'),
                NOW() AT TIME ZONE 'UTC',
                FALSE,
                NULL
            FROM (
                SELECT DISTINCT ON (bs."TenantId", bs."BranchId", bs."ProductId")
                    bs."TenantId",
                    bs."BranchId",
                    bs."ProductId",
                    bs."ShelfLocation",
                    bs."CreatedAt" AS "AssignedAt"
                FROM "BranchStocks" bs
                WHERE bs."IsDeleted" = FALSE
                ORDER BY bs."TenantId", bs."BranchId", bs."ProductId", bs."CreatedAt" ASC
            ) src
            LEFT JOIN "Products" p ON p."ProductID" = src."ProductId"
            WHERE NOT EXISTS (
                SELECT 1
                FROM "BranchProducts" bp
                WHERE bp."TenantId" = src."TenantId"
                  AND bp."BranchId" = src."BranchId"
                  AND bp."ProductId" = src."ProductId"
                  AND bp."IsDeleted" = FALSE
            );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP INDEX IF EXISTS "IX_BranchProducts_ProductId";
            DROP INDEX IF EXISTS "IX_BranchProducts_BranchId";
            DROP INDEX IF EXISTS "IX_BranchProducts_TenantId_BranchId_ProductId";
            DROP TABLE IF EXISTS "BranchProducts";
            """);
    }
}
