using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mirage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCounsellorInvitesAndMentorProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "counsellor_invites",
                schema: "mirage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RedeemedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_counsellor_invites", x => x.Id);
                    table.ForeignKey(
                        name: "FK_counsellor_invites_organisations_OrganisationId",
                        column: x => x.OrganisationId,
                        principalSchema: "mirage",
                        principalTable: "organisations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "mentors",
                schema: "mirage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    YearsMarried = table.Column<int>(type: "integer", nullable: false),
                    Testimony = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    AreasOfGuidance = table.Column<string[]>(type: "text[]", nullable: false),
                    Languages = table.Column<string[]>(type: "text[]", nullable: false),
                    IsAnonymous = table.Column<bool>(type: "boolean", nullable: false),
                    IsApproved = table.Column<bool>(type: "boolean", nullable: false),
                    AcceptsFreeSessions = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mentors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_mentors_profiles_UserId",
                        column: x => x.UserId,
                        principalSchema: "mirage",
                        principalTable: "profiles",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_counsellor_invites_OrganisationId_Email",
                schema: "mirage",
                table: "counsellor_invites",
                columns: new[] { "OrganisationId", "Email" });

            migrationBuilder.CreateIndex(
                name: "IX_counsellor_invites_TokenHash",
                schema: "mirage",
                table: "counsellor_invites",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_mentors_UserId",
                schema: "mirage",
                table: "mentors",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "counsellor_invites",
                schema: "mirage");

            migrationBuilder.DropTable(
                name: "mentors",
                schema: "mirage");
        }
    }
}
