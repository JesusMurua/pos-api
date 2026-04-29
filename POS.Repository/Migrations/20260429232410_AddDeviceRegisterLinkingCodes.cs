using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace POS.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceRegisterLinkingCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CashRegisterId",
                table: "DeviceActivationCodes",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CashRegisterLinkCodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    CashRegisterId = table.Column<int>(type: "integer", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsUsed = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    UsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashRegisterLinkCodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CashRegisterLinkCodes_CashRegisters_CashRegisterId",
                        column: x => x.CashRegisterId,
                        principalTable: "CashRegisters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeviceActivationCodes_CashRegisterId",
                table: "DeviceActivationCodes",
                column: "CashRegisterId");

            migrationBuilder.CreateIndex(
                name: "IX_CashRegisterLinkCodes_CashRegisterId_IsUsed_ExpiresAt",
                table: "CashRegisterLinkCodes",
                columns: new[] { "CashRegisterId", "IsUsed", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CashRegisterLinkCodes_Code",
                table: "CashRegisterLinkCodes",
                column: "Code",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_DeviceActivationCodes_CashRegisters_CashRegisterId",
                table: "DeviceActivationCodes",
                column: "CashRegisterId",
                principalTable: "CashRegisters",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DeviceActivationCodes_CashRegisters_CashRegisterId",
                table: "DeviceActivationCodes");

            migrationBuilder.DropTable(
                name: "CashRegisterLinkCodes");

            migrationBuilder.DropIndex(
                name: "IX_DeviceActivationCodes_CashRegisterId",
                table: "DeviceActivationCodes");

            migrationBuilder.DropColumn(
                name: "CashRegisterId",
                table: "DeviceActivationCodes");
        }
    }
}
