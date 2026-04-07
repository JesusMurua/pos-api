using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace POS.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentStatusStrict : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ConfirmedAt",
                table: "OrderPayments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "OrderPayments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "completed");

            // Backfill existing rows, then drop the default so new rows must provide Status explicitly
            migrationBuilder.Sql(
                "UPDATE \"OrderPayments\" SET \"Status\" = 'completed' WHERE \"Status\" = '' OR \"Status\" IS NULL;");
            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "OrderPayments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldDefaultValue: "completed");

            migrationBuilder.CreateIndex(
                name: "IX_OrderPayments_Status",
                table: "OrderPayments",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OrderPayments_Status",
                table: "OrderPayments");

            migrationBuilder.DropColumn(
                name: "ConfirmedAt",
                table: "OrderPayments");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "OrderPayments");
        }
    }
}
