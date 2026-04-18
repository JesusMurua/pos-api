using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace POS.Repository.Migrations
{
    /// <inheritdoc />
    public partial class RefactorBusinessArchitecture : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Businesses_BusinessTypeCatalogs_BusinessTypeId",
                table: "Businesses");

            migrationBuilder.DropForeignKey(
                name: "FK_BusinessTypeFeatures_BusinessTypeCatalogs_BusinessTypeId",
                table: "BusinessTypeFeatures");

            migrationBuilder.DropForeignKey(
                name: "FK_PlanBusinessTypeFeatureOverrides_BusinessTypeCatalogs_Busin~",
                table: "PlanBusinessTypeFeatureOverrides");

            migrationBuilder.DropIndex(
                name: "IX_BusinessTypeCatalogs_Code",
                table: "BusinessTypeCatalogs");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "BusinessTypeCatalogs");

            migrationBuilder.DropColumn(
                name: "HasKitchen",
                table: "BusinessTypeCatalogs");

            migrationBuilder.DropColumn(
                name: "HasTables",
                table: "BusinessTypeCatalogs");

            migrationBuilder.DropColumn(
                name: "PosExperience",
                table: "BusinessTypeCatalogs");

            migrationBuilder.DropColumn(
                name: "CustomDescription",
                table: "BusinessGiros");

            migrationBuilder.RenameColumn(
                name: "BusinessTypeId",
                table: "PlanBusinessTypeFeatureOverrides",
                newName: "MacroCategoryId");

            migrationBuilder.RenameIndex(
                name: "IX_PlanBusinessTypeFeatureOverrides_BusinessTypeId",
                table: "PlanBusinessTypeFeatureOverrides",
                newName: "IX_PlanBusinessTypeFeatureOverrides_MacroCategoryId");

            migrationBuilder.RenameColumn(
                name: "BusinessTypeId",
                table: "BusinessTypeFeatures",
                newName: "MacroCategoryId");

            migrationBuilder.RenameColumn(
                name: "SortOrder",
                table: "BusinessTypeCatalogs",
                newName: "PrimaryMacroCategoryId");

            migrationBuilder.RenameColumn(
                name: "BusinessTypeId",
                table: "Businesses",
                newName: "PrimaryMacroCategoryId");

            migrationBuilder.RenameIndex(
                name: "IX_Businesses_BusinessTypeId",
                table: "Businesses",
                newName: "IX_Businesses_PrimaryMacroCategoryId");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "BusinessTypeCatalogs",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AddColumn<string>(
                name: "CustomGiroDescription",
                table: "Businesses",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MacroCategories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    InternalCode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    PublicName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MacroCategories", x => x.Id);
                });

            migrationBuilder.UpdateData(
                table: "Businesses",
                keyColumn: "Id",
                keyValue: 1,
                column: "CustomGiroDescription",
                value: null);

            migrationBuilder.CreateIndex(
                name: "IX_BusinessTypeCatalogs_Name",
                table: "BusinessTypeCatalogs",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BusinessTypeCatalogs_PrimaryMacroCategoryId",
                table: "BusinessTypeCatalogs",
                column: "PrimaryMacroCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_MacroCategories_InternalCode",
                table: "MacroCategories",
                column: "InternalCode",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Businesses_MacroCategories_PrimaryMacroCategoryId",
                table: "Businesses",
                column: "PrimaryMacroCategoryId",
                principalTable: "MacroCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_BusinessTypeCatalogs_MacroCategories_PrimaryMacroCategoryId",
                table: "BusinessTypeCatalogs",
                column: "PrimaryMacroCategoryId",
                principalTable: "MacroCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_BusinessTypeFeatures_MacroCategories_MacroCategoryId",
                table: "BusinessTypeFeatures",
                column: "MacroCategoryId",
                principalTable: "MacroCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PlanBusinessTypeFeatureOverrides_MacroCategories_MacroCateg~",
                table: "PlanBusinessTypeFeatureOverrides",
                column: "MacroCategoryId",
                principalTable: "MacroCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Businesses_MacroCategories_PrimaryMacroCategoryId",
                table: "Businesses");

            migrationBuilder.DropForeignKey(
                name: "FK_BusinessTypeCatalogs_MacroCategories_PrimaryMacroCategoryId",
                table: "BusinessTypeCatalogs");

            migrationBuilder.DropForeignKey(
                name: "FK_BusinessTypeFeatures_MacroCategories_MacroCategoryId",
                table: "BusinessTypeFeatures");

            migrationBuilder.DropForeignKey(
                name: "FK_PlanBusinessTypeFeatureOverrides_MacroCategories_MacroCateg~",
                table: "PlanBusinessTypeFeatureOverrides");

            migrationBuilder.DropTable(
                name: "MacroCategories");

            migrationBuilder.DropIndex(
                name: "IX_BusinessTypeCatalogs_Name",
                table: "BusinessTypeCatalogs");

            migrationBuilder.DropIndex(
                name: "IX_BusinessTypeCatalogs_PrimaryMacroCategoryId",
                table: "BusinessTypeCatalogs");

            migrationBuilder.DropColumn(
                name: "CustomGiroDescription",
                table: "Businesses");

            migrationBuilder.RenameColumn(
                name: "MacroCategoryId",
                table: "PlanBusinessTypeFeatureOverrides",
                newName: "BusinessTypeId");

            migrationBuilder.RenameIndex(
                name: "IX_PlanBusinessTypeFeatureOverrides_MacroCategoryId",
                table: "PlanBusinessTypeFeatureOverrides",
                newName: "IX_PlanBusinessTypeFeatureOverrides_BusinessTypeId");

            migrationBuilder.RenameColumn(
                name: "MacroCategoryId",
                table: "BusinessTypeFeatures",
                newName: "BusinessTypeId");

            migrationBuilder.RenameColumn(
                name: "PrimaryMacroCategoryId",
                table: "BusinessTypeCatalogs",
                newName: "SortOrder");

            migrationBuilder.RenameColumn(
                name: "PrimaryMacroCategoryId",
                table: "Businesses",
                newName: "BusinessTypeId");

            migrationBuilder.RenameIndex(
                name: "IX_Businesses_PrimaryMacroCategoryId",
                table: "Businesses",
                newName: "IX_Businesses_BusinessTypeId");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "BusinessTypeCatalogs",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "BusinessTypeCatalogs",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "HasKitchen",
                table: "BusinessTypeCatalogs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasTables",
                table: "BusinessTypeCatalogs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PosExperience",
                table: "BusinessTypeCatalogs",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CustomDescription",
                table: "BusinessGiros",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_BusinessTypeCatalogs_Code",
                table: "BusinessTypeCatalogs",
                column: "Code",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Businesses_BusinessTypeCatalogs_BusinessTypeId",
                table: "Businesses",
                column: "BusinessTypeId",
                principalTable: "BusinessTypeCatalogs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_BusinessTypeFeatures_BusinessTypeCatalogs_BusinessTypeId",
                table: "BusinessTypeFeatures",
                column: "BusinessTypeId",
                principalTable: "BusinessTypeCatalogs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PlanBusinessTypeFeatureOverrides_BusinessTypeCatalogs_Busin~",
                table: "PlanBusinessTypeFeatureOverrides",
                column: "BusinessTypeId",
                principalTable: "BusinessTypeCatalogs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
