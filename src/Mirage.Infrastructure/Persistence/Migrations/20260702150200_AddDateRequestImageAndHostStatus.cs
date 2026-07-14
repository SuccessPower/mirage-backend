using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mirage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(Mirage.Infrastructure.Persistence.MirageDbContext))]
    [Migration("20260702150200_AddDateRequestImageAndHostStatus")]
    public partial class AddDateRequestImageAndHostStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImageUrl",
                schema: "mirage",
                table: "date_requests",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequestorIsRecommended",
                schema: "mirage",
                table: "date_requests",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RequestorIsVerified",
                schema: "mirage",
                table: "date_requests",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ImageUrl", schema: "mirage", table: "date_requests");
            migrationBuilder.DropColumn(name: "RequestorIsRecommended", schema: "mirage", table: "date_requests");
            migrationBuilder.DropColumn(name: "RequestorIsVerified", schema: "mirage", table: "date_requests");
        }
    }
}
