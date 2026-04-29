using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace POS.Repository.Migrations
{
    /// <inheritdoc />
    public partial class UpgradeSessionAndMovementUserFK : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClosedBy",
                table: "CashRegisterSessions");

            migrationBuilder.DropColumn(
                name: "OpenedBy",
                table: "CashRegisterSessions");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "CashMovements");

            migrationBuilder.AddColumn<int>(
                name: "ClosedByUserId",
                table: "CashRegisterSessions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OpenedByUserId",
                table: "CashRegisterSessions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CreatedByUserId",
                table: "CashMovements",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CashRegisterSessions_ClosedByUserId",
                table: "CashRegisterSessions",
                column: "ClosedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CashRegisterSessions_OpenedByUserId",
                table: "CashRegisterSessions",
                column: "OpenedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CashMovements_CreatedByUserId",
                table: "CashMovements",
                column: "CreatedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_CashMovements_Users_CreatedByUserId",
                table: "CashMovements",
                column: "CreatedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_CashRegisterSessions_Users_ClosedByUserId",
                table: "CashRegisterSessions",
                column: "ClosedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_CashRegisterSessions_Users_OpenedByUserId",
                table: "CashRegisterSessions",
                column: "OpenedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CashMovements_Users_CreatedByUserId",
                table: "CashMovements");

            migrationBuilder.DropForeignKey(
                name: "FK_CashRegisterSessions_Users_ClosedByUserId",
                table: "CashRegisterSessions");

            migrationBuilder.DropForeignKey(
                name: "FK_CashRegisterSessions_Users_OpenedByUserId",
                table: "CashRegisterSessions");

            migrationBuilder.DropIndex(
                name: "IX_CashRegisterSessions_ClosedByUserId",
                table: "CashRegisterSessions");

            migrationBuilder.DropIndex(
                name: "IX_CashRegisterSessions_OpenedByUserId",
                table: "CashRegisterSessions");

            migrationBuilder.DropIndex(
                name: "IX_CashMovements_CreatedByUserId",
                table: "CashMovements");

            migrationBuilder.DropColumn(
                name: "ClosedByUserId",
                table: "CashRegisterSessions");

            migrationBuilder.DropColumn(
                name: "OpenedByUserId",
                table: "CashRegisterSessions");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "CashMovements");

            migrationBuilder.AddColumn<string>(
                name: "ClosedBy",
                table: "CashRegisterSessions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OpenedBy",
                table: "CashRegisterSessions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "CashMovements",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");
        }
    }
}
