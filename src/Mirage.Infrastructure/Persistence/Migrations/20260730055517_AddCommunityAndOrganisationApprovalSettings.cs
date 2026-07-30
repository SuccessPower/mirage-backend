using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mirage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCommunityAndOrganisationApprovalSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RequireApproval",
                schema: "mirage",
                table: "organisations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                schema: "mirage",
                table: "community_members",
                type: "integer",
                nullable: false,
                defaultValue: 2); // CommunityMemberStatus.Approved — backfills existing members as already-active

            migrationBuilder.AddColumn<bool>(
                name: "RequireApproval",
                schema: "mirage",
                table: "communities",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequireApproval",
                schema: "mirage",
                table: "organisations");

            migrationBuilder.DropColumn(
                name: "Status",
                schema: "mirage",
                table: "community_members");

            migrationBuilder.DropColumn(
                name: "RequireApproval",
                schema: "mirage",
                table: "communities");
        }
    }
}
