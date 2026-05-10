using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace POS.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddAccessControlAndBiometrics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "BiometricTemplate",
                table: "Customers",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QrToken",
                table: "Customers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AccessMethodCatalogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessMethodCatalogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AccessReasonCatalogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessReasonCatalogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AccessLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BranchId = table.Column<int>(type: "integer", nullable: false),
                    CustomerId = table.Column<int>(type: "integer", nullable: false),
                    DeviceId = table.Column<int>(type: "integer", nullable: true),
                    CustomerMembershipId = table.Column<int>(type: "integer", nullable: true),
                    AccessReasonId = table.Column<int>(type: "integer", nullable: false),
                    AccessMethodId = table.Column<int>(type: "integer", nullable: false),
                    IsGranted = table.Column<bool>(type: "boolean", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccessLogs_AccessMethodCatalogs_AccessMethodId",
                        column: x => x.AccessMethodId,
                        principalTable: "AccessMethodCatalogs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccessLogs_AccessReasonCatalogs_AccessReasonId",
                        column: x => x.AccessReasonId,
                        principalTable: "AccessReasonCatalogs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccessLogs_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccessLogs_CustomerMemberships_CustomerMembershipId",
                        column: x => x.CustomerMembershipId,
                        principalTable: "CustomerMemberships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccessLogs_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccessLogs_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Customers_BusinessId_QrToken",
                table: "Customers",
                columns: new[] { "BusinessId", "QrToken" },
                unique: true,
                filter: "\"QrToken\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AccessLogs_AccessMethodId",
                table: "AccessLogs",
                column: "AccessMethodId");

            migrationBuilder.CreateIndex(
                name: "IX_AccessLogs_AccessReasonId",
                table: "AccessLogs",
                column: "AccessReasonId");

            migrationBuilder.CreateIndex(
                name: "IX_AccessLogs_BranchId_OccurredAt",
                table: "AccessLogs",
                columns: new[] { "BranchId", "OccurredAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_AccessLogs_CustomerId_OccurredAt",
                table: "AccessLogs",
                columns: new[] { "CustomerId", "OccurredAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_AccessLogs_CustomerMembershipId",
                table: "AccessLogs",
                column: "CustomerMembershipId");

            migrationBuilder.CreateIndex(
                name: "IX_AccessLogs_DeviceId",
                table: "AccessLogs",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_AccessMethodCatalogs_Code",
                table: "AccessMethodCatalogs",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccessReasonCatalogs_Code",
                table: "AccessReasonCatalogs",
                column: "Code",
                unique: true);

            // Additive seed for the pre-existing DeviceModeCatalogs table. The
            // standard 5 modes are seeded by DbInitializer; this migration only
            // injects the new "bridge" row needed by the Hardware Bridge epic.
            // Access catalogs (AccessReasonCatalogs / AccessMethodCatalogs) are
            // intentionally left for DbInitializer so PostgreSQL identity
            // sequences align with AccessReasonIds / AccessMethodIds constants.
            migrationBuilder.InsertData(
                table: "DeviceModeCatalogs",
                columns: new[] { "Code", "Name", "Description" },
                values: new object[] { "bridge", "Puente Local", "Puente de hardware local (biometría, torniquetes, impresoras)" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Only the additive "bridge" row needs explicit removal — the new
            // catalog tables are dropped wholesale below, taking their seeds
            // with them.
            migrationBuilder.DeleteData(
                table: "DeviceModeCatalogs",
                keyColumn: "Code",
                keyValue: "bridge");

            migrationBuilder.DropTable(
                name: "AccessLogs");

            migrationBuilder.DropTable(
                name: "AccessMethodCatalogs");

            migrationBuilder.DropTable(
                name: "AccessReasonCatalogs");

            migrationBuilder.DropIndex(
                name: "IX_Customers_BusinessId_QrToken",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "BiometricTemplate",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "QrToken",
                table: "Customers");
        }
    }
}
