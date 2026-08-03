using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mirage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCounsellorPayoutApprovalWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PayoutApprovedAt",
                schema: "mirage",
                table: "payments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PayoutApprovedByUserId",
                schema: "mirage",
                table: "payments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayoutFailureReason",
                schema: "mirage",
                table: "payments",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PayoutPaidAt",
                schema: "mirage",
                table: "payments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayoutReference",
                schema: "mirage",
                table: "payments",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PayoutStatus",
                schema: "mirage",
                table: "payments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ProviderTransferId",
                schema: "mirage",
                table: "payments",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaystackTransferRecipientCode",
                schema: "mirage",
                table: "counsellors",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_payments_PayoutReference",
                schema: "mirage",
                table: "payments",
                column: "PayoutReference",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_payments_PayoutReference",
                schema: "mirage",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "PayoutApprovedAt",
                schema: "mirage",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "PayoutApprovedByUserId",
                schema: "mirage",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "PayoutFailureReason",
                schema: "mirage",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "PayoutPaidAt",
                schema: "mirage",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "PayoutReference",
                schema: "mirage",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "PayoutStatus",
                schema: "mirage",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "ProviderTransferId",
                schema: "mirage",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "PaystackTransferRecipientCode",
                schema: "mirage",
                table: "counsellors");
        }
    }
}
