using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace POS.Repository.Migrations
{
    /// <inheritdoc />
    public partial class EnforceRelationalTaxEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Step 1: Wipe + reset sequence + insert with explicit Ids 1-4. ──
            // The dev DB had runtime-seeded Tax rows (from DbInitializer's old code path)
            // whose autoincremented Ids drifted — InsertData would PK-conflict. We have
            // zero users, so a clean slate is correct: DELETE cascades to ProductTax
            // (associations wiped) and SetNulls OrderItemTax.TaxId (history preserved).
            migrationBuilder.Sql(@"
                DELETE FROM ""Taxes"";
                ALTER SEQUENCE ""Taxes_Id_seq"" RESTART WITH 1;
                INSERT INTO ""Taxes"" (""Id"", ""Code"", ""CountryCode"", ""IsDefault"", ""Name"", ""Rate"") VALUES
                    (1, '002', 'MX', TRUE,  'IVA 16%', 0.16),
                    (2, '002', 'MX', FALSE, 'IVA 8%',  0.08),
                    (3, '002', 'MX', FALSE, 'IVA 0%',  0.00),
                    (4, '003', 'MX', FALSE, 'IEPS 8%', 0.08);
            ");

            // ── Step 2: Add Businesses.DefaultTaxId as NULLABLE so existing rows are accepted. ──
            migrationBuilder.AddColumn<int>(
                name: "DefaultTaxId",
                table: "Businesses",
                type: "integer",
                nullable: true);

            // ── Step 3: Backfill — point every existing business at its country's default tax. ──
            migrationBuilder.Sql(@"
                UPDATE ""Businesses""
                SET ""DefaultTaxId"" = (
                    SELECT ""Id"" FROM ""Taxes""
                    WHERE ""IsDefault"" = TRUE AND ""CountryCode"" = ""Businesses"".""CountryCode""
                    LIMIT 1
                )
                WHERE ""DefaultTaxId"" IS NULL;
            ");

            // ── Step 4: Re-apply the seeded value for Business Id=1 in case the backfill picked a different row. ──
            migrationBuilder.UpdateData(
                table: "Businesses",
                keyColumn: "Id",
                keyValue: 1,
                column: "DefaultTaxId",
                value: 1);

            // ── Step 5: Guard — abort migration loudly if any business is still unresolved. ──
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM ""Businesses"" WHERE ""DefaultTaxId"" IS NULL) THEN
                        RAISE EXCEPTION 'Migration aborted: businesses exist with no default tax for their CountryCode. Seed Tax catalog for all countries before applying.';
                    END IF;
                END $$;
            ");

            // ── Step 6: Tighten to NOT NULL. No defaultValue — onboarding always supplies one explicitly. ──
            migrationBuilder.AlterColumn<int>(
                name: "DefaultTaxId",
                table: "Businesses",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            // ── Step 7: Index + FK after the data is consistent. ──
            migrationBuilder.CreateIndex(
                name: "IX_Businesses_DefaultTaxId",
                table: "Businesses",
                column: "DefaultTaxId");

            migrationBuilder.AddForeignKey(
                name: "FK_Businesses_Taxes_DefaultTaxId",
                table: "Businesses",
                column: "DefaultTaxId",
                principalTable: "Taxes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // ── Step 8: Purge the legacy scalar tax columns. OrderItem.AppliedTaxes is now the only source of truth. ──
            migrationBuilder.DropColumn(
                name: "TaxRate",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "TaxAmountCents",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "TaxRatePercent",
                table: "OrderItems");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Businesses_Taxes_DefaultTaxId",
                table: "Businesses");

            migrationBuilder.DropIndex(
                name: "IX_Businesses_DefaultTaxId",
                table: "Businesses");

            migrationBuilder.DeleteData(
                table: "Taxes",
                keyColumn: "Id",
                keyValue: 1);

            migrationBuilder.DeleteData(
                table: "Taxes",
                keyColumn: "Id",
                keyValue: 2);

            migrationBuilder.DeleteData(
                table: "Taxes",
                keyColumn: "Id",
                keyValue: 3);

            migrationBuilder.DeleteData(
                table: "Taxes",
                keyColumn: "Id",
                keyValue: 4);

            migrationBuilder.DropColumn(
                name: "DefaultTaxId",
                table: "Businesses");

            migrationBuilder.AddColumn<decimal>(
                name: "TaxRate",
                table: "Products",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
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
        }
    }
}
