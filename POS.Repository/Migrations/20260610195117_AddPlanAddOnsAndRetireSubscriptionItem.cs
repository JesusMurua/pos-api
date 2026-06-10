using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace POS.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanAddOnsAndRetireSubscriptionItem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SubscriptionItems");

            migrationBuilder.CreateTable(
                name: "PlanAddOns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    BillingCycle = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DefaultPriceCents = table.Column<int>(type: "integer", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    LinkType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    LinkedEntityId = table.Column<int>(type: "integer", nullable: true),
                    StripePriceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanAddOns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SubscriptionAddOns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SubscriptionId = table.Column<int>(type: "integer", nullable: false),
                    AddOnId = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    ActivatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeactivatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CustomPriceCents = table.Column<int>(type: "integer", nullable: true),
                    ActivatedByTokenIdHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    StripeItemId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    StripeAddOnPriceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LastProRatedInvoiceId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionAddOns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubscriptionAddOns_PlanAddOns_AddOnId",
                        column: x => x.AddOnId,
                        principalTable: "PlanAddOns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubscriptionAddOns_Subscriptions_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalTable: "Subscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionInvoiceItems_LinkedAddOnId",
                table: "SubscriptionInvoiceItems",
                column: "LinkedAddOnId");

            migrationBuilder.CreateIndex(
                name: "IX_PlanAddOns_Code",
                table: "PlanAddOns",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionAddOns_AddOnId",
                table: "SubscriptionAddOns",
                column: "AddOnId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionAddOns_SubscriptionId",
                table: "SubscriptionAddOns",
                column: "SubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionAddOns_SubscriptionId_AddOnId",
                table: "SubscriptionAddOns",
                columns: new[] { "SubscriptionId", "AddOnId" },
                unique: true,
                filter: "\"DeactivatedAt\" IS NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_SubscriptionInvoiceItems_PlanAddOns_LinkedAddOnId",
                table: "SubscriptionInvoiceItems",
                column: "LinkedAddOnId",
                principalTable: "PlanAddOns",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SubscriptionInvoiceItems_PlanAddOns_LinkedAddOnId",
                table: "SubscriptionInvoiceItems");

            migrationBuilder.DropTable(
                name: "SubscriptionAddOns");

            migrationBuilder.DropTable(
                name: "PlanAddOns");

            migrationBuilder.DropIndex(
                name: "IX_SubscriptionInvoiceItems_LinkedAddOnId",
                table: "SubscriptionInvoiceItems");

            migrationBuilder.CreateTable(
                name: "SubscriptionItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SubscriptionId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsBasePlan = table.Column<bool>(type: "boolean", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    StripeItemId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    StripePriceId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubscriptionItems_Subscriptions_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalTable: "Subscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionItems_StripeItemId",
                table: "SubscriptionItems",
                column: "StripeItemId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionItems_SubscriptionId",
                table: "SubscriptionItems",
                column: "SubscriptionId");
        }
    }
}
