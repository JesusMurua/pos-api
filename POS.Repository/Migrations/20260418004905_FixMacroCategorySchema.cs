using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace POS.Repository.Migrations
{
    /// <inheritdoc />
    public partial class FixMacroCategorySchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HasKitchen",
                table: "MacroCategories",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasTables",
                table: "MacroCategories",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PosExperience",
                table: "MacroCategories",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HasKitchen",
                table: "MacroCategories");

            migrationBuilder.DropColumn(
                name: "HasTables",
                table: "MacroCategories");

            migrationBuilder.DropColumn(
                name: "PosExperience",
                table: "MacroCategories");
        }
    }
}
