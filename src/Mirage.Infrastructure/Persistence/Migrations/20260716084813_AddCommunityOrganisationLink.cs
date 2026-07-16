using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mirage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCommunityOrganisationLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OrganisationId",
                schema: "mirage",
                table: "communities",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_communities_OrganisationId_Category",
                schema: "mirage",
                table: "communities",
                columns: new[] { "OrganisationId", "Category" },
                unique: true,
                filter: "\"OrganisationId\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_communities_organisations_OrganisationId",
                schema: "mirage",
                table: "communities",
                column: "OrganisationId",
                principalSchema: "mirage",
                principalTable: "organisations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_communities_organisations_OrganisationId",
                schema: "mirage",
                table: "communities");

            migrationBuilder.DropIndex(
                name: "IX_communities_OrganisationId_Category",
                schema: "mirage",
                table: "communities");

            migrationBuilder.DropColumn(
                name: "OrganisationId",
                schema: "mirage",
                table: "communities");
        }
    }
}
