using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace POS.Repository.Migrations
{
    /// <summary>
    /// BDD-018: Removes the hardware identities (Kitchen, Kiosk) from the
    /// human <c>UserRole</c> enum and renumbers the remaining roles so the
    /// catalog stays compact (Owner=1, Manager=2, Cashier=3, Waiter=4,
    /// Host=5). Renumbering the <c>UserRoleCatalog.Id</c> primary key with
    /// dependent rows in <c>Users</c> is impossible under the existing
    /// <c>FK_Users_UserRoleCatalogs_RoleId</c> (ON UPDATE NO ACTION), so the
    /// FK is dropped, both sides are aligned, and the constraint is
    /// re-established with the same semantics it had before.
    /// </summary>
    /// <remarks>
    /// Down() restores the EF-managed seed (User Id=3 / Cocina) but does NOT
    /// resurrect the Kitchen/Kiosk catalog rows or undo the renumber — those
    /// are intentionally lossy under the BDD-018 contract. Reverting the
    /// migration is therefore a partial rollback aimed at restoring the seed
    /// user only; full rollback requires a fresh migration that re-inserts
    /// Kitchen/Kiosk and re-numbers Waiter/Host back to 5 and 7.
    /// </remarks>
    public partial class RemoveHardwareRolesAndRenumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── BDD-018 manual SQL (must run before EF auto-generated DeleteData) ──
            // Order is load-bearing: drop the Users→UserRoleCatalogs FK so we can
            // renumber the catalog PK without violating ON UPDATE NO ACTION, then
            // align Users.RoleId, then re-establish the constraint with its
            // original semantics. CASCADE on FK_UserBranches_Users_UserId means
            // the first DELETE also wipes the UserBranches row for User 3, so the
            // EF-generated DeleteData calls below become no-ops (safe).

            migrationBuilder.Sql("DELETE FROM \"Users\" WHERE \"RoleId\" IN (4, 6);");
            migrationBuilder.Sql("DELETE FROM \"UserRoleCatalogs\" WHERE \"Id\" IN (4, 6);");
            migrationBuilder.Sql("ALTER TABLE \"Users\" DROP CONSTRAINT \"FK_Users_UserRoleCatalogs_RoleId\";");
            migrationBuilder.Sql("UPDATE \"UserRoleCatalogs\" SET \"Id\" = 4, \"Level\" = 4 WHERE \"Code\" = 'Waiter';");
            migrationBuilder.Sql("UPDATE \"UserRoleCatalogs\" SET \"Id\" = 5, \"Level\" = 5 WHERE \"Code\" = 'Host';");
            migrationBuilder.Sql("UPDATE \"Users\" SET \"RoleId\" = 4 WHERE \"RoleId\" = 5;");
            migrationBuilder.Sql("UPDATE \"Users\" SET \"RoleId\" = 5 WHERE \"RoleId\" = 7;");
            migrationBuilder.Sql("ALTER TABLE \"Users\" ADD CONSTRAINT \"FK_Users_UserRoleCatalogs_RoleId\" FOREIGN KEY (\"RoleId\") REFERENCES \"UserRoleCatalogs\"(\"Id\") ON DELETE NO ACTION;");

            // ── EF auto-generated DeleteData (idempotent after the manual SQL) ──
            migrationBuilder.DeleteData(
                table: "UserBranches",
                keyColumns: new[] { "BranchId", "UserId" },
                keyValues: new object[] { 1, 3 });

            migrationBuilder.DeleteData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 3);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Users",
                columns: new[] { "Id", "BranchId", "BusinessId", "CreatedAt", "Email", "IsActive", "Name", "PasswordHash", "PinHash", "RoleId" },
                values: new object[] { 3, 1, 1, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, true, "Cocina", null, "$2a$11$1uDkWZWuha6zTWRnTY7Eke1GgFSozVZnRZZ8/ouAA6OdMOEp4k0sm", 4 });

            migrationBuilder.InsertData(
                table: "UserBranches",
                columns: new[] { "BranchId", "UserId", "IsDefault" },
                values: new object[] { 1, 3, true });
        }
    }
}
