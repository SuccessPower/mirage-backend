using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mirage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanionFeature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "companion_partners",
                schema: "mirage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    User1Id = table.Column<Guid>(type: "uuid", nullable: false),
                    User2Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_companion_partners", x => x.Id);
                    table.ForeignKey(
                        name: "FK_companion_partners_AspNetUsers_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalSchema: "mirage",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_companion_partners_AspNetUsers_User1Id",
                        column: x => x.User1Id,
                        principalSchema: "mirage",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_companion_partners_AspNetUsers_User2Id",
                        column: x => x.User2Id,
                        principalSchema: "mirage",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "companion_prompts",
                schema: "mirage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Cadence = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_companion_prompts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "companion_entries",
                schema: "mirage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PromptId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AnswerText = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_companion_entries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_companion_entries_AspNetUsers_AuthorUserId",
                        column: x => x.AuthorUserId,
                        principalSchema: "mirage",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_companion_entries_companion_prompts_PromptId",
                        column: x => x.PromptId,
                        principalSchema: "mirage",
                        principalTable: "companion_prompts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "companion_subscriptions",
                schema: "mirage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Cadence = table.Column<int>(type: "integer", nullable: false),
                    NextDueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CurrentPromptId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_companion_subscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_companion_subscriptions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "mirage",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_companion_subscriptions_companion_prompts_CurrentPromptId",
                        column: x => x.CurrentPromptId,
                        principalSchema: "mirage",
                        principalTable: "companion_prompts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_companion_entries_AuthorUserId_CreatedAt",
                schema: "mirage",
                table: "companion_entries",
                columns: new[] { "AuthorUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_companion_entries_PromptId",
                schema: "mirage",
                table: "companion_entries",
                column: "PromptId");

            migrationBuilder.CreateIndex(
                name: "IX_companion_partners_RequestedByUserId",
                schema: "mirage",
                table: "companion_partners",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_companion_partners_User1Id_User2Id",
                schema: "mirage",
                table: "companion_partners",
                columns: new[] { "User1Id", "User2Id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_companion_partners_User2Id",
                schema: "mirage",
                table: "companion_partners",
                column: "User2Id");

            migrationBuilder.CreateIndex(
                name: "IX_companion_prompts_Cadence_IsActive",
                schema: "mirage",
                table: "companion_prompts",
                columns: new[] { "Cadence", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_companion_subscriptions_CurrentPromptId",
                schema: "mirage",
                table: "companion_subscriptions",
                column: "CurrentPromptId");

            migrationBuilder.CreateIndex(
                name: "IX_companion_subscriptions_NextDueAt",
                schema: "mirage",
                table: "companion_subscriptions",
                column: "NextDueAt");

            migrationBuilder.CreateIndex(
                name: "IX_companion_subscriptions_UserId",
                schema: "mirage",
                table: "companion_subscriptions",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "companion_entries",
                schema: "mirage");

            migrationBuilder.DropTable(
                name: "companion_partners",
                schema: "mirage");

            migrationBuilder.DropTable(
                name: "companion_subscriptions",
                schema: "mirage");

            migrationBuilder.DropTable(
                name: "companion_prompts",
                schema: "mirage");
        }
    }
}
