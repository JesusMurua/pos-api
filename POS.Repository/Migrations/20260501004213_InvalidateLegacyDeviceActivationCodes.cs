using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace POS.Repository.Migrations
{
    /// <summary>
    /// Marks all in-flight legacy numeric activation codes as consumed.
    /// Required by BDD-016 because the new alphabet excludes digits 0 and 1,
    /// so legacy 6-digit numeric codes can never satisfy the tightened
    /// <c>ActivateDeviceRequest.Code</c> regex (<c>(?i)^[A-HJKMNP-TV-Z2-9]{6}$</c>)
    /// and would otherwise sit in the table un-redeemable until natural
    /// expiry (24 h). Marking them <c>IsUsed = true</c> preserves the audit
    /// trail (<c>Code</c>, <c>CreatedBy</c>, <c>CreatedAt</c>) while making
    /// the new regex contract authoritative from the first request after
    /// deploy. The schema itself is untouched — the column was created as
    /// <c>character varying(6) NOT NULL</c> in
    /// <c>20260327204815_AddDeviceActivationCodes</c>.
    /// </summary>
    public partial class InvalidateLegacyDeviceActivationCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE \"DeviceActivationCodes\" SET \"IsUsed\" = true, \"UsedAt\" = NOW() WHERE \"IsUsed\" = false;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Irreversible by design: previously-pending codes cannot be
            // safely "unconsumed" without reintroducing the legacy contract.
            // Issuing fresh codes is cheap; reverting this migration in
            // production is not a recovery path we want to encourage.
        }
    }
}
