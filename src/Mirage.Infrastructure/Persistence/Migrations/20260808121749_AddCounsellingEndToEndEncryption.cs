using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mirage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCounsellingEndToEndEncryption : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Ciphertext",
                schema: "mirage",
                table: "counselling_messages",
                type: "character varying(12000)",
                maxLength: 12000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClientMessageId",
                schema: "mirage",
                table: "counselling_messages",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EncryptionNonce",
                schema: "mirage",
                table: "counselling_messages",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EncryptionVersion",
                schema: "mirage",
                table: "counselling_messages",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "counselling_key_envelopes",
                schema: "mirage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SenderUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ciphertext = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Nonce = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_counselling_key_envelopes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_counselling_key_envelopes_AspNetUsers_RecipientUserId",
                        column: x => x.RecipientUserId,
                        principalSchema: "mirage",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_counselling_key_envelopes_AspNetUsers_SenderUserId",
                        column: x => x.SenderUserId,
                        principalSchema: "mirage",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_counselling_key_envelopes_counselling_sessions_SessionId",
                        column: x => x.SessionId,
                        principalSchema: "mirage",
                        principalTable: "counselling_sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_counselling_messages_SessionId_ClientMessageId",
                schema: "mirage",
                table: "counselling_messages",
                columns: new[] { "SessionId", "ClientMessageId" },
                unique: true,
                filter: "\"ClientMessageId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_counselling_key_envelopes_RecipientUserId",
                schema: "mirage",
                table: "counselling_key_envelopes",
                column: "RecipientUserId");

            migrationBuilder.CreateIndex(
                name: "IX_counselling_key_envelopes_SenderUserId",
                schema: "mirage",
                table: "counselling_key_envelopes",
                column: "SenderUserId");

            migrationBuilder.CreateIndex(
                name: "IX_counselling_key_envelopes_SessionId_RecipientUserId",
                schema: "mirage",
                table: "counselling_key_envelopes",
                columns: new[] { "SessionId", "RecipientUserId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "counselling_key_envelopes",
                schema: "mirage");

            migrationBuilder.DropIndex(
                name: "IX_counselling_messages_SessionId_ClientMessageId",
                schema: "mirage",
                table: "counselling_messages");

            migrationBuilder.DropColumn(
                name: "Ciphertext",
                schema: "mirage",
                table: "counselling_messages");

            migrationBuilder.DropColumn(
                name: "ClientMessageId",
                schema: "mirage",
                table: "counselling_messages");

            migrationBuilder.DropColumn(
                name: "EncryptionNonce",
                schema: "mirage",
                table: "counselling_messages");

            migrationBuilder.DropColumn(
                name: "EncryptionVersion",
                schema: "mirage",
                table: "counselling_messages");
        }
    }
}
