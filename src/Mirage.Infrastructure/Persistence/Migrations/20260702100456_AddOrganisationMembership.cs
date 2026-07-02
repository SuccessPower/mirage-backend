using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mirage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganisationMembership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "organisation_branches",
                schema: "mirage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    City = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Country = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Address = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_organisation_branches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_organisation_branches_organisations_OrganisationId",
                        column: x => x.OrganisationId,
                        principalSchema: "mirage",
                        principalTable: "organisations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "org_events",
                schema: "mirage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    StartsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Location = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Capacity = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_org_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_org_events_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalSchema: "mirage",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_org_events_organisation_branches_BranchId",
                        column: x => x.BranchId,
                        principalSchema: "mirage",
                        principalTable: "organisation_branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_org_events_organisations_OrganisationId",
                        column: x => x.OrganisationId,
                        principalSchema: "mirage",
                        principalTable: "organisations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "organisation_members",
                schema: "mirage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    AssignedMentorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssignedCounsellorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_organisation_members", x => x.Id);
                    table.ForeignKey(
                        name: "FK_organisation_members_AspNetUsers_AssignedCounsellorUserId",
                        column: x => x.AssignedCounsellorUserId,
                        principalSchema: "mirage",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_organisation_members_AspNetUsers_AssignedMentorUserId",
                        column: x => x.AssignedMentorUserId,
                        principalSchema: "mirage",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_organisation_members_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "mirage",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_organisation_members_organisation_branches_BranchId",
                        column: x => x.BranchId,
                        principalSchema: "mirage",
                        principalTable: "organisation_branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_organisation_members_organisations_OrganisationId",
                        column: x => x.OrganisationId,
                        principalSchema: "mirage",
                        principalTable: "organisations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "event_tickets",
                schema: "mirage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CheckedInAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_event_tickets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_event_tickets_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "mirage",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_event_tickets_org_events_EventId",
                        column: x => x.EventId,
                        principalSchema: "mirage",
                        principalTable: "org_events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_event_tickets_Code",
                schema: "mirage",
                table: "event_tickets",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_event_tickets_EventId_UserId",
                schema: "mirage",
                table: "event_tickets",
                columns: new[] { "EventId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_event_tickets_UserId",
                schema: "mirage",
                table: "event_tickets",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_org_events_BranchId",
                schema: "mirage",
                table: "org_events",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_org_events_CreatedByUserId",
                schema: "mirage",
                table: "org_events",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_org_events_OrganisationId",
                schema: "mirage",
                table: "org_events",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_organisation_branches_OrganisationId",
                schema: "mirage",
                table: "organisation_branches",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_organisation_members_AssignedCounsellorUserId",
                schema: "mirage",
                table: "organisation_members",
                column: "AssignedCounsellorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_organisation_members_AssignedMentorUserId",
                schema: "mirage",
                table: "organisation_members",
                column: "AssignedMentorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_organisation_members_BranchId",
                schema: "mirage",
                table: "organisation_members",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_organisation_members_OrganisationId_UserId",
                schema: "mirage",
                table: "organisation_members",
                columns: new[] { "OrganisationId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_organisation_members_UserId",
                schema: "mirage",
                table: "organisation_members",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "event_tickets",
                schema: "mirage");

            migrationBuilder.DropTable(
                name: "organisation_members",
                schema: "mirage");

            migrationBuilder.DropTable(
                name: "org_events",
                schema: "mirage");

            migrationBuilder.DropTable(
                name: "organisation_branches",
                schema: "mirage");
        }
    }
}
