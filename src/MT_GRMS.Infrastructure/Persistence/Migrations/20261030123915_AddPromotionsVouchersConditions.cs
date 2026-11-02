using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MT_GRMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPromotionsVouchersConditions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DO $$ BEGIN IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='HouseholdTaxSnapshots' AND column_name='AuditSignatureHash') THEN ALTER TABLE ""HouseholdTaxSnapshots"" RENAME COLUMN ""AuditSignatureHash"" TO ""AuditSignature""; END IF; END $$;");

            migrationBuilder.AddColumn<decimal>(
                name: "BudgetLimit",
                table: "Promotions",
                type: "numeric(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "Promotions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MaxDiscountAmount",
                table: "Promotions",
                type: "numeric(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MinOrderValue",
                table: "Promotions",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0.00m);

            migrationBuilder.AddColumn<string>(
                name: "TargetBranches",
                table: "Promotions",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "ALL");

            migrationBuilder.AddColumn<decimal>(
                name: "TotalDiscountGiven",
                table: "Promotions",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0.00m);

            migrationBuilder.AddColumn<int>(
                name: "TotalVoucherQuantity",
                table: "Promotions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "UsageLimit",
                table: "Promotions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UsedCount",
                table: "Promotions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "VoucherPrefix",
                table: "Promotions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "AuditSignature",
                table: "HouseholdTaxSnapshots",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128,
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "PromotionConditions",
                columns: table => new
                {
                    ConditionID = table.Column<Guid>(type: "uuid", nullable: false),
                    PromotionID = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductID = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductRole = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "BUY"),
                    MinQty = table.Column<decimal>(type: "numeric(12,2)", nullable: false, defaultValue: 1.00m),
                    DiscountValue = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    Note = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    TenantID = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromotionConditions", x => x.ConditionID);
                    table.ForeignKey(
                        name: "FK_PromotionConditions_Products_ProductID",
                        column: x => x.ProductID,
                        principalTable: "Products",
                        principalColumn: "ProductID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PromotionConditions_Promotions_PromotionID",
                        column: x => x.PromotionID,
                        principalTable: "Promotions",
                        principalColumn: "PromotionID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VoucherCodes",
                columns: table => new
                {
                    VoucherCodeID = table.Column<Guid>(type: "uuid", nullable: false),
                    PromotionID = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "UNUSED"),
                    CustomerID = table.Column<Guid>(type: "uuid", nullable: true),
                    CustomerName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CustomerPhone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    OrderID = table.Column<Guid>(type: "uuid", nullable: true),
                    OrderCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    UsedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    TenantID = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VoucherCodes", x => x.VoucherCodeID);
                    table.ForeignKey(
                        name: "FK_VoucherCodes_Promotions_PromotionID",
                        column: x => x.PromotionID,
                        principalTable: "Promotions",
                        principalColumn: "PromotionID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PromotionConditions_ProductID",
                table: "PromotionConditions",
                column: "ProductID");

            migrationBuilder.CreateIndex(
                name: "IX_PromotionConditions_PromotionID",
                table: "PromotionConditions",
                column: "PromotionID");

            migrationBuilder.CreateIndex(
                name: "IX_PromotionConditions_TenantID_PromotionID_ProductID_ProductR~",
                table: "PromotionConditions",
                columns: new[] { "TenantID", "PromotionID", "ProductID", "ProductRole" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VoucherCodes_PromotionID",
                table: "VoucherCodes",
                column: "PromotionID");

            migrationBuilder.CreateIndex(
                name: "IX_VoucherCodes_TenantID_Code",
                table: "VoucherCodes",
                columns: new[] { "TenantID", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VoucherCodes_TenantID_PromotionID_Status",
                table: "VoucherCodes",
                columns: new[] { "TenantID", "PromotionID", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PromotionConditions");

            migrationBuilder.DropTable(
                name: "VoucherCodes");

            migrationBuilder.DropColumn(
                name: "BudgetLimit",
                table: "Promotions");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "Promotions");

            migrationBuilder.DropColumn(
                name: "MaxDiscountAmount",
                table: "Promotions");

            migrationBuilder.DropColumn(
                name: "MinOrderValue",
                table: "Promotions");

            migrationBuilder.DropColumn(
                name: "TargetBranches",
                table: "Promotions");

            migrationBuilder.DropColumn(
                name: "TotalDiscountGiven",
                table: "Promotions");

            migrationBuilder.DropColumn(
                name: "TotalVoucherQuantity",
                table: "Promotions");

            migrationBuilder.DropColumn(
                name: "UsageLimit",
                table: "Promotions");

            migrationBuilder.DropColumn(
                name: "UsedCount",
                table: "Promotions");

            migrationBuilder.DropColumn(
                name: "VoucherPrefix",
                table: "Promotions");

            migrationBuilder.RenameColumn(
                name: "AuditSignature",
                table: "HouseholdTaxSnapshots",
                newName: "AuditSignatureHash");

            migrationBuilder.AlterColumn<string>(
                name: "AuditSignatureHash",
                table: "HouseholdTaxSnapshots",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256,
                oldNullable: true);
        }
    }
}
