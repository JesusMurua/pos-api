using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace POS.Repository.Migrations
{
    /// <inheritdoc />
    public partial class UpgradeCashRegisterDeviceFK : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CashRegisters_BranchId_DeviceUuid",
                table: "CashRegisters");

            migrationBuilder.DropColumn(
                name: "DeviceUuid",
                table: "CashRegisters");

            migrationBuilder.AddColumn<int>(
                name: "DeviceId",
                table: "CashRegisters",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CashRegisters_DeviceId",
                table: "CashRegisters",
                column: "DeviceId",
                unique: true,
                filter: "\"DeviceId\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_CashRegisters_Devices_DeviceId",
                table: "CashRegisters",
                column: "DeviceId",
                principalTable: "Devices",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CashRegisters_Devices_DeviceId",
                table: "CashRegisters");

            migrationBuilder.DropIndex(
                name: "IX_CashRegisters_DeviceId",
                table: "CashRegisters");

            migrationBuilder.DropColumn(
                name: "DeviceId",
                table: "CashRegisters");

            migrationBuilder.AddColumn<string>(
                name: "DeviceUuid",
                table: "CashRegisters",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CashRegisters_BranchId_DeviceUuid",
                table: "CashRegisters",
                columns: new[] { "BranchId", "DeviceUuid" },
                unique: true,
                filter: "\"DeviceUuid\" IS NOT NULL");
        }
    }
}
