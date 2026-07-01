using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mirage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddChatAttachmentsAndGatheringFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AttachmentUrl",
                schema: "mirage",
                table: "messages",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Type",
                schema: "mirage",
                table: "messages",
                type: "integer",
                nullable: false,
                defaultValue: 1); // MessageType.Text — existing rows are all plain text messages

            migrationBuilder.AddColumn<int>(
                name: "Capacity",
                schema: "mirage",
                table: "date_requests",
                type: "integer",
                nullable: false,
                defaultValue: 1); // existing date requests were all 1:1

            migrationBuilder.AddColumn<int>(
                name: "Intent",
                schema: "mirage",
                table: "date_requests",
                type: "integer",
                nullable: false,
                defaultValue: 2); // RelationshipIntent.Dating — existing date requests predate group gatherings

            migrationBuilder.AddColumn<string>(
                name: "ItemsToBring",
                schema: "mirage",
                table: "date_requests",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_date_requests_Intent_Status_StartsAt",
                schema: "mirage",
                table: "date_requests",
                columns: new[] { "Intent", "Status", "StartsAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_date_requests_Intent_Status_StartsAt",
                schema: "mirage",
                table: "date_requests");

            migrationBuilder.DropColumn(
                name: "AttachmentUrl",
                schema: "mirage",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "Type",
                schema: "mirage",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "Capacity",
                schema: "mirage",
                table: "date_requests");

            migrationBuilder.DropColumn(
                name: "Intent",
                schema: "mirage",
                table: "date_requests");

            migrationBuilder.DropColumn(
                name: "ItemsToBring",
                schema: "mirage",
                table: "date_requests");
        }
    }
}
