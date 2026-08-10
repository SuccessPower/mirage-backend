using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mirage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNewsletterPlatformManagers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsNewsletterSubscribed",
                schema: "mirage",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NewsletterSubscribedAt",
                schema: "mirage",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "newsletters",
                schema: "mirage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Subject = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    Excerpt = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ContentHtml = table.Column<string>(type: "text", nullable: false),
                    ImageUrls = table.Column<string[]>(type: "text[]", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ScheduledFor = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RecipientCount = table.Column<int>(type: "integer", nullable: false),
                    DeliveredCount = table.Column<int>(type: "integer", nullable: false),
                    FailedCount = table.Column<int>(type: "integer", nullable: false),
                    FailureReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_newsletters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_newsletters_AspNetUsers_AuthorUserId",
                        column: x => x.AuthorUserId,
                        principalSchema: "mirage",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "platform_manager_invites",
                schema: "mirage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    InvitedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AcceptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_platform_manager_invites", x => x.Id);
                    table.ForeignKey(
                        name: "FK_platform_manager_invites_AspNetUsers_InvitedByUserId",
                        column: x => x.InvitedByUserId,
                        principalSchema: "mirage",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "newsletter_comments",
                schema: "mirage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NewsletterId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentCommentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_newsletter_comments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_newsletter_comments_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "mirage",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_newsletter_comments_newsletter_comments_ParentCommentId",
                        column: x => x.ParentCommentId,
                        principalSchema: "mirage",
                        principalTable: "newsletter_comments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_newsletter_comments_newsletters_NewsletterId",
                        column: x => x.NewsletterId,
                        principalSchema: "mirage",
                        principalTable: "newsletters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "newsletter_deliveries",
                schema: "mirage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NewsletterId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_newsletter_deliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_newsletter_deliveries_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "mirage",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_newsletter_deliveries_newsletters_NewsletterId",
                        column: x => x.NewsletterId,
                        principalSchema: "mirage",
                        principalTable: "newsletters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "newsletter_likes",
                schema: "mirage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NewsletterId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_newsletter_likes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_newsletter_likes_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "mirage",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_newsletter_likes_newsletters_NewsletterId",
                        column: x => x.NewsletterId,
                        principalSchema: "mirage",
                        principalTable: "newsletters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "newsletter_comment_likes",
                schema: "mirage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CommentId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_newsletter_comment_likes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_newsletter_comment_likes_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "mirage",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_newsletter_comment_likes_newsletter_comments_CommentId",
                        column: x => x.CommentId,
                        principalSchema: "mirage",
                        principalTable: "newsletter_comments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_newsletter_comment_likes_CommentId_UserId",
                schema: "mirage",
                table: "newsletter_comment_likes",
                columns: new[] { "CommentId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_newsletter_comment_likes_UserId",
                schema: "mirage",
                table: "newsletter_comment_likes",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_newsletter_comments_NewsletterId_CreatedAt",
                schema: "mirage",
                table: "newsletter_comments",
                columns: new[] { "NewsletterId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_newsletter_comments_ParentCommentId",
                schema: "mirage",
                table: "newsletter_comments",
                column: "ParentCommentId");

            migrationBuilder.CreateIndex(
                name: "IX_newsletter_comments_UserId",
                schema: "mirage",
                table: "newsletter_comments",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_newsletter_deliveries_NewsletterId_Status",
                schema: "mirage",
                table: "newsletter_deliveries",
                columns: new[] { "NewsletterId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_newsletter_deliveries_NewsletterId_UserId",
                schema: "mirage",
                table: "newsletter_deliveries",
                columns: new[] { "NewsletterId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_newsletter_deliveries_UserId",
                schema: "mirage",
                table: "newsletter_deliveries",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_newsletter_likes_NewsletterId_UserId",
                schema: "mirage",
                table: "newsletter_likes",
                columns: new[] { "NewsletterId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_newsletter_likes_UserId",
                schema: "mirage",
                table: "newsletter_likes",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_newsletters_AuthorUserId",
                schema: "mirage",
                table: "newsletters",
                column: "AuthorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_newsletters_Status_ScheduledFor",
                schema: "mirage",
                table: "newsletters",
                columns: new[] { "Status", "ScheduledFor" });

            migrationBuilder.CreateIndex(
                name: "IX_platform_manager_invites_Email_AcceptedAt",
                schema: "mirage",
                table: "platform_manager_invites",
                columns: new[] { "Email", "AcceptedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_platform_manager_invites_InvitedByUserId",
                schema: "mirage",
                table: "platform_manager_invites",
                column: "InvitedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_platform_manager_invites_TokenHash",
                schema: "mirage",
                table: "platform_manager_invites",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "newsletter_comment_likes",
                schema: "mirage");

            migrationBuilder.DropTable(
                name: "newsletter_deliveries",
                schema: "mirage");

            migrationBuilder.DropTable(
                name: "newsletter_likes",
                schema: "mirage");

            migrationBuilder.DropTable(
                name: "platform_manager_invites",
                schema: "mirage");

            migrationBuilder.DropTable(
                name: "newsletter_comments",
                schema: "mirage");

            migrationBuilder.DropTable(
                name: "newsletters",
                schema: "mirage");

            migrationBuilder.DropColumn(
                name: "IsNewsletterSubscribed",
                schema: "mirage",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "NewsletterSubscribedAt",
                schema: "mirage",
                table: "AspNetUsers");
        }
    }
}
