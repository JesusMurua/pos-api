using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace POS.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddConfigurationCatalogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ═══════════════════════════════════════════════════════════════
            // Phase 1: Ensure catalog IDs are correct (upsert with ON CONFLICT)
            // ═══════════════════════════════════════════════════════════════

            migrationBuilder.Sql("""
                INSERT INTO "UserRoleCatalogs" ("Id", "Code", "Name", "Level")
                VALUES (1, 'Owner', 'Dueño', 1), (2, 'Manager', 'Gerente', 2),
                       (3, 'Cashier', 'Cajero', 3), (4, 'Kitchen', 'Cocina', 4),
                       (5, 'Waiter', 'Mesero', 5), (6, 'Kiosk', 'Kiosk', 6),
                       (7, 'Host', 'Hostess', 7)
                ON CONFLICT ("Id") DO NOTHING;

                INSERT INTO "PlanTypeCatalogs" ("Id", "Code", "Name", "SortOrder")
                VALUES (1, 'Free', 'Gratis', 0), (2, 'Basic', 'Básico', 1),
                       (3, 'Pro', 'Pro', 2), (4, 'Enterprise', 'Enterprise', 3)
                ON CONFLICT ("Id") DO NOTHING;

                INSERT INTO "PromotionTypeCatalogs" ("Id", "Code", "Name", "SortOrder")
                VALUES (1, 'Percentage', 'Descuento porcentaje', 1), (2, 'Fixed', 'Descuento fijo', 2),
                       (3, 'Bogo', '2x1', 3), (4, 'Bundle', 'Paquete', 4),
                       (5, 'OrderDiscount', 'Descuento en orden', 5), (6, 'FreeProduct', 'Producto gratis', 6)
                ON CONFLICT ("Id") DO NOTHING;
                """);

            // ═══════════════════════════════════════════════════════════════
            // Phase 2: Add new integer FK columns (nullable initially)
            // ═══════════════════════════════════════════════════════════════

            migrationBuilder.AddColumn<int>(name: "RoleId", table: "Users", type: "integer", nullable: true);
            migrationBuilder.AddColumn<int>(name: "PlanTypeId", table: "Subscriptions", type: "integer", nullable: true);
            migrationBuilder.AddColumn<int>(name: "PromotionTypeId", table: "Promotions", type: "integer", nullable: true);
            migrationBuilder.AddColumn<int>(name: "BusinessTypeId", table: "BusinessGiros", type: "integer", nullable: true);
            migrationBuilder.AddColumn<int>(name: "BusinessTypeId", table: "Businesses", type: "integer", nullable: true);
            migrationBuilder.AddColumn<int>(name: "PlanTypeId", table: "Businesses", type: "integer", nullable: true);

            // ═══════════════════════════════════════════════════════════════
            // Phase 3: Backfill from old string/enum columns → new integer IDs
            // ═══════════════════════════════════════════════════════════════

            migrationBuilder.Sql("""
                UPDATE "Users" SET "RoleId" = CASE
                    WHEN "Role" = 'Owner' THEN 1
                    WHEN "Role" = 'Manager' THEN 2
                    WHEN "Role" = 'Cashier' THEN 3
                    WHEN "Role" = 'Kitchen' THEN 4
                    WHEN "Role" = 'Waiter' THEN 5
                    WHEN "Role" = 'Kiosk' THEN 6
                    WHEN "Role" = 'Host' THEN 7
                    ELSE 3
                END;
                """);

            migrationBuilder.Sql("""
                UPDATE "Businesses" SET "PlanTypeId" = CASE
                    WHEN "PlanType" = 'Free' THEN 1
                    WHEN "PlanType" = 'Basic' THEN 2
                    WHEN "PlanType" = 'Pro' THEN 3
                    WHEN "PlanType" = 'Enterprise' THEN 4
                    ELSE 1
                END;
                """);

            migrationBuilder.Sql("""
                UPDATE "Businesses" b SET "BusinessTypeId" = COALESCE(
                    (SELECT c."Id" FROM "BusinessTypeCatalogs" c WHERE c."Code" = b."BusinessType"),
                    11
                );
                """);

            migrationBuilder.Sql("""
                UPDATE "Subscriptions" SET "PlanTypeId" = CASE
                    WHEN "PlanType" = 'Free' THEN 1
                    WHEN "PlanType" = 'Basic' THEN 2
                    WHEN "PlanType" = 'Pro' THEN 3
                    WHEN "PlanType" = 'Enterprise' THEN 4
                    ELSE 1
                END;
                """);

            migrationBuilder.Sql("""
                UPDATE "Promotions" SET "PromotionTypeId" = CASE
                    WHEN "Type" = 'Percentage' THEN 1
                    WHEN "Type" = 'Fixed' THEN 2
                    WHEN "Type" = 'Bogo' THEN 3
                    WHEN "Type" = 'Bundle' THEN 4
                    WHEN "Type" = 'OrderDiscount' THEN 5
                    WHEN "Type" = 'FreeProduct' THEN 6
                    ELSE 1
                END;
                """);

            migrationBuilder.Sql("""
                UPDATE "BusinessGiros" bg SET "BusinessTypeId" = COALESCE(
                    (SELECT c."Id" FROM "BusinessTypeCatalogs" c WHERE c."Code" = bg."CatalogCode"),
                    11
                );
                """);

            // ═══════════════════════════════════════════════════════════════
            // Phase 4: Make columns NOT NULL
            // ═══════════════════════════════════════════════════════════════

            migrationBuilder.AlterColumn<int>(name: "RoleId", table: "Users", type: "integer", nullable: false, defaultValue: 3);
            migrationBuilder.AlterColumn<int>(name: "PlanTypeId", table: "Subscriptions", type: "integer", nullable: false, defaultValue: 1);
            migrationBuilder.AlterColumn<int>(name: "PromotionTypeId", table: "Promotions", type: "integer", nullable: false, defaultValue: 1);
            migrationBuilder.AlterColumn<int>(name: "BusinessTypeId", table: "BusinessGiros", type: "integer", nullable: false, defaultValue: 11);
            migrationBuilder.AlterColumn<int>(name: "BusinessTypeId", table: "Businesses", type: "integer", nullable: false, defaultValue: 11);
            migrationBuilder.AlterColumn<int>(name: "PlanTypeId", table: "Businesses", type: "integer", nullable: false, defaultValue: 1);

            // ═══════════════════════════════════════════════════════════════
            // Phase 5: Drop old string columns and their indexes/constraints
            // ═══════════════════════════════════════════════════════════════

            migrationBuilder.DropForeignKey(name: "FK_BusinessGiros_BusinessTypeCatalogs_CatalogCode", table: "BusinessGiros");
            migrationBuilder.DropUniqueConstraint(name: "AK_BusinessTypeCatalogs_Code", table: "BusinessTypeCatalogs");
            migrationBuilder.DropIndex(name: "IX_BusinessGiros_BusinessId_CatalogCode", table: "BusinessGiros");
            migrationBuilder.DropIndex(name: "IX_BusinessGiros_CatalogCode", table: "BusinessGiros");

            migrationBuilder.DropColumn(name: "Role", table: "Users");
            migrationBuilder.DropColumn(name: "PlanType", table: "Subscriptions");
            migrationBuilder.DropColumn(name: "Type", table: "Promotions");
            migrationBuilder.DropColumn(name: "CatalogCode", table: "BusinessGiros");
            migrationBuilder.DropColumn(name: "BusinessType", table: "Businesses");
            migrationBuilder.DropColumn(name: "PlanType", table: "Businesses");

            // ═══════════════════════════════════════════════════════════════
            // Phase 6: Update seed data for HasData entities
            // ═══════════════════════════════════════════════════════════════

            migrationBuilder.UpdateData(table: "Businesses", keyColumn: "Id", keyValue: 1,
                columns: new[] { "BusinessTypeId", "PlanTypeId" }, values: new object[] { 1, 2 });
            migrationBuilder.UpdateData(table: "Users", keyColumn: "Id", keyValue: 1, column: "RoleId", value: 1);
            migrationBuilder.UpdateData(table: "Users", keyColumn: "Id", keyValue: 2, column: "RoleId", value: 3);
            migrationBuilder.UpdateData(table: "Users", keyColumn: "Id", keyValue: 3, column: "RoleId", value: 4);

            // ═══════════════════════════════════════════════════════════════
            // Phase 7: Create indexes and FK constraints
            // ═══════════════════════════════════════════════════════════════

            migrationBuilder.CreateIndex(name: "IX_Users_RoleId", table: "Users", column: "RoleId");
            migrationBuilder.CreateIndex(name: "IX_Subscriptions_PlanTypeId", table: "Subscriptions", column: "PlanTypeId");
            migrationBuilder.CreateIndex(name: "IX_Promotions_PromotionTypeId", table: "Promotions", column: "PromotionTypeId");
            migrationBuilder.CreateIndex(name: "IX_BusinessGiros_BusinessId_BusinessTypeId", table: "BusinessGiros",
                columns: new[] { "BusinessId", "BusinessTypeId" }, unique: true);
            migrationBuilder.CreateIndex(name: "IX_BusinessGiros_BusinessTypeId", table: "BusinessGiros", column: "BusinessTypeId");
            migrationBuilder.CreateIndex(name: "IX_Businesses_BusinessTypeId", table: "Businesses", column: "BusinessTypeId");
            migrationBuilder.CreateIndex(name: "IX_Businesses_PlanTypeId", table: "Businesses", column: "PlanTypeId");

            migrationBuilder.AddForeignKey(name: "FK_Users_UserRoleCatalogs_RoleId",
                table: "Users", column: "RoleId", principalTable: "UserRoleCatalogs", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(name: "FK_Businesses_BusinessTypeCatalogs_BusinessTypeId",
                table: "Businesses", column: "BusinessTypeId", principalTable: "BusinessTypeCatalogs", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(name: "FK_Businesses_PlanTypeCatalogs_PlanTypeId",
                table: "Businesses", column: "PlanTypeId", principalTable: "PlanTypeCatalogs", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(name: "FK_BusinessGiros_BusinessTypeCatalogs_BusinessTypeId",
                table: "BusinessGiros", column: "BusinessTypeId", principalTable: "BusinessTypeCatalogs", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(name: "FK_Promotions_PromotionTypeCatalogs_PromotionTypeId",
                table: "Promotions", column: "PromotionTypeId", principalTable: "PromotionTypeCatalogs", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(name: "FK_Subscriptions_PlanTypeCatalogs_PlanTypeId",
                table: "Subscriptions", column: "PlanTypeId", principalTable: "PlanTypeCatalogs", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(name: "FK_Businesses_BusinessTypeCatalogs_BusinessTypeId", table: "Businesses");
            migrationBuilder.DropForeignKey(name: "FK_Businesses_PlanTypeCatalogs_PlanTypeId", table: "Businesses");
            migrationBuilder.DropForeignKey(name: "FK_BusinessGiros_BusinessTypeCatalogs_BusinessTypeId", table: "BusinessGiros");
            migrationBuilder.DropForeignKey(name: "FK_Promotions_PromotionTypeCatalogs_PromotionTypeId", table: "Promotions");
            migrationBuilder.DropForeignKey(name: "FK_Subscriptions_PlanTypeCatalogs_PlanTypeId", table: "Subscriptions");
            migrationBuilder.DropForeignKey(name: "FK_Users_UserRoleCatalogs_RoleId", table: "Users");

            migrationBuilder.DropIndex(name: "IX_Users_RoleId", table: "Users");
            migrationBuilder.DropIndex(name: "IX_Subscriptions_PlanTypeId", table: "Subscriptions");
            migrationBuilder.DropIndex(name: "IX_Promotions_PromotionTypeId", table: "Promotions");
            migrationBuilder.DropIndex(name: "IX_BusinessGiros_BusinessId_BusinessTypeId", table: "BusinessGiros");
            migrationBuilder.DropIndex(name: "IX_BusinessGiros_BusinessTypeId", table: "BusinessGiros");
            migrationBuilder.DropIndex(name: "IX_Businesses_BusinessTypeId", table: "Businesses");
            migrationBuilder.DropIndex(name: "IX_Businesses_PlanTypeId", table: "Businesses");

            migrationBuilder.DropColumn(name: "RoleId", table: "Users");
            migrationBuilder.DropColumn(name: "PlanTypeId", table: "Subscriptions");
            migrationBuilder.DropColumn(name: "PromotionTypeId", table: "Promotions");
            migrationBuilder.DropColumn(name: "BusinessTypeId", table: "BusinessGiros");
            migrationBuilder.DropColumn(name: "BusinessTypeId", table: "Businesses");
            migrationBuilder.DropColumn(name: "PlanTypeId", table: "Businesses");

            migrationBuilder.AddColumn<string>(name: "Role", table: "Users", type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "");
            migrationBuilder.AddColumn<string>(name: "PlanType", table: "Subscriptions", type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "");
            migrationBuilder.AddColumn<string>(name: "Type", table: "Promotions", type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "");
            migrationBuilder.AddColumn<string>(name: "CatalogCode", table: "BusinessGiros", type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "");
            migrationBuilder.AddColumn<string>(name: "BusinessType", table: "Businesses", type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "");
            migrationBuilder.AddColumn<string>(name: "PlanType", table: "Businesses", type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "");

            migrationBuilder.AddUniqueConstraint(name: "AK_BusinessTypeCatalogs_Code", table: "BusinessTypeCatalogs", column: "Code");
            migrationBuilder.UpdateData(table: "Businesses", keyColumn: "Id", keyValue: 1, columns: new[] { "BusinessType", "PlanType" }, values: new object[] { "Restaurant", "Basic" });
            migrationBuilder.UpdateData(table: "Users", keyColumn: "Id", keyValue: 1, column: "Role", value: "Owner");
            migrationBuilder.UpdateData(table: "Users", keyColumn: "Id", keyValue: 2, column: "Role", value: "Cashier");
            migrationBuilder.UpdateData(table: "Users", keyColumn: "Id", keyValue: 3, column: "Role", value: "Kitchen");

            migrationBuilder.CreateIndex(name: "IX_BusinessGiros_BusinessId_CatalogCode", table: "BusinessGiros", columns: new[] { "BusinessId", "CatalogCode" }, unique: true);
            migrationBuilder.CreateIndex(name: "IX_BusinessGiros_CatalogCode", table: "BusinessGiros", column: "CatalogCode");
            migrationBuilder.AddForeignKey(name: "FK_BusinessGiros_BusinessTypeCatalogs_CatalogCode", table: "BusinessGiros", column: "CatalogCode", principalTable: "BusinessTypeCatalogs", principalColumn: "Code", onDelete: ReferentialAction.Restrict);
        }
    }
}
