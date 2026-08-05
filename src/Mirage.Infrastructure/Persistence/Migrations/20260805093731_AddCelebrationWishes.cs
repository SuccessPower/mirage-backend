using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mirage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCelebrationWishes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "celebration_wishes",
                schema: "mirage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CelebrationEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_celebration_wishes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_celebration_wishes_AspNetUsers_AuthorUserId",
                        column: x => x.AuthorUserId,
                        principalSchema: "mirage",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_celebration_wishes_celebration_entries_CelebrationEntryId",
                        column: x => x.CelebrationEntryId,
                        principalSchema: "mirage",
                        principalTable: "celebration_entries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_celebration_wishes_AuthorUserId",
                schema: "mirage",
                table: "celebration_wishes",
                column: "AuthorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_celebration_wishes_CelebrationEntryId_CreatedAt",
                schema: "mirage",
                table: "celebration_wishes",
                columns: new[] { "CelebrationEntryId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "celebration_wishes",
                schema: "mirage");
        }
    }
}
