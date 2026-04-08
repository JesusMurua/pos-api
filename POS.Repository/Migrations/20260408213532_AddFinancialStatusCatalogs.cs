using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace POS.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddFinancialStatusCatalogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ═══════════════════════════════════════════════════════════════
            // 1. Create the 4 catalog tables
            // ═══════════════════════════════════════════════════════════════

            migrationBuilder.CreateTable(
                name: "PaymentStatusCatalogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentStatusCatalogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CashRegisterStatusCatalogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashRegisterStatusCatalogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CashMovementTypeCatalogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashMovementTypeCatalogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OrderStatusCatalogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderStatusCatalogs", x => x.Id);
                });

            // Unique indexes on Code
            migrationBuilder.CreateIndex(name: "IX_PaymentStatusCatalogs_Code", table: "PaymentStatusCatalogs", column: "Code", unique: true);
            migrationBuilder.CreateIndex(name: "IX_CashRegisterStatusCatalogs_Code", table: "CashRegisterStatusCatalogs", column: "Code", unique: true);
            migrationBuilder.CreateIndex(name: "IX_CashMovementTypeCatalogs_Code", table: "CashMovementTypeCatalogs", column: "Code", unique: true);
            migrationBuilder.CreateIndex(name: "IX_OrderStatusCatalogs_Code", table: "OrderStatusCatalogs", column: "Code", unique: true);

            // ═══════════════════════════════════════════════════════════════
            // 2. Seed catalog rows
            // ═══════════════════════════════════════════════════════════════

            migrationBuilder.InsertData(
                table: "PaymentStatusCatalogs",
                columns: new[] { "Id", "Code", "Name" },
                values: new object[,]
                {
                    { 1, "pending", "Pendiente" },
                    { 2, "completed", "Completado" },
                    { 3, "failed", "Fallido" },
                    { 4, "refunded", "Reembolsado" }
                });

            migrationBuilder.InsertData(
                table: "CashRegisterStatusCatalogs",
                columns: new[] { "Id", "Code", "Name" },
                values: new object[,]
                {
                    { 1, "open", "Abierto" },
                    { 2, "closed", "Cerrado" },
                    { 3, "auditing", "En auditoría" }
                });

            migrationBuilder.InsertData(
                table: "CashMovementTypeCatalogs",
                columns: new[] { "Id", "Code", "Name" },
                values: new object[,]
                {
                    { 1, "in", "Entrada" },
                    { 2, "out", "Salida" },
                    { 3, "adjustment", "Ajuste" }
                });

            migrationBuilder.InsertData(
                table: "OrderStatusCatalogs",
                columns: new[] { "Id", "Code", "Name" },
                values: new object[,]
                {
                    { 1, "Draft", "Borrador" },
                    { 2, "Pending", "Pendiente" },
                    { 3, "Preparing", "En preparación" },
                    { 4, "Ready", "Listo" },
                    { 5, "Delivered", "Entregado" },
                    { 6, "Cancelled", "Cancelado" }
                });

            // ═══════════════════════════════════════════════════════════════
            // 3. Add new integer FK columns (nullable initially)
            // ═══════════════════════════════════════════════════════════════

            migrationBuilder.AddColumn<int>(
                name: "PaymentStatusId",
                table: "OrderPayments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CashRegisterStatusId",
                table: "CashRegisterSessions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CashMovementTypeId",
                table: "CashMovements",
                type: "integer",
                nullable: true);

            // ═══════════════════════════════════════════════════════════════
            // 4. Backfill: map old strings → new integer IDs
            // ═══════════════════════════════════════════════════════════════

            migrationBuilder.Sql("""
                UPDATE "OrderPayments" SET "PaymentStatusId" = CASE
                    WHEN "Status" = 'pending'   THEN 1
                    WHEN "Status" = 'completed'  THEN 2
                    WHEN "Status" = 'failed'     THEN 3
                    WHEN "Status" = 'refunded'   THEN 4
                    ELSE 1
                END;
                """);

            migrationBuilder.Sql("""
                UPDATE "CashRegisterSessions" SET "CashRegisterStatusId" = CASE
                    WHEN "Status" = 'open'   THEN 1
                    WHEN "Status" = 'closed' THEN 2
                    ELSE 1
                END;
                """);

            migrationBuilder.Sql("""
                UPDATE "CashMovements" SET "CashMovementTypeId" = CASE
                    WHEN "Type" = 'in'  THEN 1
                    WHEN "Type" = 'out' THEN 2
                    ELSE 1
                END;
                """);

            // ═══════════════════════════════════════════════════════════════
            // 5. Make columns NOT NULL now that all rows have values
            // ═══════════════════════════════════════════════════════════════

            migrationBuilder.AlterColumn<int>(
                name: "PaymentStatusId",
                table: "OrderPayments",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AlterColumn<int>(
                name: "CashRegisterStatusId",
                table: "CashRegisterSessions",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AlterColumn<int>(
                name: "CashMovementTypeId",
                table: "CashMovements",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            // ═══════════════════════════════════════════════════════════════
            // 6. Drop old string columns and their indexes
            // ═══════════════════════════════════════════════════════════════

            migrationBuilder.DropIndex(name: "IX_OrderPayments_Status", table: "OrderPayments");
            migrationBuilder.DropIndex(name: "IX_CashRegisterSessions_BranchId_Status", table: "CashRegisterSessions");
            migrationBuilder.DropIndex(name: "IX_CashRegisterSessions_CashRegisterId", table: "CashRegisterSessions");

            migrationBuilder.DropColumn(name: "Status", table: "OrderPayments");
            migrationBuilder.DropColumn(name: "Status", table: "CashRegisterSessions");
            migrationBuilder.DropColumn(name: "Type", table: "CashMovements");

            // ═══════════════════════════════════════════════════════════════
            // 7. Create indexes and FK constraints
            // ═══════════════════════════════════════════════════════════════

            migrationBuilder.CreateIndex(
                name: "IX_OrderPayments_PaymentStatusId",
                table: "OrderPayments",
                column: "PaymentStatusId");

            migrationBuilder.CreateIndex(
                name: "IX_CashRegisterSessions_BranchId_CashRegisterStatusId",
                table: "CashRegisterSessions",
                columns: new[] { "BranchId", "CashRegisterStatusId" });

            migrationBuilder.CreateIndex(
                name: "IX_CashRegisterSessions_CashRegisterStatusId",
                table: "CashRegisterSessions",
                column: "CashRegisterStatusId");

            // Partial unique index: only one open session per register
            migrationBuilder.CreateIndex(
                name: "IX_CashRegisterSessions_CashRegisterId",
                table: "CashRegisterSessions",
                column: "CashRegisterId",
                unique: true,
                filter: "\"CashRegisterStatusId\" = 1 AND \"CashRegisterId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CashMovements_CashMovementTypeId",
                table: "CashMovements",
                column: "CashMovementTypeId");

            migrationBuilder.AddForeignKey(
                name: "FK_OrderPayments_PaymentStatusCatalogs_PaymentStatusId",
                table: "OrderPayments",
                column: "PaymentStatusId",
                principalTable: "PaymentStatusCatalogs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CashRegisterSessions_CashRegisterStatusCatalogs_CashRegiste~",
                table: "CashRegisterSessions",
                column: "CashRegisterStatusId",
                principalTable: "CashRegisterStatusCatalogs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CashMovements_CashMovementTypeCatalogs_CashMovementTypeId",
                table: "CashMovements",
                column: "CashMovementTypeId",
                principalTable: "CashMovementTypeCatalogs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Reset identity sequences
            migrationBuilder.Sql("""
                SELECT setval(pg_get_serial_sequence('"PaymentStatusCatalogs"', 'Id'),
                    GREATEST((SELECT MAX("Id") FROM "PaymentStatusCatalogs") + 1,
                    nextval(pg_get_serial_sequence('"PaymentStatusCatalogs"', 'Id'))), false);

                SELECT setval(pg_get_serial_sequence('"CashRegisterStatusCatalogs"', 'Id'),
                    GREATEST((SELECT MAX("Id") FROM "CashRegisterStatusCatalogs") + 1,
                    nextval(pg_get_serial_sequence('"CashRegisterStatusCatalogs"', 'Id'))), false);

                SELECT setval(pg_get_serial_sequence('"CashMovementTypeCatalogs"', 'Id'),
                    GREATEST((SELECT MAX("Id") FROM "CashMovementTypeCatalogs") + 1,
                    nextval(pg_get_serial_sequence('"CashMovementTypeCatalogs"', 'Id'))), false);

                SELECT setval(pg_get_serial_sequence('"OrderStatusCatalogs"', 'Id'),
                    GREATEST((SELECT MAX("Id") FROM "OrderStatusCatalogs") + 1,
                    nextval(pg_get_serial_sequence('"OrderStatusCatalogs"', 'Id'))), false);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(name: "FK_OrderPayments_PaymentStatusCatalogs_PaymentStatusId", table: "OrderPayments");
            migrationBuilder.DropForeignKey(name: "FK_CashRegisterSessions_CashRegisterStatusCatalogs_CashRegiste~", table: "CashRegisterSessions");
            migrationBuilder.DropForeignKey(name: "FK_CashMovements_CashMovementTypeCatalogs_CashMovementTypeId", table: "CashMovements");

            migrationBuilder.DropTable(name: "PaymentStatusCatalogs");
            migrationBuilder.DropTable(name: "CashRegisterStatusCatalogs");
            migrationBuilder.DropTable(name: "CashMovementTypeCatalogs");
            migrationBuilder.DropTable(name: "OrderStatusCatalogs");

            migrationBuilder.DropIndex(name: "IX_OrderPayments_PaymentStatusId", table: "OrderPayments");
            migrationBuilder.DropIndex(name: "IX_CashRegisterSessions_BranchId_CashRegisterStatusId", table: "CashRegisterSessions");
            migrationBuilder.DropIndex(name: "IX_CashRegisterSessions_CashRegisterId", table: "CashRegisterSessions");
            migrationBuilder.DropIndex(name: "IX_CashRegisterSessions_CashRegisterStatusId", table: "CashRegisterSessions");
            migrationBuilder.DropIndex(name: "IX_CashMovements_CashMovementTypeId", table: "CashMovements");

            migrationBuilder.DropColumn(name: "PaymentStatusId", table: "OrderPayments");
            migrationBuilder.DropColumn(name: "CashRegisterStatusId", table: "CashRegisterSessions");
            migrationBuilder.DropColumn(name: "CashMovementTypeId", table: "CashMovements");

            migrationBuilder.AddColumn<string>(name: "Status", table: "OrderPayments", type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "");
            migrationBuilder.AddColumn<string>(name: "Status", table: "CashRegisterSessions", type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "");
            migrationBuilder.AddColumn<string>(name: "Type", table: "CashMovements", type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "");

            migrationBuilder.CreateIndex(name: "IX_OrderPayments_Status", table: "OrderPayments", column: "Status");
            migrationBuilder.CreateIndex(name: "IX_CashRegisterSessions_BranchId_Status", table: "CashRegisterSessions", columns: new[] { "BranchId", "Status" });
            migrationBuilder.CreateIndex(name: "IX_CashRegisterSessions_CashRegisterId", table: "CashRegisterSessions", column: "CashRegisterId", unique: true, filter: "\"Status\" = 'open' AND \"CashRegisterId\" IS NOT NULL");
        }
    }
}
