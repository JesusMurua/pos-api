using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace POS.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionInvoicingAndPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SubscriptionInvoices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SubscriptionId = table.Column<int>(type: "integer", nullable: false),
                    BusinessId = table.Column<int>(type: "integer", nullable: false),
                    InvoiceNumber = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IssuedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DueDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PeriodStart = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PeriodEnd = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SubtotalCents = table.Column<int>(type: "integer", nullable: false),
                    TaxCents = table.Column<int>(type: "integer", nullable: false),
                    TotalCents = table.Column<int>(type: "integer", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    CreatedByTokenIdHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    StripeInvoiceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ReceptorRfc = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: true),
                    ReceptorRegime = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    ReceptorLegalName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    ReceptorPostalCode = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true),
                    CfdiUseCode = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    SatPaymentFormCode = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    SatUuid = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    SatStampedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SatXmlUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SatPdfUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionInvoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubscriptionInvoices_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubscriptionInvoices_Subscriptions_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalTable: "Subscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionInvoiceItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    InvoiceId = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    UnitAmountCents = table.Column<int>(type: "integer", nullable: false),
                    TotalAmountCents = table.Column<int>(type: "integer", nullable: false),
                    ItemType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    LinkedAddOnId = table.Column<int>(type: "integer", nullable: true),
                    LinkedPlanTypeId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionInvoiceItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubscriptionInvoiceItems_PlanTypeCatalogs_LinkedPlanTypeId",
                        column: x => x.LinkedPlanTypeId,
                        principalTable: "PlanTypeCatalogs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubscriptionInvoiceItems_SubscriptionInvoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "SubscriptionInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TenantPayments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    InvoiceId = table.Column<int>(type: "integer", nullable: false),
                    BillingMethodId = table.Column<int>(type: "integer", nullable: false),
                    AmountCents = table.Column<int>(type: "integer", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    PaidAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Reference = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Notes = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    ReceivedByTokenIdHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    StripeChargeId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RawWebhookPayloadJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantPayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TenantPayments_SaaSBillingMethods_BillingMethodId",
                        column: x => x.BillingMethodId,
                        principalTable: "SaaSBillingMethods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TenantPayments_SubscriptionInvoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "SubscriptionInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPriceHistories_AppliedToInvoiceId",
                table: "SubscriptionPriceHistories",
                column: "AppliedToInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionInvoiceItems_InvoiceId",
                table: "SubscriptionInvoiceItems",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionInvoiceItems_LinkedPlanTypeId",
                table: "SubscriptionInvoiceItems",
                column: "LinkedPlanTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionInvoices_BusinessId",
                table: "SubscriptionInvoices",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionInvoices_BusinessId_InvoiceNumber",
                table: "SubscriptionInvoices",
                columns: new[] { "BusinessId", "InvoiceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionInvoices_StripeInvoiceId",
                table: "SubscriptionInvoices",
                column: "StripeInvoiceId",
                unique: true,
                filter: "\"StripeInvoiceId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionInvoices_SubscriptionId",
                table: "SubscriptionInvoices",
                column: "SubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionInvoices_SubscriptionId_PeriodStart",
                table: "SubscriptionInvoices",
                columns: new[] { "SubscriptionId", "PeriodStart" },
                unique: true,
                filter: "\"StripeInvoiceId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TenantPayments_BillingMethodId_Reference",
                table: "TenantPayments",
                columns: new[] { "BillingMethodId", "Reference" },
                unique: true,
                filter: "\"Reference\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TenantPayments_InvoiceId",
                table: "TenantPayments",
                column: "InvoiceId");

            migrationBuilder.AddForeignKey(
                name: "FK_SubscriptionPriceHistories_SubscriptionInvoices_AppliedToIn~",
                table: "SubscriptionPriceHistories",
                column: "AppliedToInvoiceId",
                principalTable: "SubscriptionInvoices",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SubscriptionPriceHistories_SubscriptionInvoices_AppliedToIn~",
                table: "SubscriptionPriceHistories");

            migrationBuilder.DropTable(
                name: "SubscriptionInvoiceItems");

            migrationBuilder.DropTable(
                name: "TenantPayments");

            migrationBuilder.DropTable(
                name: "SubscriptionInvoices");

            migrationBuilder.DropIndex(
                name: "IX_SubscriptionPriceHistories_AppliedToInvoiceId",
                table: "SubscriptionPriceHistories");
        }
    }
}
