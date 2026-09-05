using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mirage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSharedChatThemesAndReactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "chat_conversation_themes",
                schema: "mirage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ThemeKey = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    SetByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chat_conversation_themes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_chat_conversation_themes_AspNetUsers_SetByUserId",
                        column: x => x.SetByUserId,
                        principalSchema: "mirage",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "chat_message_reactions",
                schema: "mirage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    Emoji = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chat_message_reactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_chat_message_reactions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "mirage",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_chat_conversation_themes_ConversationKey",
                schema: "mirage",
                table: "chat_conversation_themes",
                column: "ConversationKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_chat_conversation_themes_SetByUserId",
                schema: "mirage",
                table: "chat_conversation_themes",
                column: "SetByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_chat_message_reactions_ConversationKey",
                schema: "mirage",
                table: "chat_message_reactions",
                column: "ConversationKey");

            migrationBuilder.CreateIndex(
                name: "IX_chat_message_reactions_UserId_MessageId",
                schema: "mirage",
                table: "chat_message_reactions",
                columns: new[] { "UserId", "MessageId" },
                unique: true);

            // Wallpapers chosen before a conversation's theme was shared were held per member.
            // Carry each conversation's most recent choice across so nobody's chat loses the look
            // it had, then drop the rows: the only per-member preference left is the account
            // default, which keeps the "*" key.
            migrationBuilder.Sql("""
                INSERT INTO mirage.chat_conversation_themes
                    ("Id", "ConversationKey", "ThemeKey", "SetByUserId", "CreatedAt", "UpdatedAt")
                SELECT DISTINCT ON (p."ConversationKey")
                    gen_random_uuid(), p."ConversationKey", p."ThemeKey", p."UserId", p."CreatedAt", p."UpdatedAt"
                FROM mirage.chat_theme_preferences p
                WHERE p."ConversationKey" <> '*'
                ORDER BY p."ConversationKey", p."UpdatedAt" DESC;

                DELETE FROM mirage.chat_theme_preferences WHERE "ConversationKey" <> '*';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chat_conversation_themes",
                schema: "mirage");

            migrationBuilder.DropTable(
                name: "chat_message_reactions",
                schema: "mirage");
        }
    }
}
