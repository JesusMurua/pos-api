using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace POS.Repository.Migrations
{
    /// <inheritdoc />
    public partial class AddConcurrencyTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // No-op: xmin is a PostgreSQL system column present on every table.
            // This migration exists to sync the EF Core model snapshot, which
            // configures xmin as a concurrency token via shadow property.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op: xmin is a system column and cannot be dropped.
        }
    }
}
