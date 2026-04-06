using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace POS.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddCashRegisterMultiTill : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CashRegisterSessions_BranchId",
                table: "CashRegisterSessions");

            migrationBuilder.AddColumn<int>(
                name: "CashRegisterId",
                table: "CashRegisterSessions",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CashRegisters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BranchId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DeviceUuid = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashRegisters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CashRegisters_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CashRegisterSessions_CashRegisterId",
                table: "CashRegisterSessions",
                column: "CashRegisterId",
                unique: true,
                filter: "\"Status\" = 'open' AND \"CashRegisterId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CashRegisters_BranchId_DeviceUuid",
                table: "CashRegisters",
                columns: new[] { "BranchId", "DeviceUuid" },
                unique: true,
                filter: "\"DeviceUuid\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CashRegisters_BranchId_Name",
                table: "CashRegisters",
                columns: new[] { "BranchId", "Name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_CashRegisterSessions_CashRegisters_CashRegisterId",
                table: "CashRegisterSessions",
                column: "CashRegisterId",
                principalTable: "CashRegisters",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CashRegisterSessions_CashRegisters_CashRegisterId",
                table: "CashRegisterSessions");

            migrationBuilder.DropTable(
                name: "CashRegisters");

            migrationBuilder.DropIndex(
                name: "IX_CashRegisterSessions_CashRegisterId",
                table: "CashRegisterSessions");

            migrationBuilder.DropColumn(
                name: "CashRegisterId",
                table: "CashRegisterSessions");

            migrationBuilder.CreateIndex(
                name: "IX_CashRegisterSessions_BranchId",
                table: "CashRegisterSessions",
                column: "BranchId",
                unique: true,
                filter: "\"Status\" = 'open'");
        }
    }
}
