using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mirage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserDeletionMarker : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                schema: "mirage",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Previous releases represented deletion by scrubbing the profile without a
            // dedicated account flag. Backfill only that complete sentinel combination so
            // ordinary suspended accounts remain visible and recoverable by administrators.
            migrationBuilder.Sql("""
                UPDATE mirage."AspNetUsers" AS users
                SET "IsDeleted" = TRUE
                WHERE users."IsActive" = FALSE
                  AND EXISTS (
                    SELECT 1
                    FROM mirage.profiles AS profile
                    WHERE profile."UserId" = users."Id"
                      AND profile."DisplayName" = 'Deleted user'
                      AND profile."AnonymityEnabled" = TRUE
                      AND profile."Bio" = ''
                      AND profile."AvatarUrl" IS NULL
                  );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsDeleted",
                schema: "mirage",
                table: "AspNetUsers");
        }
    }
}
