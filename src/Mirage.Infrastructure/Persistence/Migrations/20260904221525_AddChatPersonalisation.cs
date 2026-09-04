using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mirage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddChatPersonalisation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "chat_clear_markers",
                schema: "mirage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ClearedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chat_clear_markers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_chat_clear_markers_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "mirage",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "chat_message_hides",
                schema: "mirage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chat_message_hides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_chat_message_hides_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "mirage",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "chat_theme_preferences",
                schema: "mirage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ThemeKey = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chat_theme_preferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_chat_theme_preferences_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "mirage",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_chat_clear_markers_UserId_ConversationKey",
                schema: "mirage",
                table: "chat_clear_markers",
                columns: new[] { "UserId", "ConversationKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_chat_message_hides_UserId_ConversationKey",
                schema: "mirage",
                table: "chat_message_hides",
                columns: new[] { "UserId", "ConversationKey" });

            migrationBuilder.CreateIndex(
                name: "IX_chat_message_hides_UserId_MessageId",
                schema: "mirage",
                table: "chat_message_hides",
                columns: new[] { "UserId", "MessageId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_chat_theme_preferences_UserId_ConversationKey",
                schema: "mirage",
                table: "chat_theme_preferences",
                columns: new[] { "UserId", "ConversationKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chat_clear_markers",
                schema: "mirage");

            migrationBuilder.DropTable(
                name: "chat_message_hides",
                schema: "mirage");

            migrationBuilder.DropTable(
                name: "chat_theme_preferences",
                schema: "mirage");
        }
    }
}
