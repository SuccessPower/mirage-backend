using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mirage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPrivateMentorMeetings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "MentorRequestId",
                schema: "mirage",
                table: "mentor_meetings",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_mentor_meetings_MentorRequestId",
                schema: "mirage",
                table: "mentor_meetings",
                column: "MentorRequestId");

            migrationBuilder.AddForeignKey(
                name: "FK_mentor_meetings_mentor_requests_MentorRequestId",
                schema: "mirage",
                table: "mentor_meetings",
                column: "MentorRequestId",
                principalSchema: "mirage",
                principalTable: "mentor_requests",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_mentor_meetings_mentor_requests_MentorRequestId",
                schema: "mirage",
                table: "mentor_meetings");

            migrationBuilder.DropIndex(
                name: "IX_mentor_meetings_MentorRequestId",
                schema: "mirage",
                table: "mentor_meetings");

            migrationBuilder.DropColumn(
                name: "MentorRequestId",
                schema: "mirage",
                table: "mentor_meetings");
        }
    }
}
