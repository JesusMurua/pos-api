using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace POS.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddModifierGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Create the new ProductModifierGroups table and its Product FK.
            migrationBuilder.CreateTable(
                name: "ProductModifierGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProductId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    MinSelectable = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    MaxSelectable = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductModifierGroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductModifierGroups_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductModifierGroups_ProductId",
                table: "ProductModifierGroups",
                column: "ProductId");

            // 2. Add the new columns on ProductExtras. ProductModifierGroupId is
            //    nullable for now so the backfill has somewhere to write before
            //    we tighten it to NOT NULL in step 5.
            migrationBuilder.AddColumn<int>(
                name: "ProductModifierGroupId",
                table: "ProductExtras",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "ProductExtras",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // 3. Backfill step A — one default "Modificadores" group per product
            //    that currently owns at least one extra. Products without extras
            //    get no group, so the migration is a no-op for them.
            migrationBuilder.Sql(@"
                INSERT INTO ""ProductModifierGroups"" (""ProductId"", ""Name"", ""SortOrder"", ""IsRequired"", ""MinSelectable"", ""MaxSelectable"")
                SELECT DISTINCT pe.""ProductId"", 'Modificadores', 0, false, 0, 99
                FROM ""ProductExtras"" pe;
            ");

            // 4. Backfill step B — re-point each extra at the default group that
            //    was just created for its product. The Name filter is defensive:
            //    the table was just created so there can't be other groups yet,
            //    but it makes intent explicit.
            migrationBuilder.Sql(@"
                UPDATE ""ProductExtras"" pe
                SET ""ProductModifierGroupId"" = g.""Id""
                FROM ""ProductModifierGroups"" g
                WHERE g.""ProductId"" = pe.""ProductId"" AND g.""Name"" = 'Modificadores';
            ");

            // 5. Every row now has a group — tighten the column.
            migrationBuilder.AlterColumn<int>(
                name: "ProductModifierGroupId",
                table: "ProductExtras",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            // 6. Drop the legacy Product-level FK/index/column from ProductExtras.
            migrationBuilder.DropForeignKey(
                name: "FK_ProductExtras_Products_ProductId",
                table: "ProductExtras");

            migrationBuilder.DropIndex(
                name: "IX_ProductExtras_ProductId",
                table: "ProductExtras");

            migrationBuilder.DropColumn(
                name: "ProductId",
                table: "ProductExtras");

            // 7. Wire ProductExtras to its new owner.
            migrationBuilder.CreateIndex(
                name: "IX_ProductExtras_ProductModifierGroupId",
                table: "ProductExtras",
                column: "ProductModifierGroupId");

            migrationBuilder.AddForeignKey(
                name: "FK_ProductExtras_ProductModifierGroups_ProductModifierGroupId",
                table: "ProductExtras",
                column: "ProductModifierGroupId",
                principalTable: "ProductModifierGroups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 1. Add ProductId back as nullable so the reverse backfill can write.
            migrationBuilder.AddColumn<int>(
                name: "ProductId",
                table: "ProductExtras",
                type: "integer",
                nullable: true);

            // 2. Reverse backfill — copy each extra's owning product id back
            //    from its modifier group, so extras that existed before the
            //    upgrade survive a downgrade unchanged. Extras created while
            //    on the new schema simply follow their group's product id.
            migrationBuilder.Sql(@"
                UPDATE ""ProductExtras"" pe
                SET ""ProductId"" = g.""ProductId""
                FROM ""ProductModifierGroups"" g
                WHERE g.""Id"" = pe.""ProductModifierGroupId"";
            ");

            migrationBuilder.AlterColumn<int>(
                name: "ProductId",
                table: "ProductExtras",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            // 3. Drop the new-schema FK/index/columns on ProductExtras.
            migrationBuilder.DropForeignKey(
                name: "FK_ProductExtras_ProductModifierGroups_ProductModifierGroupId",
                table: "ProductExtras");

            migrationBuilder.DropIndex(
                name: "IX_ProductExtras_ProductModifierGroupId",
                table: "ProductExtras");

            migrationBuilder.DropColumn(
                name: "ProductModifierGroupId",
                table: "ProductExtras");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "ProductExtras");

            // 4. Drop the groups table entirely.
            migrationBuilder.DropTable(
                name: "ProductModifierGroups");

            // 5. Restore the original Products -> ProductExtras link.
            migrationBuilder.CreateIndex(
                name: "IX_ProductExtras_ProductId",
                table: "ProductExtras",
                column: "ProductId");

            migrationBuilder.AddForeignKey(
                name: "FK_ProductExtras_Products_ProductId",
                table: "ProductExtras",
                column: "ProductId",
                principalTable: "Products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
