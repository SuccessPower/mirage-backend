using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mirage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProfileDetailFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "HeightInches",
                schema: "mirage",
                table: "profiles",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreferredLanguage",
                schema: "mirage",
                table: "profiles",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RelationshipStatus",
                schema: "mirage",
                table: "profiles",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Sex",
                schema: "mirage",
                table: "profiles",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SkinTone",
                schema: "mirage",
                table: "profiles",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ChatRequestedByUserId",
                schema: "mirage",
                table: "matches",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_matches_ChatRequestedByUserId",
                schema: "mirage",
                table: "matches",
                column: "ChatRequestedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_matches_AspNetUsers_ChatRequestedByUserId",
                schema: "mirage",
                table: "matches",
                column: "ChatRequestedByUserId",
                principalSchema: "mirage",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_matches_AspNetUsers_ChatRequestedByUserId",
                schema: "mirage",
                table: "matches");

            migrationBuilder.DropIndex(
                name: "IX_matches_ChatRequestedByUserId",
                schema: "mirage",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "HeightInches",
                schema: "mirage",
                table: "profiles");

            migrationBuilder.DropColumn(
                name: "PreferredLanguage",
                schema: "mirage",
                table: "profiles");

            migrationBuilder.DropColumn(
                name: "RelationshipStatus",
                schema: "mirage",
                table: "profiles");

            migrationBuilder.DropColumn(
                name: "Sex",
                schema: "mirage",
                table: "profiles");

            migrationBuilder.DropColumn(
                name: "SkinTone",
                schema: "mirage",
                table: "profiles");

            migrationBuilder.DropColumn(
                name: "ChatRequestedByUserId",
                schema: "mirage",
                table: "matches");
        }
    }
}
