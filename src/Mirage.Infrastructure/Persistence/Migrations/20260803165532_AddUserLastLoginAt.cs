using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mirage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserLastLoginAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastLoginAt",
                schema: "mirage",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true);

            // Seed existing activity from issued sessions so the dashboard is meaningful
            // immediately after deployment; subsequent successful authentication updates it.
            migrationBuilder.Sql("""
                UPDATE mirage."AspNetUsers" AS users
                SET "LastLoginAt" = sessions."LastLoginAt"
                FROM (
                    SELECT "UserId", MAX("CreatedAt") AS "LastLoginAt"
                    FROM mirage.refresh_tokens
                    GROUP BY "UserId"
                ) AS sessions
                WHERE users."Id" = sessions."UserId";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastLoginAt",
                schema: "mirage",
                table: "AspNetUsers");
        }
    }
}
