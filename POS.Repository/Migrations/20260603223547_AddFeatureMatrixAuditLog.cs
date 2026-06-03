using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace POS.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddFeatureMatrixAuditLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FeatureMatrixAuditLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ChangedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ChangedByTokenId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Axis = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    EntityKey = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    BeforeJson = table.Column<string>(type: "text", nullable: true),
                    AfterJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeatureMatrixAuditLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FeatureMatrixAuditLogs_Axis",
                table: "FeatureMatrixAuditLogs",
                column: "Axis");

            migrationBuilder.CreateIndex(
                name: "IX_FeatureMatrixAuditLogs_ChangedAt",
                table: "FeatureMatrixAuditLogs",
                column: "ChangedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FeatureMatrixAuditLogs");
        }
    }
}
