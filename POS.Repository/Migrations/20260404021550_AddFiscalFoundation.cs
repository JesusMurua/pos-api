using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace POS.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddFiscalFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SatProductCode",
                table: "Products",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SatUnitCode",
                table: "Products",
                type: "character varying(5)",
                maxLength: 5,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TaxRate",
                table: "Products",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FacturapiId",
                table: "Orders",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FiscalCustomerId",
                table: "Orders",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvoiceStatus",
                table: "Orders",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<string>(
                name: "InvoiceUrl",
                table: "Orders",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "InvoicedAt",
                table: "Orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FacturapiOrganizationId",
                table: "Businesses",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "InvoicingEnabled",
                table: "Businesses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LegalName",
                table: "Businesses",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Rfc",
                table: "Businesses",
                type: "character varying(13)",
                maxLength: 13,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TaxRegime",
                table: "Businesses",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FiscalZipCode",
                table: "Branches",
                type: "character varying(5)",
                maxLength: 5,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FiscalCustomers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BusinessId = table.Column<int>(type: "integer", nullable: false),
                    Rfc = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: false),
                    BusinessName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    TaxRegime = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    ZipCode = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    Email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    CfdiUse = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true),
                    FacturapiCustomerId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FiscalCustomers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FiscalCustomers_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "Branches",
                keyColumn: "Id",
                keyValue: 1,
                column: "FiscalZipCode",
                value: null);

            migrationBuilder.UpdateData(
                table: "Businesses",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "FacturapiOrganizationId", "LegalName", "Rfc", "TaxRegime" },
                values: new object[] { null, null, null, null });

            migrationBuilder.UpdateData(
                table: "Products",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "SatProductCode", "SatUnitCode", "TaxRate" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "Products",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "SatProductCode", "SatUnitCode", "TaxRate" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "Products",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "SatProductCode", "SatUnitCode", "TaxRate" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "Products",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "SatProductCode", "SatUnitCode", "TaxRate" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "Products",
                keyColumn: "Id",
                keyValue: 5,
                columns: new[] { "SatProductCode", "SatUnitCode", "TaxRate" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "Products",
                keyColumn: "Id",
                keyValue: 6,
                columns: new[] { "SatProductCode", "SatUnitCode", "TaxRate" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "Products",
                keyColumn: "Id",
                keyValue: 7,
                columns: new[] { "SatProductCode", "SatUnitCode", "TaxRate" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "Products",
                keyColumn: "Id",
                keyValue: 8,
                columns: new[] { "SatProductCode", "SatUnitCode", "TaxRate" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "Products",
                keyColumn: "Id",
                keyValue: 9,
                columns: new[] { "SatProductCode", "SatUnitCode", "TaxRate" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "Products",
                keyColumn: "Id",
                keyValue: 10,
                columns: new[] { "SatProductCode", "SatUnitCode", "TaxRate" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "Products",
                keyColumn: "Id",
                keyValue: 11,
                columns: new[] { "SatProductCode", "SatUnitCode", "TaxRate" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "Products",
                keyColumn: "Id",
                keyValue: 12,
                columns: new[] { "SatProductCode", "SatUnitCode", "TaxRate" },
                values: new object[] { null, null, null });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_FacturapiId",
                table: "Orders",
                column: "FacturapiId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_FiscalCustomerId",
                table: "Orders",
                column: "FiscalCustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_FiscalCustomers_BusinessId_Rfc",
                table: "FiscalCustomers",
                columns: new[] { "BusinessId", "Rfc" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_FiscalCustomers_FiscalCustomerId",
                table: "Orders",
                column: "FiscalCustomerId",
                principalTable: "FiscalCustomers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Orders_FiscalCustomers_FiscalCustomerId",
                table: "Orders");

            migrationBuilder.DropTable(
                name: "FiscalCustomers");

            migrationBuilder.DropIndex(
                name: "IX_Orders_FacturapiId",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_FiscalCustomerId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "SatProductCode",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "SatUnitCode",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "TaxRate",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "FacturapiId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "FiscalCustomerId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "InvoiceStatus",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "InvoiceUrl",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "InvoicedAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "FacturapiOrganizationId",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "InvoicingEnabled",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "LegalName",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "Rfc",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "TaxRegime",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "FiscalZipCode",
                table: "Branches");
        }
    }
}
