using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MT_GRMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplierModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Category",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoryCode = table.Column<string>(type: "text", nullable: false),
                    CategoryName = table.Column<string>(type: "text", nullable: false),
                    ParentCategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Category", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "supplier_categories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoryCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CategoryName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_categories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "products",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SKU = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Barcode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ProductName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    BasePrice = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    CostPrice = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    UnitName = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "Cái"),
                    HasBatches = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_products", x => x.Id);
                    table.ForeignKey(
                        name: "FK_products_Category_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Category",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "suppliers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    TradeName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    SupplierType = table.Column<int>(type: "integer", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    CategoryCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "FRESH"),
                    ContactPerson = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Email = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    Address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PaymentTermDays = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CreditLimit = table.Column<decimal>(type: "numeric(18,2)", nullable: false, defaultValue: 0m),
                    CurrentDebt = table.Column<decimal>(type: "numeric(18,2)", nullable: false, defaultValue: 0m),
                    OverdueDebt = table.Column<decimal>(type: "numeric(18,2)", nullable: false, defaultValue: 0m),
                    Rating = table.Column<decimal>(type: "numeric(3,2)", nullable: false, defaultValue: 5.0m),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    LinkedCustomerId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TaxCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CompanyName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    InvoiceAddress = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    BankName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    BankAccount = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    AccountHolder = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_suppliers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_suppliers_supplier_categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "supplier_categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "supplier_ap_ledgers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DocType = table.Column<int>(type: "integer", nullable: false),
                    DocNo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Direction = table.Column<int>(type: "integer", nullable: false),
                    BalanceAfter = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    CreatedByName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_ap_ledgers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_supplier_ap_ledgers_suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "supplier_delivery_configs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    LeadTimeHours = table.Column<int>(type: "integer", nullable: false),
                    CutoffTime = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, defaultValue: "17:00"),
                    DeliveryWeekdaysMask = table.Column<int>(type: "integer", nullable: false),
                    DeliveryDaysJson = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false, defaultValue: "[\"Mon\",\"Tue\",\"Wed\",\"Thu\",\"Fri\",\"Sat\"]"),
                    MinOrderValue = table.Column<decimal>(type: "numeric(18,2)", nullable: false, defaultValue: 0m),
                    MinOrderQuantity = table.Column<int>(type: "integer", nullable: false),
                    AllowUrgent = table.Column<bool>(type: "boolean", nullable: false),
                    UrgentLeadTimeHours = table.Column<int>(type: "integer", nullable: false),
                    UrgentFeePercent = table.Column<decimal>(type: "numeric(5,2)", nullable: false, defaultValue: 0m),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_delivery_configs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_supplier_delivery_configs_suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "supplier_blackout_dates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierDeliveryConfigId = table.Column<Guid>(type: "uuid", nullable: true),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    IsRecurringAnnual = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_blackout_dates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_supplier_blackout_dates_supplier_delivery_configs_SupplierD~",
                        column: x => x.SupplierDeliveryConfigId,
                        principalTable: "supplier_delivery_configs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_supplier_blackout_dates_suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_products_CategoryId",
                table: "products",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_products_TenantId_Barcode",
                table: "products",
                columns: new[] { "TenantId", "Barcode" });

            migrationBuilder.CreateIndex(
                name: "IX_products_TenantId_SKU",
                table: "products",
                columns: new[] { "TenantId", "SKU" });

            migrationBuilder.CreateIndex(
                name: "IX_supplier_ap_ledgers_SupplierId",
                table: "supplier_ap_ledgers",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_ap_ledgers_TenantId_IdempotencyKey",
                table: "supplier_ap_ledgers",
                columns: new[] { "TenantId", "IdempotencyKey" },
                unique: true,
                filter: "\"IdempotencyKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_ap_ledgers_TenantId_SupplierId_TransactionDate",
                table: "supplier_ap_ledgers",
                columns: new[] { "TenantId", "SupplierId", "TransactionDate" });

            migrationBuilder.CreateIndex(
                name: "IX_supplier_blackout_dates_SupplierDeliveryConfigId",
                table: "supplier_blackout_dates",
                column: "SupplierDeliveryConfigId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_blackout_dates_SupplierId",
                table: "supplier_blackout_dates",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_blackout_dates_TenantId_SupplierId_StartDate_EndDa~",
                table: "supplier_blackout_dates",
                columns: new[] { "TenantId", "SupplierId", "StartDate", "EndDate" });

            migrationBuilder.CreateIndex(
                name: "IX_supplier_categories_TenantId_CategoryCode",
                table: "supplier_categories",
                columns: new[] { "TenantId", "CategoryCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_supplier_delivery_configs_SupplierId",
                table: "supplier_delivery_configs",
                column: "SupplierId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_supplier_delivery_configs_TenantId_SupplierId",
                table: "supplier_delivery_configs",
                columns: new[] { "TenantId", "SupplierId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_suppliers_CategoryId",
                table: "suppliers",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_suppliers_TenantId_Code",
                table: "suppliers",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_suppliers_TenantId_Phone",
                table: "suppliers",
                columns: new[] { "TenantId", "Phone" });

            migrationBuilder.CreateIndex(
                name: "IX_suppliers_TenantId_Status",
                table: "suppliers",
                columns: new[] { "TenantId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "products");

            migrationBuilder.DropTable(
                name: "supplier_ap_ledgers");

            migrationBuilder.DropTable(
                name: "supplier_blackout_dates");

            migrationBuilder.DropTable(
                name: "Category");

            migrationBuilder.DropTable(
                name: "supplier_delivery_configs");

            migrationBuilder.DropTable(
                name: "suppliers");

            migrationBuilder.DropTable(
                name: "supplier_categories");
        }
    }
}
