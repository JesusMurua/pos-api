using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace POS.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddLogisticsOperationsCatalogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ═══════════════════════════════════════════════════════════════
            // Phase 1: Create 2 new catalog tables
            // ═══════════════════════════════════════════════════════════════

            migrationBuilder.CreateTable(
                name: "InventoryMovementTypeCatalogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryMovementTypeCatalogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TableStatusCatalogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TableStatusCatalogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(name: "IX_InventoryMovementTypeCatalogs_Code", table: "InventoryMovementTypeCatalogs", column: "Code", unique: true);
            migrationBuilder.CreateIndex(name: "IX_TableStatusCatalogs_Code", table: "TableStatusCatalogs", column: "Code", unique: true);

            // ═══════════════════════════════════════════════════════════════
            // Phase 2: Seed catalog rows (+ ensure existing catalogs have correct IDs)
            // ═══════════════════════════════════════════════════════════════

            migrationBuilder.InsertData(
                table: "InventoryMovementTypeCatalogs",
                columns: new[] { "Id", "Code", "Name" },
                values: new object[,]
                {
                    { 1, "in", "Entrada" },
                    { 2, "out", "Salida" },
                    { 3, "adjustment", "Ajuste" }
                });

            migrationBuilder.InsertData(
                table: "TableStatusCatalogs",
                columns: new[] { "Id", "Code", "Name" },
                values: new object[,]
                {
                    { 1, "available", "Disponible" },
                    { 2, "occupied", "Ocupada" },
                    { 3, "reserved", "Reservada" },
                    { 4, "maintenance", "Mantenimiento" }
                });

            // Ensure KitchenStatusCatalogs and OrderSyncStatusCatalogs have correct IDs
            // (they were seeded without explicit IDs before — ensure rows exist)
            migrationBuilder.Sql("""
                INSERT INTO "KitchenStatusCatalogs" ("Id", "Code", "Name", "Color", "SortOrder")
                VALUES (1, 'Pending', 'En cocina', '#F59E0B', 1),
                       (2, 'Ready', 'Listo', '#10B981', 2),
                       (3, 'Delivered', 'Entregado', '#3B82F6', 3)
                ON CONFLICT ("Id") DO NOTHING;

                INSERT INTO "OrderSyncStatusCatalogs" ("Id", "Code", "Name")
                VALUES (1, 'Pending', 'Pendiente'),
                       (2, 'Synced', 'Sincronizado'),
                       (3, 'Failed', 'Error')
                ON CONFLICT ("Id") DO NOTHING;
                """);

            // ═══════════════════════════════════════════════════════════════
            // Phase 3: Add new integer FK columns (nullable initially)
            // ═══════════════════════════════════════════════════════════════

            migrationBuilder.AddColumn<int>(
                name: "TableStatusId",
                table: "RestaurantTables",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "KitchenStatusId",
                table: "Orders",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SyncStatusId",
                table: "Orders",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InventoryMovementTypeId",
                table: "InventoryMovements",
                type: "integer",
                nullable: true);

            // ═══════════════════════════════════════════════════════════════
            // Phase 4: Backfill old string/enum values → new integer IDs
            // ═══════════════════════════════════════════════════════════════

            migrationBuilder.Sql("""
                UPDATE "RestaurantTables" SET "TableStatusId" = CASE
                    WHEN "Status" = 'available' THEN 1
                    WHEN "Status" = 'occupied'  THEN 2
                    WHEN "Status" = 'reserved'  THEN 3
                    ELSE 1
                END;
                """);

            migrationBuilder.Sql("""
                UPDATE "Orders" SET "KitchenStatusId" = CASE
                    WHEN "KitchenStatus" = 'Pending'   THEN 1
                    WHEN "KitchenStatus" = 'Ready'     THEN 2
                    WHEN "KitchenStatus" = 'Delivered'  THEN 3
                    ELSE 1
                END;
                """);

            migrationBuilder.Sql("""
                UPDATE "Orders" SET "SyncStatusId" = CASE
                    WHEN "SyncStatus" = 'Pending' THEN 1
                    WHEN "SyncStatus" = 'Synced'  THEN 2
                    WHEN "SyncStatus" = 'Failed'  THEN 3
                    ELSE 1
                END;
                """);

            migrationBuilder.Sql("""
                UPDATE "InventoryMovements" SET "InventoryMovementTypeId" = CASE
                    WHEN "Type" = 'in'         THEN 1
                    WHEN "Type" = 'out'        THEN 2
                    WHEN "Type" = 'adjustment' THEN 3
                    ELSE 1
                END;
                """);

            // ═══════════════════════════════════════════════════════════════
            // Phase 5: Make columns NOT NULL (except KitchenStatusId stays nullable)
            // ═══════════════════════════════════════════════════════════════

            migrationBuilder.AlterColumn<int>(
                name: "TableStatusId",
                table: "RestaurantTables",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AlterColumn<int>(
                name: "SyncStatusId",
                table: "Orders",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AlterColumn<int>(
                name: "InventoryMovementTypeId",
                table: "InventoryMovements",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            // KitchenStatusId stays nullable — no AlterColumn needed

            // ═══════════════════════════════════════════════════════════════
            // Phase 6: Drop old string columns and their indexes
            // ═══════════════════════════════════════════════════════════════

            migrationBuilder.DropIndex(name: "IX_Orders_SyncStatus", table: "Orders");

            migrationBuilder.DropColumn(name: "Status", table: "RestaurantTables");
            migrationBuilder.DropColumn(name: "KitchenStatus", table: "Orders");
            migrationBuilder.DropColumn(name: "SyncStatus", table: "Orders");
            migrationBuilder.DropColumn(name: "Type", table: "InventoryMovements");

            // ═══════════════════════════════════════════════════════════════
            // Phase 7: Create indexes and FK constraints
            // ═══════════════════════════════════════════════════════════════

            migrationBuilder.CreateIndex(name: "IX_RestaurantTables_TableStatusId", table: "RestaurantTables", column: "TableStatusId");
            migrationBuilder.CreateIndex(name: "IX_Orders_KitchenStatusId", table: "Orders", column: "KitchenStatusId");
            migrationBuilder.CreateIndex(name: "IX_Orders_SyncStatusId", table: "Orders", column: "SyncStatusId");
            migrationBuilder.CreateIndex(name: "IX_InventoryMovements_InventoryMovementTypeId", table: "InventoryMovements", column: "InventoryMovementTypeId");

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryMovements_InventoryMovementTypeCatalogs_InventoryM~",
                table: "InventoryMovements", column: "InventoryMovementTypeId",
                principalTable: "InventoryMovementTypeCatalogs", principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_KitchenStatusCatalogs_KitchenStatusId",
                table: "Orders", column: "KitchenStatusId",
                principalTable: "KitchenStatusCatalogs", principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_OrderSyncStatusCatalogs_SyncStatusId",
                table: "Orders", column: "SyncStatusId",
                principalTable: "OrderSyncStatusCatalogs", principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RestaurantTables_TableStatusCatalogs_TableStatusId",
                table: "RestaurantTables", column: "TableStatusId",
                principalTable: "TableStatusCatalogs", principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Reset identity sequences
            migrationBuilder.Sql("""
                SELECT setval(pg_get_serial_sequence('"InventoryMovementTypeCatalogs"', 'Id'),
                    GREATEST((SELECT MAX("Id") FROM "InventoryMovementTypeCatalogs") + 1,
                    nextval(pg_get_serial_sequence('"InventoryMovementTypeCatalogs"', 'Id'))), false);
                SELECT setval(pg_get_serial_sequence('"TableStatusCatalogs"', 'Id'),
                    GREATEST((SELECT MAX("Id") FROM "TableStatusCatalogs") + 1,
                    nextval(pg_get_serial_sequence('"TableStatusCatalogs"', 'Id'))), false);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(name: "FK_InventoryMovements_InventoryMovementTypeCatalogs_InventoryM~", table: "InventoryMovements");
            migrationBuilder.DropForeignKey(name: "FK_Orders_KitchenStatusCatalogs_KitchenStatusId", table: "Orders");
            migrationBuilder.DropForeignKey(name: "FK_Orders_OrderSyncStatusCatalogs_SyncStatusId", table: "Orders");
            migrationBuilder.DropForeignKey(name: "FK_RestaurantTables_TableStatusCatalogs_TableStatusId", table: "RestaurantTables");

            migrationBuilder.DropTable(name: "InventoryMovementTypeCatalogs");
            migrationBuilder.DropTable(name: "TableStatusCatalogs");

            migrationBuilder.DropIndex(name: "IX_RestaurantTables_TableStatusId", table: "RestaurantTables");
            migrationBuilder.DropIndex(name: "IX_Orders_KitchenStatusId", table: "Orders");
            migrationBuilder.DropIndex(name: "IX_Orders_SyncStatusId", table: "Orders");
            migrationBuilder.DropIndex(name: "IX_InventoryMovements_InventoryMovementTypeId", table: "InventoryMovements");

            migrationBuilder.DropColumn(name: "TableStatusId", table: "RestaurantTables");
            migrationBuilder.DropColumn(name: "KitchenStatusId", table: "Orders");
            migrationBuilder.DropColumn(name: "SyncStatusId", table: "Orders");
            migrationBuilder.DropColumn(name: "InventoryMovementTypeId", table: "InventoryMovements");

            migrationBuilder.AddColumn<string>(name: "Status", table: "RestaurantTables", type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "");
            migrationBuilder.AddColumn<string>(name: "KitchenStatus", table: "Orders", type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Pending");
            migrationBuilder.AddColumn<string>(name: "SyncStatus", table: "Orders", type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "");
            migrationBuilder.AddColumn<string>(name: "Type", table: "InventoryMovements", type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "");

            migrationBuilder.CreateIndex(name: "IX_Orders_SyncStatus", table: "Orders", column: "SyncStatus");
        }
    }
}
