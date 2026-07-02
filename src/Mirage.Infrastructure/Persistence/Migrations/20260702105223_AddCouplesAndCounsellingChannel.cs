using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mirage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCouplesAndCounsellingChannel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "OrganisationId",
                schema: "mirage",
                table: "counsellors",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<bool>(
                name: "IsRejected",
                schema: "mirage",
                table: "counsellors",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                schema: "mirage",
                table: "counsellors",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string[]>(
                name: "VerificationDocumentUrls",
                schema: "mirage",
                table: "counsellors",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.CreateTable(
                name: "counselling_meetings",
                schema: "mirage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_counselling_meetings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_counselling_meetings_AspNetUsers_ScheduledByUserId",
                        column: x => x.ScheduledByUserId,
                        principalSchema: "mirage",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_counselling_meetings_counselling_sessions_SessionId",
                        column: x => x.SessionId,
                        principalSchema: "mirage",
                        principalTable: "counselling_sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "counselling_messages",
                schema: "mirage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SenderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Content = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    AttachmentUrl = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_counselling_messages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_counselling_messages_AspNetUsers_SenderId",
                        column: x => x.SenderId,
                        principalSchema: "mirage",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_counselling_messages_counselling_sessions_SessionId",
                        column: x => x.SessionId,
                        principalSchema: "mirage",
                        principalTable: "counselling_sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "couples",
                schema: "mirage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    User1Id = table.Column<Guid>(type: "uuid", nullable: false),
                    User2Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_couples", x => x.Id);
                    table.ForeignKey(
                        name: "FK_couples_AspNetUsers_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalSchema: "mirage",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_couples_AspNetUsers_User1Id",
                        column: x => x.User1Id,
                        principalSchema: "mirage",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_couples_AspNetUsers_User2Id",
                        column: x => x.User2Id,
                        principalSchema: "mirage",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_counselling_meetings_ScheduledByUserId",
                schema: "mirage",
                table: "counselling_meetings",
                column: "ScheduledByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_counselling_meetings_SessionId",
                schema: "mirage",
                table: "counselling_meetings",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_counselling_messages_SenderId",
                schema: "mirage",
                table: "counselling_messages",
                column: "SenderId");

            migrationBuilder.CreateIndex(
                name: "IX_counselling_messages_SessionId",
                schema: "mirage",
                table: "counselling_messages",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_couples_RequestedByUserId",
                schema: "mirage",
                table: "couples",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_couples_User1Id_User2Id",
                schema: "mirage",
                table: "couples",
                columns: new[] { "User1Id", "User2Id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_couples_User2Id",
                schema: "mirage",
                table: "couples",
                column: "User2Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "counselling_meetings",
                schema: "mirage");

            migrationBuilder.DropTable(
                name: "counselling_messages",
                schema: "mirage");

            migrationBuilder.DropTable(
                name: "couples",
                schema: "mirage");

            migrationBuilder.DropColumn(
                name: "IsRejected",
                schema: "mirage",
                table: "counsellors");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                schema: "mirage",
                table: "counsellors");

            migrationBuilder.DropColumn(
                name: "VerificationDocumentUrls",
                schema: "mirage",
                table: "counsellors");

            migrationBuilder.AlterColumn<Guid>(
                name: "OrganisationId",
                schema: "mirage",
                table: "counsellors",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
