using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace POS.Repository.Migrations
{
    /// <inheritdoc />
    public partial class CleanOrderDiscountsAndAddPromotionFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Remove legacy discount fields from Orders
            migrationBuilder.DropColumn(name: "DiscountCents", table: "Orders");
            migrationBuilder.DropColumn(name: "DiscountLabel", table: "Orders");
            migrationBuilder.DropColumn(name: "DiscountReason", table: "Orders");

            // SubtotalCents: change from nullable to non-nullable
            migrationBuilder.Sql(@"UPDATE ""Orders"" SET ""SubtotalCents"" = ""TotalCents"" WHERE ""SubtotalCents"" IS NULL;");

            migrationBuilder.AlterColumn<int>(
                name: "SubtotalCents",
                table: "Orders",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            // Add new promotion fields to Orders
            migrationBuilder.AddColumn<int>(
                name: "OrderDiscountCents",
                table: "Orders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TotalDiscountCents",
                table: "Orders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "OrderPromotionId",
                table: "Orders",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrderPromotionName",
                table: "Orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            // Add discount fields to OrderItems
            migrationBuilder.AddColumn<int>(
                name: "DiscountCents",
                table: "OrderItems",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PromotionId",
                table: "OrderItems",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PromotionName",
                table: "OrderItems",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Remove new OrderItem fields
            migrationBuilder.DropColumn(name: "DiscountCents", table: "OrderItems");
            migrationBuilder.DropColumn(name: "PromotionId", table: "OrderItems");
            migrationBuilder.DropColumn(name: "PromotionName", table: "OrderItems");

            // Remove new Order fields
            migrationBuilder.DropColumn(name: "OrderDiscountCents", table: "Orders");
            migrationBuilder.DropColumn(name: "TotalDiscountCents", table: "Orders");
            migrationBuilder.DropColumn(name: "OrderPromotionId", table: "Orders");
            migrationBuilder.DropColumn(name: "OrderPromotionName", table: "Orders");

            // Restore SubtotalCents to nullable
            migrationBuilder.AlterColumn<int>(
                name: "SubtotalCents",
                table: "Orders",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            // Restore legacy fields
            migrationBuilder.AddColumn<int>(
                name: "DiscountCents",
                table: "Orders",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DiscountLabel",
                table: "Orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DiscountReason",
                table: "Orders",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }
    }
}
