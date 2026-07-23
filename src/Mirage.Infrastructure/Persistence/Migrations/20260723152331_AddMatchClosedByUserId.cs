using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mirage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMatchClosedByUserId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ClosedByUserId",
                schema: "mirage",
                table: "matches",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_matches_ClosedByUserId",
                schema: "mirage",
                table: "matches",
                column: "ClosedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_matches_AspNetUsers_ClosedByUserId",
                schema: "mirage",
                table: "matches",
                column: "ClosedByUserId",
                principalSchema: "mirage",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_matches_AspNetUsers_ClosedByUserId",
                schema: "mirage",
                table: "matches");

            migrationBuilder.DropIndex(
                name: "IX_matches_ClosedByUserId",
                schema: "mirage",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "ClosedByUserId",
                schema: "mirage",
                table: "matches");
        }
    }
}
