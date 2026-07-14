using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mirage.Infrastructure.Persistence.Migrations;

[DbContext(typeof(MirageDbContext))]
[Migration("20260703113000_AddCommunities")]
public sealed class AddCommunities : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "communities",
            schema: "mirage",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                Category = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                Status = table.Column<int>(type: "integer", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_communities", x => x.Id);
                table.ForeignKey(
                    name: "FK_communities_AspNetUsers_CreatedByUserId",
                    column: x => x.CreatedByUserId,
                    principalSchema: "mirage",
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "community_members",
            schema: "mirage",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CommunityId = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                Role = table.Column<int>(type: "integer", nullable: false),
                LeftAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_community_members", x => x.Id);
                table.ForeignKey(
                    name: "FK_community_members_AspNetUsers_UserId",
                    column: x => x.UserId,
                    principalSchema: "mirage",
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_community_members_communities_CommunityId",
                    column: x => x.CommunityId,
                    principalSchema: "mirage",
                    principalTable: "communities",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "community_posts",
            schema: "mirage",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CommunityId = table.Column<Guid>(type: "uuid", nullable: false),
                AuthorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                Body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_community_posts", x => x.Id);
                table.ForeignKey(
                    name: "FK_community_posts_AspNetUsers_AuthorUserId",
                    column: x => x.AuthorUserId,
                    principalSchema: "mirage",
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_community_posts_communities_CommunityId",
                    column: x => x.CommunityId,
                    principalSchema: "mirage",
                    principalTable: "communities",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_communities_Category",
            schema: "mirage",
            table: "communities",
            column: "Category");

        migrationBuilder.CreateIndex(
            name: "IX_communities_CreatedByUserId",
            schema: "mirage",
            table: "communities",
            column: "CreatedByUserId");

        migrationBuilder.CreateIndex(
            name: "IX_communities_Status",
            schema: "mirage",
            table: "communities",
            column: "Status");

        migrationBuilder.CreateIndex(
            name: "IX_community_members_CommunityId_UserId",
            schema: "mirage",
            table: "community_members",
            columns: ["CommunityId", "UserId"],
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_community_members_UserId_LeftAt",
            schema: "mirage",
            table: "community_members",
            columns: ["UserId", "LeftAt"]);

        migrationBuilder.CreateIndex(
            name: "IX_community_posts_AuthorUserId",
            schema: "mirage",
            table: "community_posts",
            column: "AuthorUserId");

        migrationBuilder.CreateIndex(
            name: "IX_community_posts_CommunityId_CreatedAt",
            schema: "mirage",
            table: "community_posts",
            columns: ["CommunityId", "CreatedAt"]);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "community_posts", schema: "mirage");
        migrationBuilder.DropTable(name: "community_members", schema: "mirage");
        migrationBuilder.DropTable(name: "communities", schema: "mirage");
    }
}
