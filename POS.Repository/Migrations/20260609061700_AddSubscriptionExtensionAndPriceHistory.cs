using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace POS.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionExtensionAndPriceHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BaseAmountCents",
                table: "Subscriptions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BillingEmail",
                table: "Subscriptions",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BillingMethodId",
                table: "Subscriptions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CfdiRequired",
                table: "Subscriptions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "Subscriptions",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "MXN");

            migrationBuilder.AddColumn<DateTime>(
                name: "NextBillingDate",
                table: "Subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "Subscriptions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StripeBaseItemId",
                table: "Subscriptions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StripePriceId",
                table: "Subscriptions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SubscriptionPriceHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SubscriptionId = table.Column<int>(type: "integer", nullable: false),
                    BeforeAmountCents = table.Column<int>(type: "integer", nullable: false),
                    AfterAmountCents = table.Column<int>(type: "integer", nullable: false),
                    ChangedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ChangedByTokenId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    EffectiveDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AppliedToInvoiceId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionPriceHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubscriptionPriceHistories_Subscriptions_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalTable: "Subscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_BillingMethodId",
                table: "Subscriptions",
                column: "BillingMethodId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPriceHistories_SubscriptionId",
                table: "SubscriptionPriceHistories",
                column: "SubscriptionId");

            migrationBuilder.AddForeignKey(
                name: "FK_Subscriptions_SaaSBillingMethods_BillingMethodId",
                table: "Subscriptions",
                column: "BillingMethodId",
                principalTable: "SaaSBillingMethods",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // ── Data-sensitive backfill (PR-1b §5). No-op on prod today (0 rows),
            // but structurally required and cold-start-proof (the PR-A1 lesson).
            // (a) Guarantee the Stripe rail exists before the backfill resolves it.
            //     Idempotent; mirrors DbInitializer.UpsertSaaSBillingMethodsAsync.
            migrationBuilder.Sql(@"
                INSERT INTO ""SaaSBillingMethods""
                    (""Code"", ""Name"", ""IsAutomatic"", ""RequiresReference"", ""ProviderKey"", ""CountryCode"", ""SortOrder"", ""IsActive"", ""IsSystem"")
                SELECT 'Stripe', 'Stripe', true, false, 'stripe', NULL, 1, true, true
                WHERE NOT EXISTS (SELECT 1 FROM ""SaaSBillingMethods"" WHERE ""Code"" = 'Stripe');");

            // (b) Every existing Subscription is a Stripe subscription
            //     (StripeSubscriptionId is NOT NULL) → assign the Stripe rail.
            migrationBuilder.Sql(@"
                UPDATE ""Subscriptions""
                   SET ""BillingMethodId"" = (SELECT ""Id"" FROM ""SaaSBillingMethods"" WHERE ""Code"" = 'Stripe')
                 WHERE ""BillingMethodId"" IS NULL;");

            // (c) Backfill the negotiated price from the plan. Enterprise
            //     (MonthlyPrice IS NULL) stays NULL — not coerced to 0.
            migrationBuilder.Sql(@"
                UPDATE ""Subscriptions"" AS s
                   SET ""BaseAmountCents"" = CAST(pt.""MonthlyPrice"" * 100 AS integer)
                  FROM ""PlanTypeCatalogs"" AS pt
                 WHERE pt.""Id"" = s.""PlanTypeId""
                   AND pt.""MonthlyPrice"" IS NOT NULL
                   AND s.""BaseAmountCents"" IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Subscriptions_SaaSBillingMethods_BillingMethodId",
                table: "Subscriptions");

            migrationBuilder.DropTable(
                name: "SubscriptionPriceHistories");

            migrationBuilder.DropIndex(
                name: "IX_Subscriptions_BillingMethodId",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "BaseAmountCents",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "BillingEmail",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "BillingMethodId",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "CfdiRequired",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "NextBillingDate",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "StripeBaseItemId",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "StripePriceId",
                table: "Subscriptions");
        }
    }
}
