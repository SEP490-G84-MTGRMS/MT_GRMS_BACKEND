using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MT_GRMS.Infrastructure.Persistence;

#nullable disable

namespace MT_GRMS.Infrastructure.Persistence.Migrations;

/// <summary>
/// Đổi LandedUnitCost → PurchaseUnitCost; bỏ phân bổ cước Freight trên GRN.
/// Idempotent — chạy được nhiều lần trên DB cũ.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261122150000_RemoveGoodsReceiptFreight")]
public partial class RemoveGoodsReceiptFreight : Migration
{
    internal const string UpSql = """
        DO $$
        BEGIN
          IF EXISTS (
            SELECT 1 FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = 'GoodsReceiptLines' AND column_name = 'LandedUnitCost'
          ) AND NOT EXISTS (
            SELECT 1 FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = 'GoodsReceiptLines' AND column_name = 'PurchaseUnitCost'
          ) THEN
            ALTER TABLE "GoodsReceiptLines" RENAME COLUMN "LandedUnitCost" TO "PurchaseUnitCost";
          END IF;

          IF EXISTS (
            SELECT 1 FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = 'GoodsReceiptLines' AND column_name = 'LandedUnitCost'
          ) AND EXISTS (
            SELECT 1 FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = 'GoodsReceiptLines' AND column_name = 'PurchaseUnitCost'
          ) THEN
            UPDATE "GoodsReceiptLines"
            SET "PurchaseUnitCost" = "LandedUnitCost"
            WHERE COALESCE("PurchaseUnitCost", 0) = 0 AND COALESCE("LandedUnitCost", 0) <> 0;
            ALTER TABLE "GoodsReceiptLines" DROP COLUMN "LandedUnitCost";
          END IF;
        END $$;

        ALTER TABLE IF EXISTS "GoodsReceiptLines"
          ADD COLUMN IF NOT EXISTS "PurchaseUnitCost" numeric(18,2) NOT NULL DEFAULT 0;

        ALTER TABLE IF EXISTS "GoodsReceipts" DROP COLUMN IF EXISTS "FreightFee";
        ALTER TABLE IF EXISTS "GoodsReceiptLines" DROP COLUMN IF EXISTS "FreightAllocated";
        """;

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(UpSql);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE IF EXISTS "GoodsReceiptLines"
          ADD COLUMN IF NOT EXISTS "FreightAllocated" numeric(18,2) NOT NULL DEFAULT 0;
        ALTER TABLE IF EXISTS "GoodsReceipts"
          ADD COLUMN IF NOT EXISTS "FreightFee" numeric(18,2) NOT NULL DEFAULT 0;

        DO $$
        BEGIN
          IF EXISTS (
            SELECT 1 FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = 'GoodsReceiptLines' AND column_name = 'PurchaseUnitCost'
          ) AND NOT EXISTS (
            SELECT 1 FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = 'GoodsReceiptLines' AND column_name = 'LandedUnitCost'
          ) THEN
            ALTER TABLE "GoodsReceiptLines" RENAME COLUMN "PurchaseUnitCost" TO "LandedUnitCost";
          END IF;
        END $$;
        """);
}
