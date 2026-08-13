using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mirage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHearthFeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_community_post_likes_PostId_UserId",
                schema: "mirage",
                table: "community_post_likes");

            // PostKind.Everyday — every post written before Hearth existed is an everyday post.
            // The default must not be EF's 0: that is not a member of the enum, and those rows
            // would silently fall out of every kind filter on the feed.
            migrationBuilder.AddColumn<int>(
                name: "Kind",
                schema: "mirage",
                table: "community_posts",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "Place",
                schema: "mirage",
                table: "community_posts",
                type: "text",
                nullable: true);

            // PostReactionKind.Love — every like already in the table is a Love. Backfilling 0
            // would drop the like count to zero on every post in the database, since both the
            // community and Hearth read paths now filter on Reaction.
            migrationBuilder.AddColumn<int>(
                name: "Reaction",
                schema: "mirage",
                table: "community_post_likes",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateIndex(
                name: "IX_community_post_likes_PostId_UserId_Reaction",
                schema: "mirage",
                table: "community_post_likes",
                columns: new[] { "PostId", "UserId", "Reaction" },
                unique: true);

            // Hearth is a singleton: exactly one platform-wide community holds the married feed.
            // It is created lazily by the first married member to open the page, so without this
            // two simultaneous first visits would each create one and split the feed in half.
            // Expressed as raw SQL because it is a filtered index, which EF cannot model here.
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX "IX_communities_hearth_singleton"
                ON mirage.communities ("Category")
                WHERE "Category" = 'Hearth' AND "OrganisationId" IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP INDEX IF EXISTS mirage."IX_communities_hearth_singleton";""");

            migrationBuilder.DropIndex(
                name: "IX_community_post_likes_PostId_UserId_Reaction",
                schema: "mirage",
                table: "community_post_likes");

            migrationBuilder.DropColumn(
                name: "Kind",
                schema: "mirage",
                table: "community_posts");

            migrationBuilder.DropColumn(
                name: "Place",
                schema: "mirage",
                table: "community_posts");

            migrationBuilder.DropColumn(
                name: "Reaction",
                schema: "mirage",
                table: "community_post_likes");

            migrationBuilder.CreateIndex(
                name: "IX_community_post_likes_PostId_UserId",
                schema: "mirage",
                table: "community_post_likes",
                columns: new[] { "PostId", "UserId" },
                unique: true);
        }
    }
}
