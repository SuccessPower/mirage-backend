using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mirage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveRelationshipIntent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_profiles_Intent_City",
                schema: "mirage",
                table: "profiles");

            migrationBuilder.DropColumn(
                name: "Intent",
                schema: "mirage",
                table: "profiles");

            migrationBuilder.RenameColumn(
                name: "Intent",
                schema: "mirage",
                table: "date_requests",
                newName: "Category");

            migrationBuilder.RenameIndex(
                name: "IX_date_requests_Intent_Status_StartsAt",
                schema: "mirage",
                table: "date_requests",
                newName: "IX_date_requests_Category_Status_StartsAt");

            migrationBuilder.CreateIndex(
                name: "IX_profiles_RelationshipStatus_City",
                schema: "mirage",
                table: "profiles",
                columns: new[] { "RelationshipStatus", "City" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_profiles_RelationshipStatus_City",
                schema: "mirage",
                table: "profiles");

            migrationBuilder.RenameColumn(
                name: "Category",
                schema: "mirage",
                table: "date_requests",
                newName: "Intent");

            migrationBuilder.RenameIndex(
                name: "IX_date_requests_Category_Status_StartsAt",
                schema: "mirage",
                table: "date_requests",
                newName: "IX_date_requests_Intent_Status_StartsAt");

            migrationBuilder.AddColumn<int>(
                name: "Intent",
                schema: "mirage",
                table: "profiles",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_profiles_Intent_City",
                schema: "mirage",
                table: "profiles",
                columns: new[] { "Intent", "City" });
        }
    }
}
