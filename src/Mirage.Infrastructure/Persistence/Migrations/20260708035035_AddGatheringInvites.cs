using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mirage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGatheringInvites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "gathering_invites",
                schema: "mirage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    TargetId = table.Column<Guid>(type: "uuid", nullable: false),
                    InviterUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    InviteeUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gathering_invites", x => x.Id);
                    table.ForeignKey(
                        name: "FK_gathering_invites_AspNetUsers_InviteeUserId",
                        column: x => x.InviteeUserId,
                        principalSchema: "mirage",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_gathering_invites_AspNetUsers_InviterUserId",
                        column: x => x.InviterUserId,
                        principalSchema: "mirage",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_gathering_invites_InviteeUserId_Status",
                schema: "mirage",
                table: "gathering_invites",
                columns: new[] { "InviteeUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_gathering_invites_InviterUserId",
                schema: "mirage",
                table: "gathering_invites",
                column: "InviterUserId");

            migrationBuilder.CreateIndex(
                name: "IX_gathering_invites_Kind_TargetId_InviteeUserId",
                schema: "mirage",
                table: "gathering_invites",
                columns: new[] { "Kind", "TargetId", "InviteeUserId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "gathering_invites",
                schema: "mirage");
        }
    }
}
