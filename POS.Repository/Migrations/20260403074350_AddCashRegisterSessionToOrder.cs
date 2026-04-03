using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace POS.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddCashRegisterSessionToOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CashRegisterSessionId",
                table: "Orders",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CashRegisterSessionId",
                table: "Orders",
                column: "CashRegisterSessionId");

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_CashRegisterSessions_CashRegisterSessionId",
                table: "Orders",
                column: "CashRegisterSessionId",
                principalTable: "CashRegisterSessions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Orders_CashRegisterSessions_CashRegisterSessionId",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_CashRegisterSessionId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "CashRegisterSessionId",
                table: "Orders");
        }
    }
}
