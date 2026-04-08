using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace POS.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddStrictNumericOnboardingCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Create the catalog table first
            migrationBuilder.CreateTable(
                name: "OnboardingStatusCatalogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OnboardingStatusCatalogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OnboardingStatusCatalogs_Code",
                table: "OnboardingStatusCatalogs",
                column: "Code",
                unique: true);

            // 2. Seed catalog rows so FK can be satisfied
            migrationBuilder.InsertData(
                table: "OnboardingStatusCatalogs",
                columns: new[] { "Id", "Code", "Name" },
                values: new object[,]
                {
                    { 1, "Pending", "Pendiente" },
                    { 2, "InProgress", "En progreso" },
                    { 3, "Completed", "Completado" },
                    { 4, "Skipped", "Omitido" }
                });

            // 3. Add columns with default = 1 (Pending)
            migrationBuilder.AddColumn<int>(
                name: "CurrentOnboardingStep",
                table: "Businesses",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "OnboardingStatusId",
                table: "Businesses",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            // 4. Backfill existing businesses: OnboardingCompleted = true → StatusId 3
            migrationBuilder.Sql(
                """
                UPDATE "Businesses"
                SET "OnboardingStatusId" = 3
                WHERE "OnboardingCompleted" = true;
                """);

            migrationBuilder.UpdateData(
                table: "Businesses",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "CurrentOnboardingStep", "OnboardingStatusId" },
                values: new object[] { 1, 1 });

            // 5. FK and index
            migrationBuilder.CreateIndex(
                name: "IX_Businesses_OnboardingStatusId",
                table: "Businesses",
                column: "OnboardingStatusId");

            migrationBuilder.AddForeignKey(
                name: "FK_Businesses_OnboardingStatusCatalogs_OnboardingStatusId",
                table: "Businesses",
                column: "OnboardingStatusId",
                principalTable: "OnboardingStatusCatalogs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Businesses_OnboardingStatusCatalogs_OnboardingStatusId",
                table: "Businesses");

            migrationBuilder.DropTable(
                name: "OnboardingStatusCatalogs");

            migrationBuilder.DropIndex(
                name: "IX_Businesses_OnboardingStatusId",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "CurrentOnboardingStep",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "OnboardingStatusId",
                table: "Businesses");
        }
    }
}
