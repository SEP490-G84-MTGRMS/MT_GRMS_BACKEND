using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MT_GRMS.Infrastructure.Persistence.Migrations;

/// <summary>
/// Adds explicit, queryable tax classification to the immutable cashbook.
/// Existing manual entries deliberately remain ReviewRequired: free-text is
/// not a safe basis for an accounting or tax migration.
/// </summary>
public partial class AddCashbookTaxClassification : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "CategoryCode",
            table: "CashbookEntries",
            type: "character varying(50)",
            maxLength: 50,
            nullable: false,
            defaultValue: "REVIEW_REQUIRED");

        migrationBuilder.AddColumn<int>(
            name: "AuditSchemaVersion",
            table: "CashbookEntries",
            type: "integer",
            nullable: false,
            defaultValue: 1);

        migrationBuilder.AddColumn<int>(
            name: "FinancialNature",
            table: "CashbookEntries",
            type: "integer",
            nullable: false,
            defaultValue: 9);

        migrationBuilder.AddColumn<int>(
            name: "TaxTreatment",
            table: "CashbookEntries",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<string>(
            name: "TaxTreatmentReason",
            table: "CashbookEntries",
            type: "character varying(255)",
            maxLength: 255,
            nullable: true);

        migrationBuilder.Sql("""
            UPDATE "CashbookEntries"
            SET "CategoryCode" = "SourceType", "FinancialNature" = 7, "TaxTreatment" = 6
            WHERE "SourceType" IN ('INTERNAL_TRANSFER', 'CASH_DROP', 'BANK_DEPOSIT');

            UPDATE "CashbookEntries"
            SET "CategoryCode" = 'TAX_PAYMENT', "FinancialNature" = 8, "TaxTreatment" = 5
            WHERE "SourceType" = 'TAX_PAYMENT';

            UPDATE "CashbookEntries"
            SET "CategoryCode" = 'POS_SALE', "FinancialNature" = 4, "TaxTreatment" = 1
            WHERE "SourceType" IN ('POS', 'POS_SALE');

            UPDATE "CashbookEntries"
            SET "CategoryCode" = 'DEBT_COLLECTION', "FinancialNature" = 4, "TaxTreatment" = 2
            WHERE "SourceType" IN ('DEBT', 'DEBT_REPAYMENT');
            """);

        migrationBuilder.CreateIndex(
            name: "IX_CashbookEntries_TenantId_TaxTreatment_OccurredAtUtc",
            table: "CashbookEntries",
            columns: new[] { "TenantId", "TaxTreatment", "OccurredAtUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_CashbookEntries_TenantId_TaxTreatment_OccurredAtUtc",
            table: "CashbookEntries");
        migrationBuilder.DropColumn(name: "CategoryCode", table: "CashbookEntries");
        migrationBuilder.DropColumn(name: "AuditSchemaVersion", table: "CashbookEntries");
        migrationBuilder.DropColumn(name: "FinancialNature", table: "CashbookEntries");
        migrationBuilder.DropColumn(name: "TaxTreatment", table: "CashbookEntries");
        migrationBuilder.DropColumn(name: "TaxTreatmentReason", table: "CashbookEntries");
    }
}
