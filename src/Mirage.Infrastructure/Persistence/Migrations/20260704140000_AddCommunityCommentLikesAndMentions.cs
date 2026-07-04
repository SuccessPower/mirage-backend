using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mirage.Infrastructure.Persistence.Migrations;

[Migration("20260704140000_AddCommunityCommentLikesAndMentions")]
public sealed class AddCommunityCommentLikesAndMentions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE mirage.community_post_comments
                ADD COLUMN IF NOT EXISTS "MentionedUserIds" uuid[] NOT NULL DEFAULT '{}';

            CREATE TABLE IF NOT EXISTS mirage.community_post_comment_likes (
                "Id" uuid NOT NULL,
                "CommentId" uuid NOT NULL,
                "UserId" uuid NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NOT NULL
            );

            DO $$
            BEGIN
                IF NOT EXISTS (
                    SELECT 1 FROM pg_constraint
                    WHERE conname = 'PK_community_post_comment_likes'
                      AND conrelid = 'mirage.community_post_comment_likes'::regclass
                ) THEN
                    ALTER TABLE mirage.community_post_comment_likes
                    ADD CONSTRAINT "PK_community_post_comment_likes" PRIMARY KEY ("Id");
                END IF;

                IF NOT EXISTS (
                    SELECT 1 FROM pg_constraint
                    WHERE conname = 'FK_community_post_comment_likes_community_post_comments_CommentId'
                      AND conrelid = 'mirage.community_post_comment_likes'::regclass
                ) THEN
                    ALTER TABLE mirage.community_post_comment_likes
                    ADD CONSTRAINT "FK_community_post_comment_likes_community_post_comments_CommentId"
                    FOREIGN KEY ("CommentId")
                    REFERENCES mirage.community_post_comments ("Id")
                    ON DELETE CASCADE;
                END IF;

                IF NOT EXISTS (
                    SELECT 1 FROM pg_constraint
                    WHERE conname = 'FK_community_post_comment_likes_AspNetUsers_UserId'
                      AND conrelid = 'mirage.community_post_comment_likes'::regclass
                ) THEN
                    ALTER TABLE mirage.community_post_comment_likes
                    ADD CONSTRAINT "FK_community_post_comment_likes_AspNetUsers_UserId"
                    FOREIGN KEY ("UserId")
                    REFERENCES mirage."AspNetUsers" ("Id")
                    ON DELETE CASCADE;
                END IF;
            END $$;

            CREATE UNIQUE INDEX IF NOT EXISTS "IX_community_post_comment_likes_CommentId_UserId"
                ON mirage.community_post_comment_likes ("CommentId", "UserId");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TABLE IF EXISTS mirage.community_post_comment_likes;
            ALTER TABLE mirage.community_post_comments DROP COLUMN IF EXISTS "MentionedUserIds";
            """);
    }
}
