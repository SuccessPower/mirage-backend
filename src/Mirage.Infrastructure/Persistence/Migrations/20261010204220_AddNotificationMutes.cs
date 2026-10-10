using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mirage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationMutes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "notification_mutes",
                schema: "mirage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Scope = table.Column<int>(type: "integer", nullable: false),
                    ScopeId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_mutes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_notification_mutes_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "mirage",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_notification_mutes_Scope_ScopeId",
                schema: "mirage",
                table: "notification_mutes",
                columns: new[] { "Scope", "ScopeId" });

            migrationBuilder.CreateIndex(
                name: "IX_notification_mutes_UserId_Scope_ScopeId",
                schema: "mirage",
                table: "notification_mutes",
                columns: new[] { "UserId", "Scope", "ScopeId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notification_mutes",
                schema: "mirage");
        }
    }
}
