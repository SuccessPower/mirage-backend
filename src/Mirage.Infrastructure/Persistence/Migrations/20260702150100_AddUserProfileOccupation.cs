using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mirage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(Mirage.Infrastructure.Persistence.MirageDbContext))]
    [Migration("20260702150100_AddUserProfileOccupation")]
    public partial class AddUserProfileOccupation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Occupation",
                schema: "mirage",
                table: "profiles",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Occupation",
                schema: "mirage",
                table: "profiles");
        }
    }
}
