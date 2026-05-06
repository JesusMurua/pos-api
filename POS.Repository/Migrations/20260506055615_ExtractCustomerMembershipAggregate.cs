using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace POS.Repository.Migrations
{
    /// <inheritdoc />
    public partial class ExtractCustomerMembershipAggregate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Customers_MembershipValidUntil",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "MembershipValidUntil",
                table: "Customers");

            migrationBuilder.CreateTable(
                name: "CustomerMemberships",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CustomerId = table.Column<int>(type: "integer", nullable: false),
                    ProductId = table.Column<int>(type: "integer", nullable: true),
                    ValidFrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ValidUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Active"),
                    OriginatingOrderId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerMemberships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerMemberships_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CustomerMemberships_Orders_OriginatingOrderId",
                        column: x => x.OriginatingOrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_CustomerMemberships_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerMemberships_CustomerId",
                table: "CustomerMemberships",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerMemberships_CustomerId_ProductId",
                table: "CustomerMemberships",
                columns: new[] { "CustomerId", "ProductId" },
                filter: "\"Status\" = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerMemberships_OriginatingOrderId",
                table: "CustomerMemberships",
                column: "OriginatingOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerMemberships_ProductId",
                table: "CustomerMemberships",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerMemberships_ValidUntil",
                table: "CustomerMemberships",
                column: "ValidUntil",
                filter: "\"Status\" = 'Active'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomerMemberships");

            migrationBuilder.AddColumn<DateTime>(
                name: "MembershipValidUntil",
                table: "Customers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Customers_MembershipValidUntil",
                table: "Customers",
                column: "MembershipValidUntil",
                filter: "\"MembershipValidUntil\" IS NOT NULL");
        }
    }
}
