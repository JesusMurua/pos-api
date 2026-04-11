using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace POS.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddFeatureGatingMatrix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FeatureCatalogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Key = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    IsQuantitative = table.Column<bool>(type: "boolean", nullable: false),
                    ResourceLabel = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeatureCatalogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BusinessTypeFeatures",
                columns: table => new
                {
                    BusinessTypeId = table.Column<int>(type: "integer", nullable: false),
                    FeatureId = table.Column<int>(type: "integer", nullable: false),
                    Limit = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessTypeFeatures", x => new { x.BusinessTypeId, x.FeatureId });
                    table.ForeignKey(
                        name: "FK_BusinessTypeFeatures_BusinessTypeCatalogs_BusinessTypeId",
                        column: x => x.BusinessTypeId,
                        principalTable: "BusinessTypeCatalogs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BusinessTypeFeatures_FeatureCatalogs_FeatureId",
                        column: x => x.FeatureId,
                        principalTable: "FeatureCatalogs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlanFeatureMatrices",
                columns: table => new
                {
                    PlanTypeId = table.Column<int>(type: "integer", nullable: false),
                    FeatureId = table.Column<int>(type: "integer", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    DefaultLimit = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanFeatureMatrices", x => new { x.PlanTypeId, x.FeatureId });
                    table.ForeignKey(
                        name: "FK_PlanFeatureMatrices_FeatureCatalogs_FeatureId",
                        column: x => x.FeatureId,
                        principalTable: "FeatureCatalogs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlanFeatureMatrices_PlanTypeCatalogs_PlanTypeId",
                        column: x => x.PlanTypeId,
                        principalTable: "PlanTypeCatalogs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessTypeFeatures_FeatureId",
                table: "BusinessTypeFeatures",
                column: "FeatureId");

            migrationBuilder.CreateIndex(
                name: "IX_FeatureCatalogs_Code",
                table: "FeatureCatalogs",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FeatureCatalogs_Key",
                table: "FeatureCatalogs",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlanFeatureMatrices_FeatureId",
                table: "PlanFeatureMatrices",
                column: "FeatureId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BusinessTypeFeatures");

            migrationBuilder.DropTable(
                name: "PlanFeatureMatrices");

            migrationBuilder.DropTable(
                name: "FeatureCatalogs");
        }
    }
}
