using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mirage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCommunityVotingAndHiding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "HiddenAt",
                schema: "mirage",
                table: "community_posts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsHidden",
                schema: "mirage",
                table: "community_posts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "HiddenAt",
                schema: "mirage",
                table: "community_post_comments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsHidden",
                schema: "mirage",
                table: "community_post_comments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "community_post_comment_votes",
                schema: "mirage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CommentId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Value = table.Column<short>(type: "smallint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_community_post_comment_votes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_community_post_comment_votes_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "mirage",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_community_post_comment_votes_community_post_comments_Commen~",
                        column: x => x.CommentId,
                        principalSchema: "mirage",
                        principalTable: "community_post_comments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "community_post_votes",
                schema: "mirage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PostId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Value = table.Column<short>(type: "smallint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_community_post_votes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_community_post_votes_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "mirage",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_community_post_votes_community_posts_PostId",
                        column: x => x.PostId,
                        principalSchema: "mirage",
                        principalTable: "community_posts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_community_posts_CommunityId_IsHidden",
                schema: "mirage",
                table: "community_posts",
                columns: new[] { "CommunityId", "IsHidden" });

            migrationBuilder.CreateIndex(
                name: "IX_community_post_comments_PostId_IsHidden",
                schema: "mirage",
                table: "community_post_comments",
                columns: new[] { "PostId", "IsHidden" });

            migrationBuilder.CreateIndex(
                name: "IX_community_post_comment_votes_CommentId_UserId",
                schema: "mirage",
                table: "community_post_comment_votes",
                columns: new[] { "CommentId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_community_post_comment_votes_UserId",
                schema: "mirage",
                table: "community_post_comment_votes",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_community_post_votes_PostId_UserId",
                schema: "mirage",
                table: "community_post_votes",
                columns: new[] { "PostId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_community_post_votes_UserId",
                schema: "mirage",
                table: "community_post_votes",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "community_post_comment_votes",
                schema: "mirage");

            migrationBuilder.DropTable(
                name: "community_post_votes",
                schema: "mirage");

            migrationBuilder.DropIndex(
                name: "IX_community_posts_CommunityId_IsHidden",
                schema: "mirage",
                table: "community_posts");

            migrationBuilder.DropIndex(
                name: "IX_community_post_comments_PostId_IsHidden",
                schema: "mirage",
                table: "community_post_comments");

            migrationBuilder.DropColumn(
                name: "HiddenAt",
                schema: "mirage",
                table: "community_posts");

            migrationBuilder.DropColumn(
                name: "IsHidden",
                schema: "mirage",
                table: "community_posts");

            migrationBuilder.DropColumn(
                name: "HiddenAt",
                schema: "mirage",
                table: "community_post_comments");

            migrationBuilder.DropColumn(
                name: "IsHidden",
                schema: "mirage",
                table: "community_post_comments");
        }
    }
}
