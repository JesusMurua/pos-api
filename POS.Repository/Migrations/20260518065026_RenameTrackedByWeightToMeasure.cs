using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace POS.Repository.Migrations
{
    /// <inheritdoc />
    public partial class RenameTrackedByWeightToMeasure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Idempotent string rename — filtered by old value so re-runs are safe.
            // Historic migrations (20260516024801, 20260518053728) seeded
            // 'TrackedByWeight'; this rename brings both tables into the new
            // semantic naming ('TrackedByMeasure' covers Weight, Volume, Length, Area).
            migrationBuilder.Sql(@"
                UPDATE ""Products""
                SET ""Type"" = 'TrackedByMeasure'
                WHERE ""Type"" = 'TrackedByWeight';

                UPDATE ""OrderItems""
                SET ""ProductType"" = 'TrackedByMeasure'
                WHERE ""ProductType"" = 'TrackedByWeight';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Idempotent reverse rename — filtered by new value so re-runs are safe.
            migrationBuilder.Sql(@"
                UPDATE ""Products""
                SET ""Type"" = 'TrackedByWeight'
                WHERE ""Type"" = 'TrackedByMeasure';

                UPDATE ""OrderItems""
                SET ""ProductType"" = 'TrackedByWeight'
                WHERE ""ProductType"" = 'TrackedByMeasure';
            ");
        }
    }
}
