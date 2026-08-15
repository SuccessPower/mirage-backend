using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mirage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHiddenByAdminIdAndWarningUpdateNotified : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "HiddenByAdminId",
                schema: "mirage",
                table: "AspNetUsers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ProfileUpdateNotifiedAt",
                schema: "mirage",
                table: "account_warnings",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HiddenByAdminId",
                schema: "mirage",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "ProfileUpdateNotifiedAt",
                schema: "mirage",
                table: "account_warnings");
        }
    }
}
