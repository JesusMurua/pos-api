using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace POS.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddCategoriesAndProductsSeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Categories",
                columns: new[] { "Id", "BranchId", "Icon", "IsActive", "Name", "SortOrder" },
                values: new object[,]
                {
                    { 1, 1, "pi-shopping-bag", true, "Comida", 1 },
                    { 2, 1, "pi-star", true, "Antojitos", 2 },
                    { 3, 1, "pi-filter", true, "Bebidas", 3 },
                    { 4, 1, "pi-heart", true, "Postres", 4 }
                });

            migrationBuilder.InsertData(
                table: "Products",
                columns: new[] { "Id", "CategoryId", "ImageUrl", "IsAvailable", "IsPopular", "Name", "PriceCents" },
                values: new object[,]
                {
                    { 1, 1, null, true, false, "Torta de Milanesa", 8500 },
                    { 2, 1, null, true, false, "Quesadilla", 5500 },
                    { 3, 1, null, true, false, "Enchiladas Verdes", 7500 },
                    { 4, 1, null, true, false, "Pozole Rojo", 9000 },
                    { 5, 2, null, true, false, "Taco de Canasta", 2000 },
                    { 6, 2, null, true, false, "Gordita", 3500 },
                    { 7, 2, null, true, false, "Tostada de Tinga", 3000 },
                    { 8, 3, null, true, false, "Agua de Jamaica", 2500 },
                    { 9, 3, null, true, false, "Café de Olla", 3000 },
                    { 10, 3, null, true, false, "Refresco", 2500 },
                    { 11, 4, null, true, false, "Arroz con Leche", 4000 },
                    { 12, 4, null, true, false, "Gelatina", 2500 }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Products",
                keyColumn: "Id",
                keyValue: 1);

            migrationBuilder.DeleteData(
                table: "Products",
                keyColumn: "Id",
                keyValue: 2);

            migrationBuilder.DeleteData(
                table: "Products",
                keyColumn: "Id",
                keyValue: 3);

            migrationBuilder.DeleteData(
                table: "Products",
                keyColumn: "Id",
                keyValue: 4);

            migrationBuilder.DeleteData(
                table: "Products",
                keyColumn: "Id",
                keyValue: 5);

            migrationBuilder.DeleteData(
                table: "Products",
                keyColumn: "Id",
                keyValue: 6);

            migrationBuilder.DeleteData(
                table: "Products",
                keyColumn: "Id",
                keyValue: 7);

            migrationBuilder.DeleteData(
                table: "Products",
                keyColumn: "Id",
                keyValue: 8);

            migrationBuilder.DeleteData(
                table: "Products",
                keyColumn: "Id",
                keyValue: 9);

            migrationBuilder.DeleteData(
                table: "Products",
                keyColumn: "Id",
                keyValue: 10);

            migrationBuilder.DeleteData(
                table: "Products",
                keyColumn: "Id",
                keyValue: 11);

            migrationBuilder.DeleteData(
                table: "Products",
                keyColumn: "Id",
                keyValue: 12);

            migrationBuilder.DeleteData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: 1);

            migrationBuilder.DeleteData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: 2);

            migrationBuilder.DeleteData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: 3);

            migrationBuilder.DeleteData(
                table: "Categories",
                keyColumn: "Id",
                keyValue: 4);
        }
    }
}
