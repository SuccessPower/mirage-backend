using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mirage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProfessionalInvitesAndCalendarReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InviteCode",
                schema: "mirage",
                table: "mentors",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InviteCode",
                schema: "mirage",
                table: "counsellors",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "calendar_reminder_deliveries",
                schema: "mirage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    LeadTime = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_calendar_reminder_deliveries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "professional_connections",
                schema: "mirage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfessionalUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    MemberUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_professional_connections", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_mentors_InviteCode",
                schema: "mirage",
                table: "mentors",
                column: "InviteCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_counsellors_InviteCode",
                schema: "mirage",
                table: "counsellors",
                column: "InviteCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_calendar_reminder_deliveries_Source_SourceId_UserId_LeadTime",
                schema: "mirage",
                table: "calendar_reminder_deliveries",
                columns: new[] { "Source", "SourceId", "UserId", "LeadTime" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_professional_connections_ProfessionalUserId_MemberUserId_Ro~",
                schema: "mirage",
                table: "professional_connections",
                columns: new[] { "ProfessionalUserId", "MemberUserId", "Role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_professional_connections_ProfessionalUserId_Status",
                schema: "mirage",
                table: "professional_connections",
                columns: new[] { "ProfessionalUserId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "calendar_reminder_deliveries",
                schema: "mirage");

            migrationBuilder.DropTable(
                name: "professional_connections",
                schema: "mirage");

            migrationBuilder.DropIndex(
                name: "IX_mentors_InviteCode",
                schema: "mirage",
                table: "mentors");

            migrationBuilder.DropIndex(
                name: "IX_counsellors_InviteCode",
                schema: "mirage",
                table: "counsellors");

            migrationBuilder.DropColumn(
                name: "InviteCode",
                schema: "mirage",
                table: "mentors");

            migrationBuilder.DropColumn(
                name: "InviteCode",
                schema: "mirage",
                table: "counsellors");
        }
    }
}
