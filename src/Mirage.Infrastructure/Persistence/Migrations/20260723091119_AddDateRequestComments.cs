using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mirage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDateRequestComments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "date_request_comments",
                schema: "mirage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DateRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Body = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_date_request_comments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_date_request_comments_AspNetUsers_AuthorUserId",
                        column: x => x.AuthorUserId,
                        principalSchema: "mirage",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_date_request_comments_date_requests_DateRequestId",
                        column: x => x.DateRequestId,
                        principalSchema: "mirage",
                        principalTable: "date_requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_date_request_comments_AuthorUserId",
                schema: "mirage",
                table: "date_request_comments",
                column: "AuthorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_date_request_comments_DateRequestId_CreatedAt",
                schema: "mirage",
                table: "date_request_comments",
                columns: new[] { "DateRequestId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "date_request_comments",
                schema: "mirage");
        }
    }
}
