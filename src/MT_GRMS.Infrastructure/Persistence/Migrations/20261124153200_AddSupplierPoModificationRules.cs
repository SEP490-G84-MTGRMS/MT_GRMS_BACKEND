using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MT_GRMS.Infrastructure.Persistence;

#nullable disable

namespace MT_GRMS.Infrastructure.Persistence.Migrations;

/// <summary>
/// Thêm 4 cột PO Modification Rules (AllowStoreEdit, EditWindowType, HoursBeforeDelivery, HoursAfterOrder)
/// vào bảng Suppliers (SUP-02). Idempotent.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261124153200_AddSupplierPoModificationRules")]
public partial class AddSupplierPoModificationRules : Migration
{
    internal const string UpSql = """
        ALTER TABLE IF EXISTS "Suppliers"
          ADD COLUMN IF NOT EXISTS "AllowStoreEdit" boolean NOT NULL DEFAULT true,
          ADD COLUMN IF NOT EXISTS "EditWindowType" character varying(50) NOT NULL DEFAULT 'BEFORE_CUTOFF',
          ADD COLUMN IF NOT EXISTS "HoursBeforeDelivery" integer NOT NULL DEFAULT 24,
          ADD COLUMN IF NOT EXISTS "HoursAfterOrder" integer NOT NULL DEFAULT 2;
        """;

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(UpSql);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE IF EXISTS "Suppliers"
          DROP COLUMN IF EXISTS "AllowStoreEdit",
          DROP COLUMN IF EXISTS "EditWindowType",
          DROP COLUMN IF EXISTS "HoursBeforeDelivery",
          DROP COLUMN IF EXISTS "HoursAfterOrder";
        """);
}
