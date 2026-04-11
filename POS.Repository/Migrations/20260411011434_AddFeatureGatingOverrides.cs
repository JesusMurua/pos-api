using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace POS.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddFeatureGatingOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlanBusinessTypeFeatureOverrides",
                columns: table => new
                {
                    PlanTypeId = table.Column<int>(type: "integer", nullable: false),
                    BusinessTypeId = table.Column<int>(type: "integer", nullable: false),
                    FeatureId = table.Column<int>(type: "integer", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanBusinessTypeFeatureOverrides", x => new { x.PlanTypeId, x.BusinessTypeId, x.FeatureId });
                    table.ForeignKey(
                        name: "FK_PlanBusinessTypeFeatureOverrides_BusinessTypeCatalogs_Busin~",
                        column: x => x.BusinessTypeId,
                        principalTable: "BusinessTypeCatalogs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlanBusinessTypeFeatureOverrides_FeatureCatalogs_FeatureId",
                        column: x => x.FeatureId,
                        principalTable: "FeatureCatalogs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlanBusinessTypeFeatureOverrides_PlanTypeCatalogs_PlanTypeId",
                        column: x => x.PlanTypeId,
                        principalTable: "PlanTypeCatalogs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlanBusinessTypeFeatureOverrides_BusinessTypeId",
                table: "PlanBusinessTypeFeatureOverrides",
                column: "BusinessTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_PlanBusinessTypeFeatureOverrides_FeatureId",
                table: "PlanBusinessTypeFeatureOverrides",
                column: "FeatureId");

            migrationBuilder.CreateIndex(
                name: "IX_PlanBusinessTypeFeatureOverrides_PlanTypeId_FeatureId",
                table: "PlanBusinessTypeFeatureOverrides",
                columns: new[] { "PlanTypeId", "FeatureId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlanBusinessTypeFeatureOverrides");
        }
    }
}
