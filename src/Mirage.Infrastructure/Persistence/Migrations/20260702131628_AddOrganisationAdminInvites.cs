using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mirage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganisationAdminInvites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "organisation_admin_invites",
                schema: "mirage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RedeemedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_organisation_admin_invites", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_organisation_admin_invites_Email",
                schema: "mirage",
                table: "organisation_admin_invites",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_organisation_admin_invites_TokenHash",
                schema: "mirage",
                table: "organisation_admin_invites",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "organisation_admin_invites",
                schema: "mirage");
        }
    }
}
