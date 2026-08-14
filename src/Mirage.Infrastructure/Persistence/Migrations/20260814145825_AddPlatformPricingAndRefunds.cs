using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mirage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformPricingAndRefunds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RefundNote",
                schema: "mirage",
                table: "payments",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RefundProviderReference",
                schema: "mirage",
                table: "payments",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RefundReason",
                schema: "mirage",
                table: "payments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RefundedAmount",
                schema: "mirage",
                table: "payments",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RefundedAt",
                schema: "mirage",
                table: "payments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RefundedByUserId",
                schema: "mirage",
                table: "payments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "platform_pricing",
                schema: "mirage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MinSessionFee = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    MaxSessionFee = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_platform_pricing", x => x.Id);
                    table.ForeignKey(
                        name: "FK_platform_pricing_AspNetUsers_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalSchema: "mirage",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_platform_pricing_UpdatedByUserId",
                schema: "mirage",
                table: "platform_pricing",
                column: "UpdatedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "platform_pricing",
                schema: "mirage");

            migrationBuilder.DropColumn(
                name: "RefundNote",
                schema: "mirage",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "RefundProviderReference",
                schema: "mirage",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "RefundReason",
                schema: "mirage",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "RefundedAmount",
                schema: "mirage",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "RefundedAt",
                schema: "mirage",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "RefundedByUserId",
                schema: "mirage",
                table: "payments");
        }
    }
}
