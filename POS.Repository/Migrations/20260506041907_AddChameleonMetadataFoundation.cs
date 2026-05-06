using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace POS.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddChameleonMetadataFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Products",
                keyColumn: "Id",
                keyValue: 1);

            migrationBuilder.DeleteData(
                table: "Products",
                keyColumn: "Id",
                keyValue: 2);

            migrationBuilder.DeleteData(
                table: "Products",
                keyColumn: "Id",
                keyValue: 3);

            migrationBuilder.DeleteData(
                table: "Products",
                keyColumn: "Id",
                keyValue: 4);

            migrationBuilder.DeleteData(
                table: "Products",
                keyColumn: "Id",
                keyValue: 5);

            migrationBuilder.DeleteData(
                table: "Products",
                keyColumn: "Id",
                keyValue: 6);

            migrationBuilder.DeleteData(
                table: "Products",
                keyColumn: "Id",
                keyValue: 7);

            migrationBuilder.DeleteData(
                table: "Products",
                keyColumn: "Id",
                keyValue: 8);

            migrationBuilder.DeleteData(
                table: "Products",
                keyColumn: "Id",
                keyValue: 9);

            migrationBuilder.DeleteData(
                table: "Products",
                keyColumn: "Id",
                keyValue: 10);

            migrationBuilder.DeleteData(
                table: "Products",
                keyColumn: "Id",
                keyValue: 11);

            migrationBuilder.DeleteData(
                table: "Products",
                keyColumn: "Id",
                keyValue: 12);

            // Postgres rejects ''::jsonb and does not auto-cast text→jsonb. Normalize empty
            // strings to NULL and force an explicit USING expression so the conversion is
            // safe across dev databases (no production data per the corrective contract).
            migrationBuilder.Sql(@"
                UPDATE ""Products"" SET ""Metadata"" = NULL WHERE ""Metadata"" = '';
                ALTER TABLE ""Products"" ALTER COLUMN ""Metadata"" TYPE jsonb USING ""Metadata""::jsonb;
            ");

            migrationBuilder.AddColumn<JsonDocument>(
                name: "ExtensionData",
                table: "Products",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<JsonDocument>(
                name: "ExtensionData",
                table: "Orders",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Metadata",
                table: "Orders",
                type: "jsonb",
                nullable: true);

            migrationBuilder.Sql(@"
                UPDATE ""OrderPayments"" SET ""PaymentMetadata"" = NULL WHERE ""PaymentMetadata"" = '';
                ALTER TABLE ""OrderPayments"" ALTER COLUMN ""PaymentMetadata"" TYPE jsonb USING ""PaymentMetadata""::jsonb;
            ");

            migrationBuilder.AddColumn<JsonDocument>(
                name: "ExtensionData",
                table: "OrderPayments",
                type: "jsonb",
                nullable: true);

            migrationBuilder.Sql(@"
                UPDATE ""OrderItems"" SET ""Metadata"" = NULL WHERE ""Metadata"" = '';
                ALTER TABLE ""OrderItems"" ALTER COLUMN ""Metadata"" TYPE jsonb USING ""Metadata""::jsonb;
            ");

            migrationBuilder.AddColumn<JsonDocument>(
                name: "ExtensionData",
                table: "OrderItems",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<JsonDocument>(
                name: "ExtensionData",
                table: "Customers",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Metadata",
                table: "Customers",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExtensionData",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ExtensionData",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "Metadata",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ExtensionData",
                table: "OrderPayments");

            migrationBuilder.DropColumn(
                name: "ExtensionData",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "ExtensionData",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "Metadata",
                table: "Customers");

            migrationBuilder.AlterColumn<string>(
                name: "Metadata",
                table: "Products",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "PaymentMetadata",
                table: "OrderPayments",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Metadata",
                table: "OrderItems",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldNullable: true);

            migrationBuilder.InsertData(
                table: "Products",
                columns: new[] { "Id", "Barcode", "BranchId", "CategoryId", "Description", "ImageUrl", "IsAvailable", "IsPopular", "IsTaxIncluded", "Metadata", "Name", "PriceCents", "PrintingDestination", "SatProductCode", "SatUnitCode" },
                values: new object[,]
                {
                    { 1, null, 1, 1, null, null, true, false, true, null, "Torta de Milanesa", 8500, 0, null, null },
                    { 2, null, 1, 1, null, null, true, false, true, null, "Quesadilla", 5500, 0, null, null },
                    { 3, null, 1, 1, null, null, true, false, true, null, "Enchiladas Verdes", 7500, 0, null, null },
                    { 4, null, 1, 1, null, null, true, false, true, null, "Pozole Rojo", 9000, 0, null, null },
                    { 5, null, 1, 2, null, null, true, false, true, null, "Taco de Canasta", 2000, 0, null, null },
                    { 6, null, 1, 2, null, null, true, false, true, null, "Gordita", 3500, 0, null, null },
                    { 7, null, 1, 2, null, null, true, false, true, null, "Tostada de Tinga", 3000, 0, null, null },
                    { 8, null, 1, 3, null, null, true, false, true, null, "Agua de Jamaica", 2500, 0, null, null },
                    { 9, null, 1, 3, null, null, true, false, true, null, "Café de Olla", 3000, 0, null, null },
                    { 10, null, 1, 3, null, null, true, false, true, null, "Refresco", 2500, 0, null, null },
                    { 11, null, 1, 4, null, null, true, false, true, null, "Arroz con Leche", 4000, 0, null, null },
                    { 12, null, 1, 4, null, null, true, false, true, null, "Gelatina", 2500, 0, null, null }
                });
        }
    }
}
