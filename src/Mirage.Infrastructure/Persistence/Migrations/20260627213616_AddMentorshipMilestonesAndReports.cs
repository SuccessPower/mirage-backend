using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mirage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMentorshipMilestonesAndReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "content_reports",
                schema: "mirage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetType = table.Column<int>(type: "integer", nullable: false),
                    TargetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<int>(type: "integer", nullable: false),
                    Details = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Resolution = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_content_reports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_content_reports_AspNetUsers_ReportedByUserId",
                        column: x => x.ReportedByUserId,
                        principalSchema: "mirage",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "date_feedbacks",
                schema: "mirage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DateRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewedUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Rating = table.Column<int>(type: "integer", nullable: false),
                    Comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_date_feedbacks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_date_feedbacks_AspNetUsers_ReviewedUserId",
                        column: x => x.ReviewedUserId,
                        principalSchema: "mirage",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_date_feedbacks_AspNetUsers_ReviewerUserId",
                        column: x => x.ReviewerUserId,
                        principalSchema: "mirage",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_date_feedbacks_date_requests_DateRequestId",
                        column: x => x.DateRequestId,
                        principalSchema: "mirage",
                        principalTable: "date_requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "mentor_requests",
                schema: "mirage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MentorProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    MenteeUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mentor_requests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_mentor_requests_AspNetUsers_MenteeUserId",
                        column: x => x.MenteeUserId,
                        principalSchema: "mirage",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_mentor_requests_mentors_MentorProfileId",
                        column: x => x.MentorProfileId,
                        principalSchema: "mirage",
                        principalTable: "mentors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "milestone_logs",
                schema: "mirage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    PartnerId = table.Column<Guid>(type: "uuid", nullable: true),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_milestone_logs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_milestone_logs_AspNetUsers_PartnerId",
                        column: x => x.PartnerId,
                        principalSchema: "mirage",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_milestone_logs_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "mirage",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "session_notes",
                schema: "mirage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Content = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_session_notes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_session_notes_AspNetUsers_AuthorUserId",
                        column: x => x.AuthorUserId,
                        principalSchema: "mirage",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_session_notes_counselling_sessions_SessionId",
                        column: x => x.SessionId,
                        principalSchema: "mirage",
                        principalTable: "counselling_sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "session_ratings",
                schema: "mirage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Rating = table.Column<int>(type: "integer", nullable: false),
                    Comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_session_ratings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_session_ratings_AspNetUsers_ReviewerUserId",
                        column: x => x.ReviewerUserId,
                        principalSchema: "mirage",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_session_ratings_counselling_sessions_SessionId",
                        column: x => x.SessionId,
                        principalSchema: "mirage",
                        principalTable: "counselling_sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_content_reports_ReportedByUserId",
                schema: "mirage",
                table: "content_reports",
                column: "ReportedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_content_reports_Status_CreatedAt",
                schema: "mirage",
                table: "content_reports",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_content_reports_TargetType_TargetId",
                schema: "mirage",
                table: "content_reports",
                columns: new[] { "TargetType", "TargetId" });

            migrationBuilder.CreateIndex(
                name: "IX_date_feedbacks_DateRequestId_ReviewerUserId",
                schema: "mirage",
                table: "date_feedbacks",
                columns: new[] { "DateRequestId", "ReviewerUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_date_feedbacks_ReviewedUserId",
                schema: "mirage",
                table: "date_feedbacks",
                column: "ReviewedUserId");

            migrationBuilder.CreateIndex(
                name: "IX_date_feedbacks_ReviewerUserId",
                schema: "mirage",
                table: "date_feedbacks",
                column: "ReviewerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_mentor_requests_MenteeUserId",
                schema: "mirage",
                table: "mentor_requests",
                column: "MenteeUserId");

            migrationBuilder.CreateIndex(
                name: "IX_mentor_requests_MentorProfileId_MenteeUserId",
                schema: "mirage",
                table: "mentor_requests",
                columns: new[] { "MentorProfileId", "MenteeUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_mentor_requests_Status",
                schema: "mirage",
                table: "mentor_requests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_milestone_logs_PartnerId",
                schema: "mirage",
                table: "milestone_logs",
                column: "PartnerId");

            migrationBuilder.CreateIndex(
                name: "IX_milestone_logs_UserId_Type",
                schema: "mirage",
                table: "milestone_logs",
                columns: new[] { "UserId", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_session_notes_AuthorUserId",
                schema: "mirage",
                table: "session_notes",
                column: "AuthorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_session_notes_SessionId",
                schema: "mirage",
                table: "session_notes",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_session_ratings_ReviewerUserId",
                schema: "mirage",
                table: "session_ratings",
                column: "ReviewerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_session_ratings_SessionId_ReviewerUserId",
                schema: "mirage",
                table: "session_ratings",
                columns: new[] { "SessionId", "ReviewerUserId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "content_reports",
                schema: "mirage");

            migrationBuilder.DropTable(
                name: "date_feedbacks",
                schema: "mirage");

            migrationBuilder.DropTable(
                name: "mentor_requests",
                schema: "mirage");

            migrationBuilder.DropTable(
                name: "milestone_logs",
                schema: "mirage");

            migrationBuilder.DropTable(
                name: "session_notes",
                schema: "mirage");

            migrationBuilder.DropTable(
                name: "session_ratings",
                schema: "mirage");
        }
    }
}
