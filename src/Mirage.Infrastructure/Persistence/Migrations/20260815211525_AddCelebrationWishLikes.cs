using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mirage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCelebrationWishLikes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "celebration_wish_likes",
                schema: "mirage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CelebrationWishId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_celebration_wish_likes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_celebration_wish_likes_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "mirage",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_celebration_wish_likes_celebration_wishes_CelebrationWishId",
                        column: x => x.CelebrationWishId,
                        principalSchema: "mirage",
                        principalTable: "celebration_wishes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_celebration_wish_likes_CelebrationWishId_UserId",
                schema: "mirage",
                table: "celebration_wish_likes",
                columns: new[] { "CelebrationWishId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_celebration_wish_likes_UserId",
                schema: "mirage",
                table: "celebration_wish_likes",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "celebration_wish_likes",
                schema: "mirage");
        }
    }
}
