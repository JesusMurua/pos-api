using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace POS.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentMethodCatalogFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "PaymentMethodCatalogs",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CountryCode",
                table: "PaymentMethodCatalogs",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IconClass",
                table: "PaymentMethodCatalogs",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "PaymentMethodCatalogs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsSystem",
                table: "PaymentMethodCatalogs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ProviderKey",
                table: "PaymentMethodCatalogs",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresCustomer",
                table: "PaymentMethodCatalogs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresReference",
                table: "PaymentMethodCatalogs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SatPaymentFormCode",
                table: "PaymentMethodCatalogs",
                type: "character varying(2)",
                maxLength: 2,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "SupportsOverpay",
                table: "PaymentMethodCatalogs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SupportsPartial",
                table: "PaymentMethodCatalogs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "OrderPayments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "MethodCode",
                table: "OrderPayments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "PaymentMethodId",
                table: "OrderPayments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SatPaymentFormCode",
                table: "OrderPayments",
                type: "character varying(2)",
                maxLength: 2,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "WasUnauthorized",
                table: "OrderPayments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "WasUnknownMethod",
                table: "OrderPayments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_OrderPayments_PaymentMethodId",
                table: "OrderPayments",
                column: "PaymentMethodId");

            migrationBuilder.AddForeignKey(
                name: "FK_OrderPayments_PaymentMethodCatalogs_PaymentMethodId",
                table: "OrderPayments",
                column: "PaymentMethodId",
                principalTable: "PaymentMethodCatalogs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrderPayments_PaymentMethodCatalogs_PaymentMethodId",
                table: "OrderPayments");

            migrationBuilder.DropIndex(
                name: "IX_OrderPayments_PaymentMethodId",
                table: "OrderPayments");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "PaymentMethodCatalogs");

            migrationBuilder.DropColumn(
                name: "CountryCode",
                table: "PaymentMethodCatalogs");

            migrationBuilder.DropColumn(
                name: "IconClass",
                table: "PaymentMethodCatalogs");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "PaymentMethodCatalogs");

            migrationBuilder.DropColumn(
                name: "IsSystem",
                table: "PaymentMethodCatalogs");

            migrationBuilder.DropColumn(
                name: "ProviderKey",
                table: "PaymentMethodCatalogs");

            migrationBuilder.DropColumn(
                name: "RequiresCustomer",
                table: "PaymentMethodCatalogs");

            migrationBuilder.DropColumn(
                name: "RequiresReference",
                table: "PaymentMethodCatalogs");

            migrationBuilder.DropColumn(
                name: "SatPaymentFormCode",
                table: "PaymentMethodCatalogs");

            migrationBuilder.DropColumn(
                name: "SupportsOverpay",
                table: "PaymentMethodCatalogs");

            migrationBuilder.DropColumn(
                name: "SupportsPartial",
                table: "PaymentMethodCatalogs");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "OrderPayments");

            migrationBuilder.DropColumn(
                name: "MethodCode",
                table: "OrderPayments");

            migrationBuilder.DropColumn(
                name: "PaymentMethodId",
                table: "OrderPayments");

            migrationBuilder.DropColumn(
                name: "SatPaymentFormCode",
                table: "OrderPayments");

            migrationBuilder.DropColumn(
                name: "WasUnauthorized",
                table: "OrderPayments");

            migrationBuilder.DropColumn(
                name: "WasUnknownMethod",
                table: "OrderPayments");
        }
    }
}
