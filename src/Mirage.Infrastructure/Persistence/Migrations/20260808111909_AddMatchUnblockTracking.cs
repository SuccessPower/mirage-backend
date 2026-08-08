using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mirage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMatchUnblockTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BlockedByUserId",
                schema: "mirage",
                table: "matches",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StatusBeforeBlock",
                schema: "mirage",
                table: "matches",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BlockedByUserId",
                schema: "mirage",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "StatusBeforeBlock",
                schema: "mirage",
                table: "matches");
        }
    }
}
