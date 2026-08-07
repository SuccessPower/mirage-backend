using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mirage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProfilePhotoRequirements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "discovery_profile_views",
                schema: "mirage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ViewerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_discovery_profile_views", x => x.Id);
                    table.ForeignKey(
                        name: "FK_discovery_profile_views_AspNetUsers_ProfileUserId",
                        column: x => x.ProfileUserId,
                        principalSchema: "mirage",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_discovery_profile_views_AspNetUsers_ViewerUserId",
                        column: x => x.ViewerUserId,
                        principalSchema: "mirage",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_discovery_profile_views_ProfileUserId",
                schema: "mirage",
                table: "discovery_profile_views",
                column: "ProfileUserId");

            migrationBuilder.CreateIndex(
                name: "IX_discovery_profile_views_ViewerUserId_CreatedAt",
                schema: "mirage",
                table: "discovery_profile_views",
                columns: new[] { "ViewerUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_discovery_profile_views_ViewerUserId_ProfileUserId",
                schema: "mirage",
                table: "discovery_profile_views",
                columns: new[] { "ViewerUserId", "ProfileUserId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "discovery_profile_views",
                schema: "mirage");
        }
    }
}
