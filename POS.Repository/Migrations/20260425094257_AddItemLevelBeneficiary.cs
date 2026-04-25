using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace POS.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddItemLevelBeneficiary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Metadata",
                table: "OrderItems",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Metadata",
                table: "OrderItems");
        }
    }
}
