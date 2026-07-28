using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mirage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMultiplePostImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImageUrl2",
                schema: "mirage",
                table: "testimonials",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImageUrl3",
                schema: "mirage",
                table: "testimonials",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImageUrl2",
                schema: "mirage",
                table: "community_posts",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImageUrl3",
                schema: "mirage",
                table: "community_posts",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImageUrl2",
                schema: "mirage",
                table: "testimonials");

            migrationBuilder.DropColumn(
                name: "ImageUrl3",
                schema: "mirage",
                table: "testimonials");

            migrationBuilder.DropColumn(
                name: "ImageUrl2",
                schema: "mirage",
                table: "community_posts");

            migrationBuilder.DropColumn(
                name: "ImageUrl3",
                schema: "mirage",
                table: "community_posts");
        }
    }
}
