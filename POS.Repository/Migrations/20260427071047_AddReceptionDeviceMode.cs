using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace POS.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddReceptionDeviceMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "DeviceModeCatalogs",
                columns: new[] { "Code", "Name", "Description" },
                values: new object[] { "reception", "Recepción", "Control de acceso (gym)" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "DeviceModeCatalogs",
                keyColumn: "Code",
                keyValue: "reception");
        }
    }
}
