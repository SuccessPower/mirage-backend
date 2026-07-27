using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mirage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProfileVisits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "profile_visits",
                schema: "mirage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    VisitorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevealOrdinal = table.Column<int>(type: "integer", nullable: false),
                    LastVisitedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_profile_visits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_profile_visits_AspNetUsers_ProfileUserId",
                        column: x => x.ProfileUserId,
                        principalSchema: "mirage",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_profile_visits_AspNetUsers_VisitorUserId",
                        column: x => x.VisitorUserId,
                        principalSchema: "mirage",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_profile_visits_ProfileUserId_LastVisitedAt",
                schema: "mirage",
                table: "profile_visits",
                columns: new[] { "ProfileUserId", "LastVisitedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_profile_visits_ProfileUserId_RevealOrdinal",
                schema: "mirage",
                table: "profile_visits",
                columns: new[] { "ProfileUserId", "RevealOrdinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_profile_visits_ProfileUserId_VisitorUserId",
                schema: "mirage",
                table: "profile_visits",
                columns: new[] { "ProfileUserId", "VisitorUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_profile_visits_VisitorUserId",
                schema: "mirage",
                table: "profile_visits",
                column: "VisitorUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "profile_visits",
                schema: "mirage");
        }
    }
}
