using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace POS.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddCashRegisterFinancials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CashSalesCents",
                table: "CashRegisterSessions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DifferenceCents",
                table: "CashRegisterSessions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ExpectedAmountCents",
                table: "CashRegisterSessions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TotalCashInCents",
                table: "CashRegisterSessions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TotalCashOutCents",
                table: "CashRegisterSessions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "CashRegisterSessions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            // xmin is a PostgreSQL system column — no DDL needed.
            // The shadow property in the EF model snapshot enables concurrency checks.

            migrationBuilder.CreateIndex(
                name: "IX_CashRegisterSessions_BranchId",
                table: "CashRegisterSessions",
                column: "BranchId",
                unique: true,
                filter: "\"Status\" = 'open'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CashRegisterSessions_BranchId",
                table: "CashRegisterSessions");

            migrationBuilder.DropColumn(
                name: "CashSalesCents",
                table: "CashRegisterSessions");

            migrationBuilder.DropColumn(
                name: "DifferenceCents",
                table: "CashRegisterSessions");

            migrationBuilder.DropColumn(
                name: "ExpectedAmountCents",
                table: "CashRegisterSessions");

            migrationBuilder.DropColumn(
                name: "TotalCashInCents",
                table: "CashRegisterSessions");

            migrationBuilder.DropColumn(
                name: "TotalCashOutCents",
                table: "CashRegisterSessions");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "CashRegisterSessions");

            // xmin is a PostgreSQL system column — cannot be dropped.
        }
    }
}
