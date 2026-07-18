using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mirage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProfileVotesAndCoupleFriendships : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "couple_friendships",
                schema: "mirage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CoupleId = table.Column<Guid>(type: "uuid", nullable: false),
                    FriendUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_couple_friendships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_couple_friendships_AspNetUsers_FriendUserId",
                        column: x => x.FriendUserId,
                        principalSchema: "mirage",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_couple_friendships_couples_CoupleId",
                        column: x => x.CoupleId,
                        principalSchema: "mirage",
                        principalTable: "couples",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "profile_votes",
                schema: "mirage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VoterUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Value = table.Column<short>(type: "smallint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_profile_votes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_profile_votes_AspNetUsers_TargetUserId",
                        column: x => x.TargetUserId,
                        principalSchema: "mirage",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_profile_votes_AspNetUsers_VoterUserId",
                        column: x => x.VoterUserId,
                        principalSchema: "mirage",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "couple_friend_messages",
                schema: "mirage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FriendshipId = table.Column<Guid>(type: "uuid", nullable: false),
                    SenderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Content = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    AttachmentUrl = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_couple_friend_messages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_couple_friend_messages_AspNetUsers_SenderId",
                        column: x => x.SenderId,
                        principalSchema: "mirage",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_couple_friend_messages_couple_friendships_FriendshipId",
                        column: x => x.FriendshipId,
                        principalSchema: "mirage",
                        principalTable: "couple_friendships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_couple_friend_messages_FriendshipId",
                schema: "mirage",
                table: "couple_friend_messages",
                column: "FriendshipId");

            migrationBuilder.CreateIndex(
                name: "IX_couple_friend_messages_SenderId",
                schema: "mirage",
                table: "couple_friend_messages",
                column: "SenderId");

            migrationBuilder.CreateIndex(
                name: "IX_couple_friendships_CoupleId_FriendUserId",
                schema: "mirage",
                table: "couple_friendships",
                columns: new[] { "CoupleId", "FriendUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_couple_friendships_FriendUserId",
                schema: "mirage",
                table: "couple_friendships",
                column: "FriendUserId");

            migrationBuilder.CreateIndex(
                name: "IX_profile_votes_TargetUserId",
                schema: "mirage",
                table: "profile_votes",
                column: "TargetUserId");

            migrationBuilder.CreateIndex(
                name: "IX_profile_votes_VoterUserId_TargetUserId",
                schema: "mirage",
                table: "profile_votes",
                columns: new[] { "VoterUserId", "TargetUserId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "couple_friend_messages",
                schema: "mirage");

            migrationBuilder.DropTable(
                name: "profile_votes",
                schema: "mirage");

            migrationBuilder.DropTable(
                name: "couple_friendships",
                schema: "mirage");
        }
    }
}
