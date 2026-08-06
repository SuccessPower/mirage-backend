using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mirage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMentionsToPostsAndTestimonials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid[]>(
                name: "MentionedUserIds",
                schema: "mirage",
                table: "testimonials",
                type: "uuid[]",
                nullable: false,
                defaultValue: new Guid[0]);

            migrationBuilder.AddColumn<Guid[]>(
                name: "MentionedUserIds",
                schema: "mirage",
                table: "testimonial_comments",
                type: "uuid[]",
                nullable: false,
                defaultValue: new Guid[0]);

            migrationBuilder.AddColumn<Guid[]>(
                name: "MentionedUserIds",
                schema: "mirage",
                table: "community_posts",
                type: "uuid[]",
                nullable: false,
                defaultValue: new Guid[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MentionedUserIds",
                schema: "mirage",
                table: "testimonials");

            migrationBuilder.DropColumn(
                name: "MentionedUserIds",
                schema: "mirage",
                table: "testimonial_comments");

            migrationBuilder.DropColumn(
                name: "MentionedUserIds",
                schema: "mirage",
                table: "community_posts");
        }
    }
}
