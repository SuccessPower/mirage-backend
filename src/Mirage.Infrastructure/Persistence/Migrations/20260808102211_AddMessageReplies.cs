using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mirage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMessageReplies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ReplyToMessageId",
                schema: "mirage",
                table: "messages",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_messages_ReplyToMessageId",
                schema: "mirage",
                table: "messages",
                column: "ReplyToMessageId");

            migrationBuilder.AddForeignKey(
                name: "FK_messages_messages_ReplyToMessageId",
                schema: "mirage",
                table: "messages",
                column: "ReplyToMessageId",
                principalSchema: "mirage",
                principalTable: "messages",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_messages_messages_ReplyToMessageId",
                schema: "mirage",
                table: "messages");

            migrationBuilder.DropIndex(
                name: "IX_messages_ReplyToMessageId",
                schema: "mirage",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "ReplyToMessageId",
                schema: "mirage",
                table: "messages");
        }
    }
}
