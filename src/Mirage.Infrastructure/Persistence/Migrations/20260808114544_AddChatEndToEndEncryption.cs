using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mirage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddChatEndToEndEncryption : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Ciphertext",
                schema: "mirage",
                table: "messages",
                type: "character varying(12000)",
                maxLength: 12000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClientMessageId",
                schema: "mirage",
                table: "messages",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EncryptionNonce",
                schema: "mirage",
                table: "messages",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EncryptionVersion",
                schema: "mirage",
                table: "messages",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "chat_device_links",
                schema: "mirage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RequesterPublicKeyJwk = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    EncryptedPayload = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    PayloadNonce = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ClaimedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chat_device_links", x => x.Id);
                    table.ForeignKey(
                        name: "FK_chat_device_links_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "mirage",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "chat_encryption_identities",
                schema: "mirage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PublicKeyJwk = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    EncryptedPrivateKey = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    PrivateKeyNonce = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RecoverySalt = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    KdfIterations = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chat_encryption_identities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_chat_encryption_identities_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "mirage",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_messages_MatchId_ClientMessageId",
                schema: "mirage",
                table: "messages",
                columns: new[] { "MatchId", "ClientMessageId" },
                unique: true,
                filter: "\"ClientMessageId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_chat_device_links_CodeHash",
                schema: "mirage",
                table: "chat_device_links",
                column: "CodeHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_chat_device_links_UserId_ExpiresAt",
                schema: "mirage",
                table: "chat_device_links",
                columns: new[] { "UserId", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_chat_encryption_identities_UserId",
                schema: "mirage",
                table: "chat_encryption_identities",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chat_device_links",
                schema: "mirage");

            migrationBuilder.DropTable(
                name: "chat_encryption_identities",
                schema: "mirage");

            migrationBuilder.DropIndex(
                name: "IX_messages_MatchId_ClientMessageId",
                schema: "mirage",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "Ciphertext",
                schema: "mirage",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "ClientMessageId",
                schema: "mirage",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "EncryptionNonce",
                schema: "mirage",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "EncryptionVersion",
                schema: "mirage",
                table: "messages");
        }
    }
}
