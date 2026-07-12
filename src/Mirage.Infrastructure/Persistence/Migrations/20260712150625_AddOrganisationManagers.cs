using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mirage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganisationManagers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BranchId",
                schema: "mirage",
                table: "gathering_invites",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "organisation_managers",
                schema: "mirage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_organisation_managers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_organisation_managers_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "mirage",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_organisation_managers_organisation_branches_BranchId",
                        column: x => x.BranchId,
                        principalSchema: "mirage",
                        principalTable: "organisation_branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_organisation_managers_organisations_OrganisationId",
                        column: x => x.OrganisationId,
                        principalSchema: "mirage",
                        principalTable: "organisations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_gathering_invites_BranchId",
                schema: "mirage",
                table: "gathering_invites",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_organisation_managers_BranchId",
                schema: "mirage",
                table: "organisation_managers",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_organisation_managers_OrganisationId_UserId",
                schema: "mirage",
                table: "organisation_managers",
                columns: new[] { "OrganisationId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_organisation_managers_UserId",
                schema: "mirage",
                table: "organisation_managers",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_gathering_invites_organisation_branches_BranchId",
                schema: "mirage",
                table: "gathering_invites",
                column: "BranchId",
                principalSchema: "mirage",
                principalTable: "organisation_branches",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_gathering_invites_organisation_branches_BranchId",
                schema: "mirage",
                table: "gathering_invites");

            migrationBuilder.DropTable(
                name: "organisation_managers",
                schema: "mirage");

            migrationBuilder.DropIndex(
                name: "IX_gathering_invites_BranchId",
                schema: "mirage",
                table: "gathering_invites");

            migrationBuilder.DropColumn(
                name: "BranchId",
                schema: "mirage",
                table: "gathering_invites");
        }
    }
}
