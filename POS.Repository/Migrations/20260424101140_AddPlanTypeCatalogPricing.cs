using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace POS.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanTypeCatalogPricing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "PlanTypeCatalogs",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "MXN");

            migrationBuilder.AddColumn<decimal>(
                name: "MonthlyPrice",
                table: "PlanTypeCatalogs",
                type: "numeric(10,2)",
                nullable: true);

            // Backfill the canonical public prices so any environment running
            // this migration without DbInitializer still has correct data.
            // Enterprise stays NULL ("contact sales") until marketing confirms.
            migrationBuilder.Sql("""
                UPDATE "PlanTypeCatalogs" SET "MonthlyPrice" = 0,   "Currency" = 'MXN' WHERE "Id" = 1;
                UPDATE "PlanTypeCatalogs" SET "MonthlyPrice" = 149, "Currency" = 'MXN' WHERE "Id" = 2;
                UPDATE "PlanTypeCatalogs" SET "MonthlyPrice" = 349, "Currency" = 'MXN' WHERE "Id" = 3;
                UPDATE "PlanTypeCatalogs" SET "MonthlyPrice" = NULL,"Currency" = 'MXN' WHERE "Id" = 4;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Currency",
                table: "PlanTypeCatalogs");

            migrationBuilder.DropColumn(
                name: "MonthlyPrice",
                table: "PlanTypeCatalogs");
        }
    }
}
