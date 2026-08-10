using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mirage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNewsletterReviewWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReviewRound",
                schema: "mirage",
                table: "newsletters",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "newsletter_reviews",
                schema: "mirage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NewsletterId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Decision = table.Column<int>(type: "integer", nullable: false),
                    Comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Round = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_newsletter_reviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_newsletter_reviews_AspNetUsers_ReviewerUserId",
                        column: x => x.ReviewerUserId,
                        principalSchema: "mirage",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_newsletter_reviews_newsletters_NewsletterId",
                        column: x => x.NewsletterId,
                        principalSchema: "mirage",
                        principalTable: "newsletters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_newsletter_reviews_NewsletterId_Round",
                schema: "mirage",
                table: "newsletter_reviews",
                columns: new[] { "NewsletterId", "Round" });

            migrationBuilder.CreateIndex(
                name: "IX_newsletter_reviews_ReviewerUserId",
                schema: "mirage",
                table: "newsletter_reviews",
                column: "ReviewerUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "newsletter_reviews",
                schema: "mirage");

            migrationBuilder.DropColumn(
                name: "ReviewRound",
                schema: "mirage",
                table: "newsletters");
        }
    }
}
