using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace POS.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddMultiGiroArchitecture : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_BusinessTypeCatalogs_Code",
                table: "BusinessTypeCatalogs",
                column: "Code");

            migrationBuilder.CreateTable(
                name: "BusinessGiros",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BusinessId = table.Column<int>(type: "integer", nullable: false),
                    CatalogCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CustomDescription = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessGiros", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BusinessGiros_BusinessTypeCatalogs_CatalogCode",
                        column: x => x.CatalogCode,
                        principalTable: "BusinessTypeCatalogs",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BusinessGiros_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessGiros_BusinessId_CatalogCode",
                table: "BusinessGiros",
                columns: new[] { "BusinessId", "CatalogCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BusinessGiros_CatalogCode",
                table: "BusinessGiros",
                column: "CatalogCode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BusinessGiros");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_BusinessTypeCatalogs_Code",
                table: "BusinessTypeCatalogs");
        }
    }
}
