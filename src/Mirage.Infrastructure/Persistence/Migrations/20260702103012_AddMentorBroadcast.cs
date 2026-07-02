using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mirage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMentorBroadcast : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mentor_group_messages",
                schema: "mirage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MentorProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    SenderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Content = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    AttachmentUrl = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mentor_group_messages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_mentor_group_messages_AspNetUsers_SenderId",
                        column: x => x.SenderId,
                        principalSchema: "mirage",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_mentor_group_messages_mentors_MentorProfileId",
                        column: x => x.MentorProfileId,
                        principalSchema: "mirage",
                        principalTable: "mentors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "mentor_meetings",
                schema: "mirage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MentorProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScheduledByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    MeetingLink = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ScheduledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DurationMinutes = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mentor_meetings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_mentor_meetings_AspNetUsers_ScheduledByUserId",
                        column: x => x.ScheduledByUserId,
                        principalSchema: "mirage",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_mentor_meetings_mentors_MentorProfileId",
                        column: x => x.MentorProfileId,
                        principalSchema: "mirage",
                        principalTable: "mentors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "mentor_posts",
                schema: "mirage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MentorProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Content = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    ImageUrl = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mentor_posts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_mentor_posts_mentors_MentorProfileId",
                        column: x => x.MentorProfileId,
                        principalSchema: "mirage",
                        principalTable: "mentors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_mentor_group_messages_MentorProfileId",
                schema: "mirage",
                table: "mentor_group_messages",
                column: "MentorProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_mentor_group_messages_SenderId",
                schema: "mirage",
                table: "mentor_group_messages",
                column: "SenderId");

            migrationBuilder.CreateIndex(
                name: "IX_mentor_meetings_MentorProfileId",
                schema: "mirage",
                table: "mentor_meetings",
                column: "MentorProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_mentor_meetings_ScheduledByUserId",
                schema: "mirage",
                table: "mentor_meetings",
                column: "ScheduledByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_mentor_posts_MentorProfileId",
                schema: "mirage",
                table: "mentor_posts",
                column: "MentorProfileId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mentor_group_messages",
                schema: "mirage");

            migrationBuilder.DropTable(
                name: "mentor_meetings",
                schema: "mirage");

            migrationBuilder.DropTable(
                name: "mentor_posts",
                schema: "mirage");
        }
    }
}
