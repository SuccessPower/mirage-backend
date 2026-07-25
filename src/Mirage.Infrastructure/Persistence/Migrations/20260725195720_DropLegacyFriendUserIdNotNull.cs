using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mirage.Infrastructure.Persistence.Migrations
{
    // LegacyFriendUserId (see MergeCoupleFriendshipsByCouplePair) is a historical, EF-untracked
    // column — the app never writes to it for new rows. The prior migration renamed it but left
    // its original NOT NULL constraint in place, so every new CoupleFriendship insert has been
    // failing with a not-null violation ("...befriend" returning 500) since that migration ran.
    /// <inheritdoc />
    public partial class DropLegacyFriendUserIdNotNull : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "LegacyFriendUserId",
                schema: "mirage",
                table: "couple_friendships",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "LegacyFriendUserId",
                schema: "mirage",
                table: "couple_friendships",
                type: "uuid",
                nullable: false,
                defaultValue: Guid.Empty,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
