using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mirage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCounsellorChargingRequest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ChargingRequested",
                schema: "mirage",
                table: "counsellors",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // AcceptsFreeSessions had no default and was never explicitly set on creation, so every
            // counsellor row has been sitting at false since the free-session feature was built —
            // meaning RecordCompletedFreeSession() (which only counts a session if AcceptsFreeSessions
            // is true) could never actually increment for anyone. Nothing could have legitimately
            // flipped this to false before now (the old app code always threw before allowing it while
            // IsEligibleToCharge was permanently false), so it's safe to backfill every row to true.
            migrationBuilder.Sql(
                "UPDATE mirage.counsellors SET \"AcceptsFreeSessions\" = TRUE WHERE \"AcceptsFreeSessions\" = FALSE;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ChargingRequested",
                schema: "mirage",
                table: "counsellors");
        }
    }
}
