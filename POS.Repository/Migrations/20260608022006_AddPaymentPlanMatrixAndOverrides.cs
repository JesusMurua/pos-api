using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace POS.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentPlanMatrixAndOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PaymentMatrixAuditLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ChangedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ChangedByTokenId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Axis = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    EntityKey = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    BeforeJson = table.Column<string>(type: "text", nullable: true),
                    AfterJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentMatrixAuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlanPaymentMethodMatrices",
                columns: table => new
                {
                    PlanTypeId = table.Column<int>(type: "integer", nullable: false),
                    PaymentMethodId = table.Column<int>(type: "integer", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanPaymentMethodMatrices", x => new { x.PlanTypeId, x.PaymentMethodId });
                    table.ForeignKey(
                        name: "FK_PlanPaymentMethodMatrices_PaymentMethodCatalogs_PaymentMeth~",
                        column: x => x.PaymentMethodId,
                        principalTable: "PaymentMethodCatalogs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlanPaymentMethodMatrices_PlanTypeCatalogs_PlanTypeId",
                        column: x => x.PlanTypeId,
                        principalTable: "PlanTypeCatalogs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TenantPaymentMethodOverrides",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BusinessId = table.Column<int>(type: "integer", nullable: false),
                    PaymentMethodId = table.Column<int>(type: "integer", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CustomLabel = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ProviderConfigJson = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantPaymentMethodOverrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TenantPaymentMethodOverrides_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TenantPaymentMethodOverrides_PaymentMethodCatalogs_PaymentM~",
                        column: x => x.PaymentMethodId,
                        principalTable: "PaymentMethodCatalogs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentMatrixAuditLogs_Axis",
                table: "PaymentMatrixAuditLogs",
                column: "Axis");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentMatrixAuditLogs_ChangedAt",
                table: "PaymentMatrixAuditLogs",
                column: "ChangedAt");

            migrationBuilder.CreateIndex(
                name: "IX_PlanPaymentMethodMatrices_PaymentMethodId",
                table: "PlanPaymentMethodMatrices",
                column: "PaymentMethodId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantPaymentMethodOverrides_BusinessId_PaymentMethodId",
                table: "TenantPaymentMethodOverrides",
                columns: new[] { "BusinessId", "PaymentMethodId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantPaymentMethodOverrides_PaymentMethodId",
                table: "TenantPaymentMethodOverrides",
                column: "PaymentMethodId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentMatrixAuditLogs");

            migrationBuilder.DropTable(
                name: "PlanPaymentMethodMatrices");

            migrationBuilder.DropTable(
                name: "TenantPaymentMethodOverrides");
        }
    }
}
