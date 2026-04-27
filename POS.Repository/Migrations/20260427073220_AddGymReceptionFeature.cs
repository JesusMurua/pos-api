using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace POS.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddGymReceptionFeature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Parent row first — junctions FK to FeatureCatalogs.Id.
            migrationBuilder.InsertData(
                table: "FeatureCatalogs",
                columns: new[] { "Id", "Code", "Key", "Name", "Description", "IsQuantitative", "ResourceLabel", "SortOrder" },
                values: new object[] { 83, "GymReception", 83, "Control de Acceso (Gym)", "Modo recepción para control de acceso (gimnasios)", false, null, 83 });

            // Plan × Feature: Free=false, Basic=true, Pro=true.
            // Services vertical does not use Enterprise plan — intentionally only 3 rows.
            migrationBuilder.InsertData(
                table: "PlanFeatureMatrices",
                columns: new[] { "PlanTypeId", "FeatureId", "IsEnabled", "DefaultLimit" },
                values: new object[,]
                {
                    { 1, 83, false, null }, // PlanTypeIds.Free
                    { 2, 83, true,  null }, // PlanTypeIds.Basic
                    { 3, 83, true,  null }  // PlanTypeIds.Pro
                });

            // Macro × Feature applicability — only Services exposes GymReception.
            migrationBuilder.InsertData(
                table: "BusinessTypeFeatures",
                columns: new[] { "MacroCategoryId", "FeatureId", "Limit" },
                values: new object[] { 4, 83, null }); // 4 = MacroCategoryIds.Services
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Children first to respect FK constraints.
            migrationBuilder.DeleteData(
                table: "BusinessTypeFeatures",
                keyColumns: new[] { "MacroCategoryId", "FeatureId" },
                keyValues: new object[] { 4, 83 });

            migrationBuilder.DeleteData(
                table: "PlanFeatureMatrices",
                keyColumns: new[] { "PlanTypeId", "FeatureId" },
                keyValues: new object[] { 1, 83 });
            migrationBuilder.DeleteData(
                table: "PlanFeatureMatrices",
                keyColumns: new[] { "PlanTypeId", "FeatureId" },
                keyValues: new object[] { 2, 83 });
            migrationBuilder.DeleteData(
                table: "PlanFeatureMatrices",
                keyColumns: new[] { "PlanTypeId", "FeatureId" },
                keyValues: new object[] { 3, 83 });

            migrationBuilder.DeleteData(
                table: "FeatureCatalogs",
                keyColumn: "Id",
                keyValue: 83);
        }
    }
}
