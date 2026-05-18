using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace POS.Repository.Migrations
{
    /// <inheritdoc />
    public partial class FreezeProductTypeInOrderItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProductType",
                table: "OrderItems",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Standard");

            // Backfill historical OrderItems with the current Product.Type via JOIN.
            // Idempotent: filtered by ProductType='Standard' so re-runs are safe.
            // Placed BEFORE CreateIndex to avoid touching index pages per UPDATE row;
            // the index is built once over already-classified data.
            // Webhook-ingested items (ProductId=0, no matching Product) keep Standard.
            migrationBuilder.Sql(@"
                UPDATE ""OrderItems""
                SET ""ProductType"" = p.""Type""
                FROM ""Products"" p
                WHERE ""OrderItems"".""ProductId"" = p.""Id""
                  AND ""OrderItems"".""ProductType"" = 'Standard';
            ");

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_ProductType",
                table: "OrderItems",
                column: "ProductType");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OrderItems_ProductType",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "ProductType",
                table: "OrderItems");
        }
    }
}
