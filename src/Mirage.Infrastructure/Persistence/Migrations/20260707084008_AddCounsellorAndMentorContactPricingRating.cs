using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mirage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCounsellorAndMentorContactPricingRating : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PhoneNumber",
                schema: "mirage",
                table: "mentors",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "AverageRating",
                schema: "mirage",
                table: "counsellors",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<string>(
                name: "PhoneNumber",
                schema: "mirage",
                table: "counsellors",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PriceAmount",
                schema: "mirage",
                table: "counsellors",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PriceCurrency",
                schema: "mirage",
                table: "counsellors",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RatingCount",
                schema: "mirage",
                table: "counsellors",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "SupportsVideoCalls",
                schema: "mirage",
                table: "counsellors",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SupportsVoiceCalls",
                schema: "mirage",
                table: "counsellors",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // The checked-in model snapshot had drifted from the actual schema (communities
            // tables/indexes and this index were already applied by earlier hand-written
            // migrations — see RepairMissingCommunitiesTables, AddCommunityAvatarsAndEngagement,
            // AddCommunityCommentLikesAndMentions). Only IX_counselling_sessions_PartnerUserId
            // was genuinely never created (AddCounsellingSessionPartner added the column via raw
            // SQL but skipped the FK index) — add it idempotently here instead of recreating
            // tables that already exist.
            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS "IX_counselling_sessions_PartnerUserId"
                    ON mirage.counselling_sessions ("PartnerUserId");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS mirage."IX_counselling_sessions_PartnerUserId";
                """);

            migrationBuilder.DropColumn(
                name: "PhoneNumber",
                schema: "mirage",
                table: "mentors");

            migrationBuilder.DropColumn(
                name: "AverageRating",
                schema: "mirage",
                table: "counsellors");

            migrationBuilder.DropColumn(
                name: "PhoneNumber",
                schema: "mirage",
                table: "counsellors");

            migrationBuilder.DropColumn(
                name: "PriceAmount",
                schema: "mirage",
                table: "counsellors");

            migrationBuilder.DropColumn(
                name: "PriceCurrency",
                schema: "mirage",
                table: "counsellors");

            migrationBuilder.DropColumn(
                name: "RatingCount",
                schema: "mirage",
                table: "counsellors");

            migrationBuilder.DropColumn(
                name: "SupportsVideoCalls",
                schema: "mirage",
                table: "counsellors");

            migrationBuilder.DropColumn(
                name: "SupportsVoiceCalls",
                schema: "mirage",
                table: "counsellors");
        }
    }
}
