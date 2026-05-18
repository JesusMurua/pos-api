using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace POS.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddProductTypeEnum : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "Products",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Standard");

            // Backfill classification from existing inferred signals.
            // PascalCase JSON keys match EF Core 9 OwnsOne(...).ToJson() storage.
            // Order is MECE: Membership > Service > Recipe > TrackedByWeight > TrackedByUnit.
            // Each step filters `Type = 'Standard'` so the chain is re-runnable.
            migrationBuilder.Sql(@"
                UPDATE ""Products""
                SET ""Type"" = 'Membership'
                WHERE ""Type"" = 'Standard'
                  AND ""Metadata""->>'MembershipDurationDays' IS NOT NULL
                  AND (""Metadata""->>'MembershipDurationDays')::int > 0;

                UPDATE ""Products""
                SET ""Type"" = 'Service'
                WHERE ""Type"" = 'Standard'
                  AND ""Metadata""->>'ServiceDurationMinutes' IS NOT NULL
                  AND (""Metadata""->>'ServiceDurationMinutes')::int > 0;

                UPDATE ""Products""
                SET ""Type"" = 'Recipe'
                WHERE ""Type"" = 'Standard'
                  AND EXISTS (
                      SELECT 1 FROM ""ProductConsumptions""
                      WHERE ""ProductConsumptions"".""ProductId"" = ""Products"".""Id""
                  );

                UPDATE ""Products""
                SET ""Type"" = 'TrackedByWeight'
                WHERE ""Type"" = 'Standard'
                  AND ""TrackStock"" = true
                  AND ""Metadata""->>'IsSoldByWeight' = 'true';

                UPDATE ""Products""
                SET ""Type"" = 'TrackedByUnit'
                WHERE ""Type"" = 'Standard'
                  AND ""TrackStock"" = true;
            ");

            migrationBuilder.CreateIndex(
                name: "IX_Products_Type",
                table: "Products",
                column: "Type");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Products_Type",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "Products");
        }
    }
}
