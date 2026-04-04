using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace POS.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryPhase18Fields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InventoryMovements_InventoryItems_InventoryItemId",
                table: "InventoryMovements");

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "InventoryMovements",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "StockAfterTransaction",
                table: "InventoryMovements",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "TransactionType",
                table: "InventoryMovements",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "UnitOfMeasure",
                table: "InventoryItems",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_TransactionType_CreatedAt",
                table: "InventoryMovements",
                columns: new[] { "TransactionType", "CreatedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryMovements_InventoryItems_InventoryItemId",
                table: "InventoryMovements",
                column: "InventoryItemId",
                principalTable: "InventoryItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InventoryMovements_InventoryItems_InventoryItemId",
                table: "InventoryMovements");

            migrationBuilder.DropIndex(
                name: "IX_InventoryMovements_TransactionType_CreatedAt",
                table: "InventoryMovements");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "InventoryMovements");

            migrationBuilder.DropColumn(
                name: "StockAfterTransaction",
                table: "InventoryMovements");

            migrationBuilder.DropColumn(
                name: "TransactionType",
                table: "InventoryMovements");

            migrationBuilder.DropColumn(
                name: "UnitOfMeasure",
                table: "InventoryItems");

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryMovements_InventoryItems_InventoryItemId",
                table: "InventoryMovements",
                column: "InventoryItemId",
                principalTable: "InventoryItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
