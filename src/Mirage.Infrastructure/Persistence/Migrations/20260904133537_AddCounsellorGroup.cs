using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mirage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCounsellorGroup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "counsellor_group_meetings",
                schema: "mirage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CounsellorProfileId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_counsellor_group_meetings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_counsellor_group_meetings_AspNetUsers_ScheduledByUserId",
                        column: x => x.ScheduledByUserId,
                        principalSchema: "mirage",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_counsellor_group_meetings_counsellors_CounsellorProfileId",
                        column: x => x.CounsellorProfileId,
                        principalSchema: "mirage",
                        principalTable: "counsellors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "counsellor_group_messages",
                schema: "mirage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CounsellorProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    SenderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Content = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    AttachmentUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_counsellor_group_messages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_counsellor_group_messages_AspNetUsers_SenderId",
                        column: x => x.SenderId,
                        principalSchema: "mirage",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_counsellor_group_messages_counsellors_CounsellorProfileId",
                        column: x => x.CounsellorProfileId,
                        principalSchema: "mirage",
                        principalTable: "counsellors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "counsellor_posts",
                schema: "mirage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CounsellorProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Content = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    ImageUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_counsellor_posts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_counsellor_posts_counsellors_CounsellorProfileId",
                        column: x => x.CounsellorProfileId,
                        principalSchema: "mirage",
                        principalTable: "counsellors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_counsellor_group_meetings_CounsellorProfileId",
                schema: "mirage",
                table: "counsellor_group_meetings",
                column: "CounsellorProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_counsellor_group_meetings_ScheduledByUserId",
                schema: "mirage",
                table: "counsellor_group_meetings",
                column: "ScheduledByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_counsellor_group_messages_CounsellorProfileId",
                schema: "mirage",
                table: "counsellor_group_messages",
                column: "CounsellorProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_counsellor_group_messages_SenderId",
                schema: "mirage",
                table: "counsellor_group_messages",
                column: "SenderId");

            migrationBuilder.CreateIndex(
                name: "IX_counsellor_posts_CounsellorProfileId",
                schema: "mirage",
                table: "counsellor_posts",
                column: "CounsellorProfileId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "counsellor_group_meetings",
                schema: "mirage");

            migrationBuilder.DropTable(
                name: "counsellor_group_messages",
                schema: "mirage");

            migrationBuilder.DropTable(
                name: "counsellor_posts",
                schema: "mirage");
        }
    }
}
