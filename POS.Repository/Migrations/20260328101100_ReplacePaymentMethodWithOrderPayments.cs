using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace POS.Repository.Migrations
{
    /// <inheritdoc />
    public partial class ReplacePaymentMethodWithOrderPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Create OrderPayments table first
            migrationBuilder.CreateTable(
                name: "OrderPayments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrderId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    Method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AmountCents = table.Column<int>(type: "integer", nullable: false),
                    Reference = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderPayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderPayments_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderPayments_OrderId",
                table: "OrderPayments",
                column: "OrderId");

            // 2. Migrate existing payment data
            migrationBuilder.Sql(@"
                INSERT INTO ""OrderPayments"" (""OrderId"", ""Method"", ""AmountCents"", ""Reference"", ""CreatedAt"")
                SELECT ""Id"", ""PaymentMethod"", ""TotalCents"", ""ExternalReference"", ""CreatedAt""
                FROM ""Orders""
                WHERE ""PaymentMethod"" IS NOT NULL AND ""PaymentMethod"" != '';
            ");

            // 3. Add PaidCents and populate from existing data
            migrationBuilder.AddColumn<int>(
                name: "PaidCents",
                table: "Orders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql(@"
                UPDATE ""Orders"" SET ""PaidCents"" = COALESCE(""TenderedCents"", ""TotalCents"");
                UPDATE ""Orders"" SET ""ChangeCents"" = GREATEST(0, ""PaidCents"" - ""TotalCents"")
                WHERE ""ChangeCents"" IS NULL;
            ");

            // 4. Drop old columns
            migrationBuilder.DropColumn(name: "ExternalReference", table: "Orders");
            migrationBuilder.DropColumn(name: "PaymentMethod", table: "Orders");
            migrationBuilder.DropColumn(name: "PaymentProvider", table: "Orders");
            migrationBuilder.DropColumn(name: "TenderedCents", table: "Orders");

            // 5. Change ChangeCents from nullable to non-nullable
            migrationBuilder.AlterColumn<int>(
                name: "ChangeCents",
                table: "Orders",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "OrderPayments");

            migrationBuilder.DropColumn(name: "PaidCents", table: "Orders");

            migrationBuilder.AlterColumn<int>(
                name: "ChangeCents",
                table: "Orders",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<string>(
                name: "ExternalReference",
                table: "Orders",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentMethod",
                table: "Orders",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PaymentProvider",
                table: "Orders",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TenderedCents",
                table: "Orders",
                type: "integer",
                nullable: true);
        }
    }
}
