using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MT_GRMS.Infrastructure.Persistence.Migrations;

[Migration("20261103233000_AddBranchProductUnitPrices")]
public partial class AddBranchProductUnitPrices : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE TABLE IF NOT EXISTS "BranchProductUnitPrices" (
                "Id" uuid NOT NULL,
                "TenantId" character varying(50) NOT NULL,
                "BranchProductId" uuid NOT NULL,
                "ProductUnitId" uuid NOT NULL,
                "SellingPriceOverride" numeric(18,2) NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL DEFAULT (NOW() AT TIME ZONE 'UTC'),
                "UpdatedAt" timestamp with time zone NULL,
                "IsDeleted" boolean NOT NULL DEFAULT FALSE,
                "CreatedBy" uuid NULL,
                CONSTRAINT "PK_BranchProductUnitPrices" PRIMARY KEY ("Id"),
                CONSTRAINT "FK_BranchProductUnitPrices_BranchProducts_BranchProductId"
                    FOREIGN KEY ("BranchProductId") REFERENCES "BranchProducts" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_BranchProductUnitPrices_ProductUnits_ProductUnitId"
                    FOREIGN KEY ("ProductUnitId") REFERENCES "ProductUnits" ("UnitID") ON DELETE RESTRICT
            );

            CREATE UNIQUE INDEX IF NOT EXISTS "IX_BranchProductUnitPrices_BranchProductId_ProductUnitId"
                ON "BranchProductUnitPrices" ("BranchProductId", "ProductUnitId")
                WHERE "IsDeleted" = FALSE;

            CREATE INDEX IF NOT EXISTS "IX_BranchProductUnitPrices_ProductUnitId"
                ON "BranchProductUnitPrices" ("ProductUnitId");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP INDEX IF EXISTS "IX_BranchProductUnitPrices_ProductUnitId";
            DROP INDEX IF EXISTS "IX_BranchProductUnitPrices_BranchProductId_ProductUnitId";
            DROP TABLE IF EXISTS "BranchProductUnitPrices";
            """);
    }
}
