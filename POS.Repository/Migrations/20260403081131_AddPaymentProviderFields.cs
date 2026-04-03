using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace POS.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentProviderFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExternalTransactionId",
                table: "OrderPayments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OperationId",
                table: "OrderPayments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentMetadata",
                table: "OrderPayments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentProvider",
                table: "OrderPayments",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderPayments_ExternalTransactionId",
                table: "OrderPayments",
                column: "ExternalTransactionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OrderPayments_ExternalTransactionId",
                table: "OrderPayments");

            migrationBuilder.DropColumn(
                name: "ExternalTransactionId",
                table: "OrderPayments");

            migrationBuilder.DropColumn(
                name: "OperationId",
                table: "OrderPayments");

            migrationBuilder.DropColumn(
                name: "PaymentMetadata",
                table: "OrderPayments");

            migrationBuilder.DropColumn(
                name: "PaymentProvider",
                table: "OrderPayments");
        }
    }
}
