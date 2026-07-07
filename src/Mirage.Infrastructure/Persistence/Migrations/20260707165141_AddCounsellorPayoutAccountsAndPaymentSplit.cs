using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mirage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCounsellorPayoutAccountsAndPaymentSplit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CounsellorAmount",
                schema: "mirage",
                table: "payments",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PlatformFeeAmount",
                schema: "mirage",
                table: "payments",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "BankAccountName",
                schema: "mirage",
                table: "counsellors",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankAccountNumber",
                schema: "mirage",
                table: "counsellors",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankCode",
                schema: "mirage",
                table: "counsellors",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankName",
                schema: "mirage",
                table: "counsellors",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FlutterwaveSubaccountId",
                schema: "mirage",
                table: "counsellors",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaystackSubaccountCode",
                schema: "mirage",
                table: "counsellors",
                type: "text",
                nullable: true);

            // Backfill any payments created before this column existed with the same 15%
            // platform / 85% counsellor split the entity now computes at construction time.
            migrationBuilder.Sql(
                "UPDATE mirage.payments SET " +
                "\"PlatformFeeAmount\" = ROUND(\"Amount\" * 0.15, 2), " +
                "\"CounsellorAmount\" = \"Amount\" - ROUND(\"Amount\" * 0.15, 2);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CounsellorAmount",
                schema: "mirage",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "PlatformFeeAmount",
                schema: "mirage",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "BankAccountName",
                schema: "mirage",
                table: "counsellors");

            migrationBuilder.DropColumn(
                name: "BankAccountNumber",
                schema: "mirage",
                table: "counsellors");

            migrationBuilder.DropColumn(
                name: "BankCode",
                schema: "mirage",
                table: "counsellors");

            migrationBuilder.DropColumn(
                name: "BankName",
                schema: "mirage",
                table: "counsellors");

            migrationBuilder.DropColumn(
                name: "FlutterwaveSubaccountId",
                schema: "mirage",
                table: "counsellors");

            migrationBuilder.DropColumn(
                name: "PaystackSubaccountCode",
                schema: "mirage",
                table: "counsellors");
        }
    }
}
