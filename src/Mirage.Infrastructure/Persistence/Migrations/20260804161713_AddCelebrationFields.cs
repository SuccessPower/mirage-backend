using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mirage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCelebrationFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CelebrationType",
                schema: "mirage",
                table: "testimonials",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CelebrationOptOut",
                schema: "mirage",
                table: "profiles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "WeddingAnniversaryDate",
                schema: "mirage",
                table: "profiles",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CelebrationType",
                schema: "mirage",
                table: "testimonials");

            migrationBuilder.DropColumn(
                name: "CelebrationOptOut",
                schema: "mirage",
                table: "profiles");

            migrationBuilder.DropColumn(
                name: "WeddingAnniversaryDate",
                schema: "mirage",
                table: "profiles");
        }
    }
}
