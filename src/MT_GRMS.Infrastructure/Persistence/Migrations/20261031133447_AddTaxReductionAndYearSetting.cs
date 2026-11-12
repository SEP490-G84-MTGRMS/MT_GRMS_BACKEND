using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MT_GRMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTaxReductionAndYearSetting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                -- 1. Thêm cột vào Promotions nếu chưa có
                ALTER TABLE ""Promotions"" ADD COLUMN IF NOT EXISTS ""BudgetLimit"" numeric(18,2);
                ALTER TABLE ""Promotions"" ADD COLUMN IF NOT EXISTS ""Description"" character varying(500);
                ALTER TABLE ""Promotions"" ADD COLUMN IF NOT EXISTS ""MaxDiscountAmount"" numeric(18,2);
                ALTER TABLE ""Promotions"" ADD COLUMN IF NOT EXISTS ""MinOrderValue"" numeric(18,2) NOT NULL DEFAULT 0.00;
                ALTER TABLE ""Promotions"" ADD COLUMN IF NOT EXISTS ""TargetBranches"" character varying(255) NOT NULL DEFAULT 'ALL';
                ALTER TABLE ""Promotions"" ADD COLUMN IF NOT EXISTS ""TotalDiscountGiven"" numeric(18,2) NOT NULL DEFAULT 0.00;
                ALTER TABLE ""Promotions"" ADD COLUMN IF NOT EXISTS ""TotalVoucherQuantity"" integer NOT NULL DEFAULT 0;
                ALTER TABLE ""Promotions"" ADD COLUMN IF NOT EXISTS ""UsageLimit"" integer;
                ALTER TABLE ""Promotions"" ADD COLUMN IF NOT EXISTS ""UsedCount"" integer NOT NULL DEFAULT 0;
                ALTER TABLE ""Promotions"" ADD COLUMN IF NOT EXISTS ""VoucherPrefix"" character varying(20);

                -- 2. Thêm EligibleForVatReduction vào Products
                ALTER TABLE ""Products"" ADD COLUMN IF NOT EXISTS ""EligibleForVatReduction"" boolean NOT NULL DEFAULT false;

                -- 3. IdempotencyRecords & HouseholdTaxSnapshots
                ALTER TABLE ""IdempotencyRecords"" ADD COLUMN IF NOT EXISTS ""Status"" character varying(20) NOT NULL DEFAULT 'PENDING';
                ALTER TABLE ""IdempotencyRecords"" ADD COLUMN IF NOT EXISTS ""UserId"" uuid;
                ALTER TABLE ""HouseholdTaxSnapshots"" ADD COLUMN IF NOT EXISTS ""AppliedPitMethod"" integer;

                -- 4. Bảng Batches
                CREATE TABLE IF NOT EXISTS ""Batches"" (
                    ""Id"" uuid NOT NULL,
                    ""ProductId"" uuid NOT NULL,
                    ""BranchId"" uuid NOT NULL,
                    ""BatchCode"" text NOT NULL,
                    ""ManufactureDate"" timestamp without time zone NOT NULL,
                    ""ExpiryDate"" timestamp without time zone NOT NULL,
                    ""InitialQuantity"" numeric NOT NULL,
                    ""LandedCostPerUnit"" numeric NOT NULL,
                    ""CreatedAt"" timestamp without time zone NOT NULL,
                    ""UpdatedAt"" timestamp without time zone,
                    ""IsDeleted"" boolean NOT NULL,
                    ""TenantId"" text NOT NULL,
                    ""CreatedBy"" uuid,
                    CONSTRAINT ""PK_Batches"" PRIMARY KEY (""Id"")
                );

                -- 5. Bảng HouseholdTaxYearSettings
                CREATE TABLE IF NOT EXISTS ""HouseholdTaxYearSettings"" (
                    ""Id"" uuid NOT NULL,
                    ""Year"" integer NOT NULL,
                    ""PitMethod"" character varying(50) NOT NULL,
                    ""LockedUntilYear"" integer NOT NULL,
                    ""LockReason"" character varying(100) NOT NULL,
                    ""LockedAtUtc"" timestamp without time zone NOT NULL,
                    ""LockedBySystem"" character varying(100) NOT NULL,
                    ""CreatedAt"" timestamp without time zone NOT NULL,
                    ""UpdatedAt"" timestamp without time zone,
                    ""IsDeleted"" boolean NOT NULL,
                    ""TenantId"" character varying(50) NOT NULL,
                    ""CreatedBy"" uuid,
                    CONSTRAINT ""PK_HouseholdTaxYearSettings"" PRIMARY KEY (""Id"")
                );
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_HouseholdTaxYearSettings_TenantId_Year""
                    ON ""HouseholdTaxYearSettings"" (""TenantId"", ""Year"");

                -- 6. Bảng StockOnHands
                CREATE TABLE IF NOT EXISTS ""StockOnHands"" (
                    ""Id"" uuid NOT NULL,
                    ""BranchId"" uuid NOT NULL,
                    ""ProductId"" uuid NOT NULL,
                    ""BatchId"" uuid,
                    ""Quantity"" numeric NOT NULL,
                    ""ReservedQuantity"" numeric NOT NULL,
                    ""MinAlertThreshold"" numeric NOT NULL,
                    ""CreatedAt"" timestamp without time zone NOT NULL,
                    ""UpdatedAt"" timestamp without time zone,
                    ""IsDeleted"" boolean NOT NULL,
                    ""TenantId"" text NOT NULL,
                    ""CreatedBy"" uuid,
                    CONSTRAINT ""PK_StockOnHands"" PRIMARY KEY (""Id"")
                );

                -- 7. Bảng SupplierReturns
                CREATE TABLE IF NOT EXISTS ""SupplierReturns"" (
                    ""Id"" uuid NOT NULL,
                    ""Code"" text NOT NULL,
                    ""GoodsReceiptId"" uuid NOT NULL,
                    ""SupplierId"" uuid NOT NULL,
                    ""BranchId"" uuid NOT NULL,
                    ""Status"" text NOT NULL,
                    ""Reason"" text NOT NULL,
                    ""ReasonNotes"" text,
                    ""SupplierResponseNote"" text,
                    ""CancellationReason"" text,
                    ""AttachmentUrl"" text,
                    ""TotalRequestedQty"" numeric NOT NULL,
                    ""TotalApprovedQty"" numeric NOT NULL,
                    ""TotalHandedOverQty"" numeric NOT NULL,
                    ""TotalEstimatedAmount"" numeric NOT NULL,
                    ""SubmittedAt"" timestamp without time zone,
                    ""RespondedAt"" timestamp without time zone,
                    ""HandedOverAt"" timestamp without time zone,
                    ""HandedOverBy"" text,
                    ""CreatedAt"" timestamp without time zone NOT NULL,
                    ""UpdatedAt"" timestamp without time zone,
                    ""IsDeleted"" boolean NOT NULL,
                    ""TenantId"" text NOT NULL,
                    ""CreatedBy"" uuid,
                    CONSTRAINT ""PK_SupplierReturns"" PRIMARY KEY (""Id"")
                );

                -- 8. Bảng SupplierReturnLines
                CREATE TABLE IF NOT EXISTS ""SupplierReturnLines"" (
                    ""Id"" uuid NOT NULL,
                    ""SupplierReturnId"" uuid NOT NULL,
                    ""ProductId"" uuid NOT NULL,
                    ""GoodsReceiptLineId"" uuid,
                    ""BatchCode"" text,
                    ""RequestedQty"" numeric NOT NULL,
                    ""ApprovedQty"" numeric NOT NULL,
                    ""HandedOverQty"" numeric NOT NULL,
                    ""UnitCost"" numeric NOT NULL,
                    ""TotalAmount"" numeric NOT NULL DEFAULT 0,
                    ""ReturnReason"" text NOT NULL DEFAULT '',
                    ""CreatedAt"" timestamp without time zone NOT NULL,
                    ""UpdatedAt"" timestamp without time zone,
                    ""IsDeleted"" boolean NOT NULL,
                    ""TenantId"" text NOT NULL,
                    ""CreatedBy"" uuid,
                    CONSTRAINT ""PK_SupplierReturnLines"" PRIMARY KEY (""Id"")
                );

                CREATE INDEX IF NOT EXISTS ""IX_SupplierReturns_BranchId"" ON ""SupplierReturns"" (""BranchId"");
                CREATE INDEX IF NOT EXISTS ""IX_SupplierReturns_GoodsReceiptId"" ON ""SupplierReturns"" (""GoodsReceiptId"");
                CREATE INDEX IF NOT EXISTS ""IX_SupplierReturns_SupplierId"" ON ""SupplierReturns"" (""SupplierId"");
                CREATE INDEX IF NOT EXISTS ""IX_SupplierReturnLines_ProductId"" ON ""SupplierReturnLines"" (""ProductId"");
                CREATE INDEX IF NOT EXISTS ""IX_SupplierReturnLines_SupplierReturnId"" ON ""SupplierReturnLines"" (""SupplierReturnId"");
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SupplierReturnLines");

            migrationBuilder.DropTable(
                name: "SupplierReturns");

            migrationBuilder.DropTable(
                name: "StockOnHands");

            migrationBuilder.DropTable(
                name: "HouseholdTaxYearSettings");

            migrationBuilder.DropTable(
                name: "Batches");

            migrationBuilder.DropColumn(
                name: "AppliedPitMethod",
                table: "HouseholdTaxSnapshots");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "IdempotencyRecords");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "IdempotencyRecords");

            migrationBuilder.DropColumn(
                name: "EligibleForVatReduction",
                table: "Products");
        }
    }
}
