using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mirage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProfessionalBroadcastsAndPrivateEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CounsellorProfileId",
                schema: "mirage",
                table: "org_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsPrivate",
                schema: "mirage",
                table: "org_events",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "professional_broadcasts",
                schema: "mirage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    MentorProfileId = table.Column<Guid>(type: "uuid", nullable: true),
                    CounsellorProfileId = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Audience = table.Column<int>(type: "integer", nullable: false),
                    Content = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    ImageUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Location = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    StartsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EndsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Capacity = table.Column<int>(type: "integer", nullable: true),
                    ScheduledFor = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PublishedEntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    RecipientCount = table.Column<int>(type: "integer", nullable: false),
                    FailureReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_professional_broadcasts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_professional_broadcasts_AspNetUsers_AuthorUserId",
                        column: x => x.AuthorUserId,
                        principalSchema: "mirage",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_professional_broadcasts_counsellors_CounsellorProfileId",
                        column: x => x.CounsellorProfileId,
                        principalSchema: "mirage",
                        principalTable: "counsellors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_professional_broadcasts_mentors_MentorProfileId",
                        column: x => x.MentorProfileId,
                        principalSchema: "mirage",
                        principalTable: "mentors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_org_events_CounsellorProfileId",
                schema: "mirage",
                table: "org_events",
                column: "CounsellorProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_org_events_IsPrivate_StartsAt",
                schema: "mirage",
                table: "org_events",
                columns: new[] { "IsPrivate", "StartsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_professional_broadcasts_AuthorUserId",
                schema: "mirage",
                table: "professional_broadcasts",
                column: "AuthorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_professional_broadcasts_CounsellorProfileId",
                schema: "mirage",
                table: "professional_broadcasts",
                column: "CounsellorProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_professional_broadcasts_MentorProfileId",
                schema: "mirage",
                table: "professional_broadcasts",
                column: "MentorProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_professional_broadcasts_Status_ScheduledFor",
                schema: "mirage",
                table: "professional_broadcasts",
                columns: new[] { "Status", "ScheduledFor" });

            migrationBuilder.AddForeignKey(
                name: "FK_org_events_counsellors_CounsellorProfileId",
                schema: "mirage",
                table: "org_events",
                column: "CounsellorProfileId",
                principalSchema: "mirage",
                principalTable: "counsellors",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_org_events_counsellors_CounsellorProfileId",
                schema: "mirage",
                table: "org_events");

            migrationBuilder.DropTable(
                name: "professional_broadcasts",
                schema: "mirage");

            migrationBuilder.DropIndex(
                name: "IX_org_events_CounsellorProfileId",
                schema: "mirage",
                table: "org_events");

            migrationBuilder.DropIndex(
                name: "IX_org_events_IsPrivate_StartsAt",
                schema: "mirage",
                table: "org_events");

            migrationBuilder.DropColumn(
                name: "CounsellorProfileId",
                schema: "mirage",
                table: "org_events");

            migrationBuilder.DropColumn(
                name: "IsPrivate",
                schema: "mirage",
                table: "org_events");
        }
    }
}
