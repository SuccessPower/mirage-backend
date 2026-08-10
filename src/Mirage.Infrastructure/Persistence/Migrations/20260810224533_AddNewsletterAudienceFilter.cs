using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mirage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNewsletterAudienceFilter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int[]>(
                name: "AudienceRelationshipStatuses",
                schema: "mirage",
                table: "newsletters",
                type: "integer[]",
                nullable: false,
                defaultValue: new int[0]);

            migrationBuilder.AddColumn<int>(
                name: "AudienceSex",
                schema: "mirage",
                table: "newsletters",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AudienceRelationshipStatuses",
                schema: "mirage",
                table: "newsletters");

            migrationBuilder.DropColumn(
                name: "AudienceSex",
                schema: "mirage",
                table: "newsletters");
        }
    }
}
