using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace POS.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddIsOrphanedFlagToOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsOrphaned",
                table: "Orders",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsOrphaned",
                table: "Orders");
        }
    }
}
