using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace POS.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceLimitsSchemaAndScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── FK-aware cleanup of legacy boolean device features ─────────────
            // FeatureCatalog Ids 30 (KdsBasic), 42 (KioskMode), 83 (GymReception)
            // are removed in favor of quantitative replacements at Ids 14
            // (MaxKdsScreens), 15 (MaxKiosks), 16 (MaxReceptionsPerBranch).
            // Children must be deleted before parents to satisfy FK constraints.
            migrationBuilder.Sql("DELETE FROM \"PlanBusinessTypeFeatureOverrides\" WHERE \"FeatureId\" IN (30, 42, 83);");
            migrationBuilder.Sql("DELETE FROM \"BusinessTypeFeatures\" WHERE \"FeatureId\" IN (30, 42, 83);");
            migrationBuilder.Sql("DELETE FROM \"PlanFeatureMatrices\" WHERE \"FeatureId\" IN (30, 42, 83);");
            migrationBuilder.Sql("DELETE FROM \"FeatureCatalogs\" WHERE \"Id\" IN (30, 42, 83);");

            // ── Schema change ───────────────────────────────────────────────────
            // EnforcementScope.Global = 0 — default is semantically valid for all
            // existing rows; quantitative-branch features are upgraded to Branch
            // by DbInitializer at app startup.
            migrationBuilder.AddColumn<int>(
                name: "Scope",
                table: "FeatureCatalogs",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Scope",
                table: "FeatureCatalogs");
        }
    }
}
