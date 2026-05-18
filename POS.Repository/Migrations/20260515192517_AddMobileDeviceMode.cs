using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace POS.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddMobileDeviceMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "DeviceModeCatalogs",
                columns: new[] { "Code", "Name", "Description" },
                values: new object[] { "mobile", "Mobile POS / Mesero", "App móvil para meseros (BYOD)" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "DeviceModeCatalogs",
                keyColumn: "Code",
                keyValue: "mobile");
        }
    }
}
