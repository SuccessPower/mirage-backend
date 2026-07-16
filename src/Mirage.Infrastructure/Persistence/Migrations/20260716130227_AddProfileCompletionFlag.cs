using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mirage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProfileCompletionFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Every profile that already exists was created via full registration (password or an
            // already-completed Google signup) — only defaults to true for the backfill; new rows
            // set this explicitly (UserProfile's Google ctor writes false).
            migrationBuilder.AddColumn<bool>(
                name: "IsProfileComplete",
                schema: "mirage",
                table: "profiles",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsProfileComplete",
                schema: "mirage",
                table: "profiles");
        }
    }
}
