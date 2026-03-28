using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace POS.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanGiroZoneFolio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FolioNumber",
                table: "Orders",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            // Normalize existing PlanType values before column resize
            migrationBuilder.Sql(@"
                UPDATE ""Businesses"" SET ""PlanType"" = 'Basic'
                WHERE LOWER(""PlanType"") IN ('basic', 'pro', 'enterprise');
                UPDATE ""Businesses"" SET ""PlanType"" = 'Free'
                WHERE ""PlanType"" IS NULL OR LOWER(""PlanType"") = 'free';
            ");

            migrationBuilder.AlterColumn<string>(
                name: "PlanType",
                table: "Businesses",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AddColumn<string>(
                name: "BusinessType",
                table: "Businesses",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "General");

            migrationBuilder.AddColumn<DateTime>(
                name: "TrialEndsAt",
                table: "Businesses",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TrialUsed",
                table: "Businesses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "FolioCounter",
                table: "Branches",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "FolioFormat",
                table: "Branches",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FolioPrefix",
                table: "Branches",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Zones",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BranchId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Zones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Zones_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "Branches",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "FolioCounter", "FolioFormat", "FolioPrefix" },
                values: new object[] { 0, null, null });

            migrationBuilder.UpdateData(
                table: "Businesses",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "BusinessType", "PlanType", "TrialEndsAt" },
                values: new object[] { "Restaurant", "Basic", null });

            migrationBuilder.CreateIndex(
                name: "IX_Zones_BranchId_SortOrder",
                table: "Zones",
                columns: new[] { "BranchId", "SortOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Zones");

            migrationBuilder.DropColumn(
                name: "FolioNumber",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "BusinessType",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "TrialEndsAt",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "TrialUsed",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "FolioCounter",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "FolioFormat",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "FolioPrefix",
                table: "Branches");

            migrationBuilder.AlterColumn<string>(
                name: "PlanType",
                table: "Businesses",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.UpdateData(
                table: "Businesses",
                keyColumn: "Id",
                keyValue: 1,
                column: "PlanType",
                value: "basic");
        }
    }
}
