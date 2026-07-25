using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mirage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    // CoupleFriendship used to be keyed by (CoupleId, FriendUserId) — an individual spouse's
    // link to the other couple — so both spouses befriending the same couple created two
    // separate rows/conversations instead of one shared thread for all four people. This
    // migration re-keys the table by the normalized couple pair (Couple1Id < Couple2Id) and
    // merges any pre-existing duplicate threads (and their messages) into a single row.
    public partial class MergeCoupleFriendshipsByCouplePair : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_couple_friendships_couples_CoupleId",
                schema: "mirage",
                table: "couple_friendships");

            migrationBuilder.DropIndex(
                name: "IX_couple_friendships_CoupleId_FriendUserId",
                schema: "mirage",
                table: "couple_friendships");

            migrationBuilder.RenameColumn(
                name: "CoupleId",
                schema: "mirage",
                table: "couple_friendships",
                newName: "Couple1Id");

            // FriendUserId held an individual user, not a couple — kept (renamed, not dropped)
            // as an untracked legacy column rather than destroyed, since it's the only record of
            // which spouse originally initiated each pre-migration friendship.
            migrationBuilder.RenameColumn(
                name: "FriendUserId",
                schema: "mirage",
                table: "couple_friendships",
                newName: "LegacyFriendUserId");

            migrationBuilder.AddColumn<Guid>(
                name: "Couple2Id",
                schema: "mirage",
                table: "couple_friendships",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE mirage.couple_friendships cf
                SET "Couple2Id" = c."Id"
                FROM mirage.couples c
                WHERE (c."User1Id" = cf."LegacyFriendUserId" OR c."User2Id" = cf."LegacyFriendUserId")
                  AND c."Status" = 2;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "Couple2Id",
                schema: "mirage",
                table: "couple_friendships",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            // Normalize the pair so lookups by either couple resolve to the same row regardless
            // of which couple initiated the friendship.
            migrationBuilder.Sql("""
                UPDATE mirage.couple_friendships
                SET "Couple1Id" = "Couple2Id", "Couple2Id" = "Couple1Id"
                WHERE "Couple1Id" > "Couple2Id";
                """);

            // Merge duplicate threads that resulted from both spouses independently befriending
            // the same couple: pick one canonical row per couple pair (preferring Active status,
            // then the earliest created), move every message onto it, then delete the rest.
            migrationBuilder.Sql("""
                WITH ranked AS (
                    SELECT "Id", "Couple1Id", "Couple2Id",
                           ROW_NUMBER() OVER (
                               PARTITION BY "Couple1Id", "Couple2Id"
                               ORDER BY CASE WHEN "Status" = 1 THEN 0 ELSE 1 END, "CreatedAt", "Id"
                           ) AS rn
                    FROM mirage.couple_friendships
                ),
                mapping AS (
                    SELECT dup."Id" AS duplicate_id, canon."Id" AS canonical_id
                    FROM ranked dup
                    JOIN ranked canon
                      ON canon."Couple1Id" = dup."Couple1Id"
                     AND canon."Couple2Id" = dup."Couple2Id"
                     AND canon.rn = 1
                    WHERE dup.rn > 1
                )
                UPDATE mirage.couple_friend_messages m
                SET "FriendshipId" = mapping.canonical_id
                FROM mapping
                WHERE m."FriendshipId" = mapping.duplicate_id;
                """);

            migrationBuilder.Sql("""
                WITH ranked AS (
                    SELECT "Id",
                           ROW_NUMBER() OVER (
                               PARTITION BY "Couple1Id", "Couple2Id"
                               ORDER BY CASE WHEN "Status" = 1 THEN 0 ELSE 1 END, "CreatedAt", "Id"
                           ) AS rn
                    FROM mirage.couple_friendships
                )
                DELETE FROM mirage.couple_friendships
                WHERE "Id" IN (SELECT "Id" FROM ranked WHERE rn > 1);
                """);

            migrationBuilder.CreateIndex(
                name: "IX_couple_friendships_Couple1Id_Couple2Id",
                schema: "mirage",
                table: "couple_friendships",
                columns: new[] { "Couple1Id", "Couple2Id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_couple_friendships_Couple2Id",
                schema: "mirage",
                table: "couple_friendships",
                column: "Couple2Id");

            migrationBuilder.AddForeignKey(
                name: "FK_couple_friendships_couples_Couple1Id",
                schema: "mirage",
                table: "couple_friendships",
                column: "Couple1Id",
                principalSchema: "mirage",
                principalTable: "couples",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_couple_friendships_couples_Couple2Id",
                schema: "mirage",
                table: "couple_friendships",
                column: "Couple2Id",
                principalSchema: "mirage",
                principalTable: "couples",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Note: this restores the original schema shape, but cannot restore threads/messages
            // that Up() merged together — that data movement is not reversible.
            migrationBuilder.DropForeignKey(
                name: "FK_couple_friendships_couples_Couple1Id",
                schema: "mirage",
                table: "couple_friendships");

            migrationBuilder.DropForeignKey(
                name: "FK_couple_friendships_couples_Couple2Id",
                schema: "mirage",
                table: "couple_friendships");

            migrationBuilder.DropIndex(
                name: "IX_couple_friendships_Couple1Id_Couple2Id",
                schema: "mirage",
                table: "couple_friendships");

            migrationBuilder.DropIndex(
                name: "IX_couple_friendships_Couple2Id",
                schema: "mirage",
                table: "couple_friendships");

            migrationBuilder.DropColumn(
                name: "Couple2Id",
                schema: "mirage",
                table: "couple_friendships");

            migrationBuilder.RenameColumn(
                name: "Couple1Id",
                schema: "mirage",
                table: "couple_friendships",
                newName: "CoupleId");

            migrationBuilder.RenameColumn(
                name: "LegacyFriendUserId",
                schema: "mirage",
                table: "couple_friendships",
                newName: "FriendUserId");

            migrationBuilder.CreateIndex(
                name: "IX_couple_friendships_CoupleId_FriendUserId",
                schema: "mirage",
                table: "couple_friendships",
                columns: new[] { "CoupleId", "FriendUserId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_couple_friendships_couples_CoupleId",
                schema: "mirage",
                table: "couple_friendships",
                column: "CoupleId",
                principalSchema: "mirage",
                principalTable: "couples",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
