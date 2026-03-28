using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace POS.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddZoneIdToRestaurantTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ZoneId",
                table: "RestaurantTables",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantTables_ZoneId",
                table: "RestaurantTables",
                column: "ZoneId");

            migrationBuilder.AddForeignKey(
                name: "FK_RestaurantTables_Zones_ZoneId",
                table: "RestaurantTables",
                column: "ZoneId",
                principalTable: "Zones",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RestaurantTables_Zones_ZoneId",
                table: "RestaurantTables");

            migrationBuilder.DropIndex(
                name: "IX_RestaurantTables_ZoneId",
                table: "RestaurantTables");

            migrationBuilder.DropColumn(
                name: "ZoneId",
                table: "RestaurantTables");
        }
    }
}
