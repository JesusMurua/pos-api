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

            // Data: reconcile the 9 system payment methods (category/SAT/flags) so
            // the OrderPayments backfill below can resolve every Method to a catalog
            // row. Mirrors DbInitializer.UpsertPaymentMethodCatalogAsync — source of
            // truth: docs/payment-method-catalog-architecture.md §4.1.
            migrationBuilder.Sql(@"
                INSERT INTO ""PaymentMethodCatalogs""
                    (""Code"",""Name"",""SortOrder"",""Category"",""SatPaymentFormCode"",""RequiresReference"",""RequiresCustomer"",""SupportsOverpay"",""SupportsPartial"",""ProviderKey"",""CountryCode"",""IconClass"",""IsActive"",""IsSystem"")
                SELECT d.""Code"",d.""Name"",d.""SortOrder"",d.""Category"",d.""Sat"",d.""RR"",d.""RC"",d.""SO"",d.""SP"",d.""Provider"",d.""Country"",d.""Icon"",TRUE,TRUE
                FROM (VALUES
                    ('Cash','Efectivo',1,'Cash','01',FALSE,FALSE,TRUE,TRUE,NULL,NULL,'pi-money-bill'),
                    ('Card','Tarjeta',2,'Card','04',FALSE,FALSE,FALSE,TRUE,NULL,NULL,'pi-credit-card'),
                    ('Transfer','Transferencia',3,'Digital','03',FALSE,FALSE,FALSE,TRUE,NULL,NULL,'pi-arrow-right-arrow-left'),
                    ('Other','Otro',4,'Other','99',FALSE,FALSE,FALSE,TRUE,NULL,NULL,'pi-ellipsis-h'),
                    ('Clip','Clip',5,'Card','04',FALSE,FALSE,FALSE,TRUE,'clip','MX','pi-credit-card'),
                    ('MercadoPago','MercadoPago',6,'Digital','04',FALSE,FALSE,FALSE,TRUE,'mercadopago','MX','pi-qrcode'),
                    ('BankTerminal','Terminal bancaria',7,'Card','04',FALSE,FALSE,FALSE,TRUE,'bankterminal',NULL,'pi-credit-card'),
                    ('StoreCredit','Crédito de tienda',8,'Credit','05',FALSE,TRUE,FALSE,TRUE,NULL,NULL,'pi-wallet'),
                    ('LoyaltyPoints','Puntos',9,'Points','05',FALSE,TRUE,FALSE,TRUE,NULL,NULL,'pi-star')
                ) AS d(""Code"",""Name"",""SortOrder"",""Category"",""Sat"",""RR"",""RC"",""SO"",""SP"",""Provider"",""Country"",""Icon"")
                WHERE NOT EXISTS (SELECT 1 FROM ""PaymentMethodCatalogs"" p WHERE p.""Code"" = d.""Code"");

                UPDATE ""PaymentMethodCatalogs"" p SET
                    ""Name""=d.""Name"",""SortOrder""=d.""SortOrder"",""Category""=d.""Category"",""SatPaymentFormCode""=d.""Sat"",
                    ""RequiresReference""=d.""RR"",""RequiresCustomer""=d.""RC"",""SupportsOverpay""=d.""SO"",""SupportsPartial""=d.""SP"",
                    ""ProviderKey""=d.""Provider"",""CountryCode""=d.""Country"",""IconClass""=d.""Icon"",""IsActive""=TRUE,""IsSystem""=TRUE
                FROM (VALUES
                    ('Cash','Efectivo',1,'Cash','01',FALSE,FALSE,TRUE,TRUE,NULL,NULL,'pi-money-bill'),
                    ('Card','Tarjeta',2,'Card','04',FALSE,FALSE,FALSE,TRUE,NULL,NULL,'pi-credit-card'),
                    ('Transfer','Transferencia',3,'Digital','03',FALSE,FALSE,FALSE,TRUE,NULL,NULL,'pi-arrow-right-arrow-left'),
                    ('Other','Otro',4,'Other','99',FALSE,FALSE,FALSE,TRUE,NULL,NULL,'pi-ellipsis-h'),
                    ('Clip','Clip',5,'Card','04',FALSE,FALSE,FALSE,TRUE,'clip','MX','pi-credit-card'),
                    ('MercadoPago','MercadoPago',6,'Digital','04',FALSE,FALSE,FALSE,TRUE,'mercadopago','MX','pi-qrcode'),
                    ('BankTerminal','Terminal bancaria',7,'Card','04',FALSE,FALSE,FALSE,TRUE,'bankterminal',NULL,'pi-credit-card'),
                    ('StoreCredit','Crédito de tienda',8,'Credit','05',FALSE,TRUE,FALSE,TRUE,NULL,NULL,'pi-wallet'),
                    ('LoyaltyPoints','Puntos',9,'Points','05',FALSE,TRUE,FALSE,TRUE,NULL,NULL,'pi-star')
                ) AS d(""Code"",""Name"",""SortOrder"",""Category"",""Sat"",""RR"",""RC"",""SO"",""SP"",""Provider"",""Country"",""Icon"")
                WHERE p.""Code"" = d.""Code"";
            ");

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

            // Data: freeze the catalog snapshot onto existing payments so the FK
            // below is satisfied (the table is not guaranteed empty in deployed
            // environments). Every enum Method now resolves to a catalog row; a
            // stray value falls back to 'Other'.
            migrationBuilder.Sql(@"
                UPDATE ""OrderPayments"" o SET
                    ""MethodCode""=o.""Method"",
                    ""Category""=c.""Category"",
                    ""SatPaymentFormCode""=c.""SatPaymentFormCode"",
                    ""PaymentMethodId""=c.""Id""
                FROM ""PaymentMethodCatalogs"" c
                WHERE o.""Method"" = c.""Code"";

                UPDATE ""OrderPayments"" o SET
                    ""MethodCode""=c.""Code"",
                    ""Category""=c.""Category"",
                    ""SatPaymentFormCode""=c.""SatPaymentFormCode"",
                    ""PaymentMethodId""=c.""Id""
                FROM ""PaymentMethodCatalogs"" c
                WHERE c.""Code""='Other' AND o.""PaymentMethodId"" = 0;
            ");

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
