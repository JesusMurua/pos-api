using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace POS.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddClusterCodeToBusinessTypeCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClusterCode",
                table: "BusinessTypeCatalogs",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_BusinessTypeCatalog_ClusterCode",
                table: "BusinessTypeCatalogs",
                sql: "\"ClusterCode\" IS NULL OR \"ClusterCode\" IN ('beauty','health','automotive','pets','repair','fitness','education','home','events','professional')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_BusinessTypeCatalog_ClusterCode",
                table: "BusinessTypeCatalogs");

            migrationBuilder.DropColumn(
                name: "ClusterCode",
                table: "BusinessTypeCatalogs");
        }
    }
}
