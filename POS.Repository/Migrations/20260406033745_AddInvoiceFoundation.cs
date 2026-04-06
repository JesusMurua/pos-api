using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace POS.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "InvoiceId",
                table: "Orders",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SatProductCode",
                table: "OrderItems",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SatUnitCode",
                table: "OrderItems",
                type: "character varying(5)",
                maxLength: 5,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TaxAmountCents",
                table: "OrderItems",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "TaxRatePercent",
                table: "OrderItems",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CurrencyUnitsPerPoint",
                table: "Businesses",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "LoyaltyEnabled",
                table: "Businesses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "PointRedemptionValueCents",
                table: "Businesses",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PointsPerCurrencyUnit",
                table: "Businesses",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "Invoices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BusinessId = table.Column<int>(type: "integer", nullable: false),
                    BranchId = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    FacturapiId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    FiscalCustomerId = table.Column<int>(type: "integer", nullable: true),
                    Series = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    FolioNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    TotalCents = table.Column<int>(type: "integer", nullable: false),
                    SubtotalCents = table.Column<int>(type: "integer", nullable: false),
                    TaxCents = table.Column<int>(type: "integer", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    PaymentForm = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    PaymentMethod = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    PdfUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    XmlUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CancellationReason = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    IssuedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Invoices_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Invoices_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Invoices_FiscalCustomers_FiscalCustomerId",
                        column: x => x.FiscalCustomerId,
                        principalTable: "FiscalCustomers",
                        principalColumn: "Id");
                });

            migrationBuilder.UpdateData(
                table: "Businesses",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "CurrencyUnitsPerPoint", "LoyaltyEnabled", "PointRedemptionValueCents", "PointsPerCurrencyUnit" },
                values: new object[] { 1000, false, 10, 1 });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_InvoiceId",
                table: "Orders",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_BranchId",
                table: "Invoices",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_BusinessId",
                table: "Invoices",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_FiscalCustomerId",
                table: "Invoices",
                column: "FiscalCustomerId");

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_Invoices_InvoiceId",
                table: "Orders",
                column: "InvoiceId",
                principalTable: "Invoices",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Orders_Invoices_InvoiceId",
                table: "Orders");

            migrationBuilder.DropTable(
                name: "Invoices");

            migrationBuilder.DropIndex(
                name: "IX_Orders_InvoiceId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "InvoiceId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "SatProductCode",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "SatUnitCode",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "TaxAmountCents",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "TaxRatePercent",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "CurrencyUnitsPerPoint",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "LoyaltyEnabled",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "PointRedemptionValueCents",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "PointsPerCurrencyUnit",
                table: "Businesses");
        }
    }
}
