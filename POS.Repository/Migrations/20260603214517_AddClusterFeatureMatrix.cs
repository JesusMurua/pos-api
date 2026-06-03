using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace POS.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddClusterFeatureMatrix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClusterFeatures",
                columns: table => new
                {
                    ClusterCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    FeatureId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClusterFeatures", x => new { x.ClusterCode, x.FeatureId });
                    table.CheckConstraint("CK_ClusterFeature_ClusterCode", "\"ClusterCode\" IN ('beauty','health','automotive','pets','repair','fitness','education','home','events','professional')");
                    table.ForeignKey(
                        name: "FK_ClusterFeatures_FeatureCatalogs_FeatureId",
                        column: x => x.FeatureId,
                        principalTable: "FeatureCatalogs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClusterFeatures_FeatureId",
                table: "ClusterFeatures",
                column: "FeatureId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClusterFeatures");
        }
    }
}
