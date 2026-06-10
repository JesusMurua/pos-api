using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace POS.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddStripePlanPriceCatalogAndBillingNotNull : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "BillingMethodId",
                table: "Subscriptions",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "BeforeAmountCents",
                table: "SubscriptionPriceHistories",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.CreateTable(
                name: "StripePlanPrices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PlanTypeId = table.Column<int>(type: "integer", nullable: false),
                    BillingCycle = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PricingGroup = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StripePriceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StripePlanPrices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StripePlanPrices_PlanTypeCatalogs_PlanTypeId",
                        column: x => x.PlanTypeId,
                        principalTable: "PlanTypeCatalogs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StripePlanPrices_PlanTypeId_BillingCycle_PricingGroup",
                table: "StripePlanPrices",
                columns: new[] { "PlanTypeId", "BillingCycle", "PricingGroup" });

            migrationBuilder.CreateIndex(
                name: "IX_StripePlanPrices_StripePriceId",
                table: "StripePlanPrices",
                column: "StripePriceId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StripePlanPrices");

            migrationBuilder.AlterColumn<int>(
                name: "BillingMethodId",
                table: "Subscriptions",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<int>(
                name: "BeforeAmountCents",
                table: "SubscriptionPriceHistories",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);
        }
    }
}
