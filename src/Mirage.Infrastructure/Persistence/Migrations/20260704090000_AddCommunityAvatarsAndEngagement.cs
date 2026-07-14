using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mirage.Infrastructure.Persistence.Migrations;

[DbContext(typeof(MirageDbContext))]
[Migration("20260704090000_AddCommunityAvatarsAndEngagement")]
public sealed class AddCommunityAvatarsAndEngagement : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$
            BEGIN
                IF NOT EXISTS (
                    SELECT 1 FROM information_schema.columns
                    WHERE table_schema = 'mirage' AND table_name = 'communities' AND column_name = 'AvatarUrl'
                ) THEN
                    ALTER TABLE mirage.communities ADD COLUMN "AvatarUrl" character varying(1000);
                END IF;

                IF NOT EXISTS (
                    SELECT 1 FROM information_schema.columns
                    WHERE table_schema = 'mirage' AND table_name = 'communities' AND column_name = 'AvatarKey'
                ) THEN
                    ALTER TABLE mirage.communities ADD COLUMN "AvatarKey" character varying(80);
                END IF;

                IF NOT EXISTS (
                    SELECT 1 FROM information_schema.columns
                    WHERE table_schema = 'mirage' AND table_name = 'community_posts' AND column_name = 'ImageUrl'
                ) THEN
                    ALTER TABLE mirage.community_posts ADD COLUMN "ImageUrl" character varying(1000);
                END IF;
            END $$;

            CREATE TABLE IF NOT EXISTS mirage.community_post_likes (
                "Id" uuid NOT NULL,
                "PostId" uuid NOT NULL,
                "UserId" uuid NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NOT NULL
            );

            CREATE TABLE IF NOT EXISTS mirage.community_post_comments (
                "Id" uuid NOT NULL,
                "PostId" uuid NOT NULL,
                "AuthorUserId" uuid NOT NULL,
                "ParentCommentId" uuid NULL,
                "Body" character varying(2000) NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NOT NULL
            );

            DO $$
            BEGIN
                IF NOT EXISTS (
                    SELECT 1 FROM pg_constraint
                    WHERE conname = 'PK_community_post_likes'
                      AND conrelid = 'mirage.community_post_likes'::regclass
                ) THEN
                    ALTER TABLE mirage.community_post_likes
                    ADD CONSTRAINT "PK_community_post_likes" PRIMARY KEY ("Id");
                END IF;

                IF NOT EXISTS (
                    SELECT 1 FROM pg_constraint
                    WHERE conname = 'FK_community_post_likes_community_posts_PostId'
                      AND conrelid = 'mirage.community_post_likes'::regclass
                ) THEN
                    ALTER TABLE mirage.community_post_likes
                    ADD CONSTRAINT "FK_community_post_likes_community_posts_PostId"
                    FOREIGN KEY ("PostId")
                    REFERENCES mirage.community_posts ("Id")
                    ON DELETE CASCADE;
                END IF;

                IF NOT EXISTS (
                    SELECT 1 FROM pg_constraint
                    WHERE conname = 'FK_community_post_likes_AspNetUsers_UserId'
                      AND conrelid = 'mirage.community_post_likes'::regclass
                ) THEN
                    ALTER TABLE mirage.community_post_likes
                    ADD CONSTRAINT "FK_community_post_likes_AspNetUsers_UserId"
                    FOREIGN KEY ("UserId")
                    REFERENCES mirage."AspNetUsers" ("Id")
                    ON DELETE CASCADE;
                END IF;

                IF NOT EXISTS (
                    SELECT 1 FROM pg_constraint
                    WHERE conname = 'PK_community_post_comments'
                      AND conrelid = 'mirage.community_post_comments'::regclass
                ) THEN
                    ALTER TABLE mirage.community_post_comments
                    ADD CONSTRAINT "PK_community_post_comments" PRIMARY KEY ("Id");
                END IF;

                IF NOT EXISTS (
                    SELECT 1 FROM pg_constraint
                    WHERE conname = 'FK_community_post_comments_community_posts_PostId'
                      AND conrelid = 'mirage.community_post_comments'::regclass
                ) THEN
                    ALTER TABLE mirage.community_post_comments
                    ADD CONSTRAINT "FK_community_post_comments_community_posts_PostId"
                    FOREIGN KEY ("PostId")
                    REFERENCES mirage.community_posts ("Id")
                    ON DELETE CASCADE;
                END IF;

                IF NOT EXISTS (
                    SELECT 1 FROM pg_constraint
                    WHERE conname = 'FK_community_post_comments_community_post_comments_ParentCommentId'
                      AND conrelid = 'mirage.community_post_comments'::regclass
                ) THEN
                    ALTER TABLE mirage.community_post_comments
                    ADD CONSTRAINT "FK_community_post_comments_community_post_comments_ParentCommentId"
                    FOREIGN KEY ("ParentCommentId")
                    REFERENCES mirage.community_post_comments ("Id")
                    ON DELETE CASCADE;
                END IF;

                IF NOT EXISTS (
                    SELECT 1 FROM pg_constraint
                    WHERE conname = 'FK_community_post_comments_AspNetUsers_AuthorUserId'
                      AND conrelid = 'mirage.community_post_comments'::regclass
                ) THEN
                    ALTER TABLE mirage.community_post_comments
                    ADD CONSTRAINT "FK_community_post_comments_AspNetUsers_AuthorUserId"
                    FOREIGN KEY ("AuthorUserId")
                    REFERENCES mirage."AspNetUsers" ("Id")
                    ON DELETE RESTRICT;
                END IF;
            END $$;

            CREATE UNIQUE INDEX IF NOT EXISTS "IX_community_post_likes_PostId_UserId"
                ON mirage.community_post_likes ("PostId", "UserId");

            CREATE INDEX IF NOT EXISTS "IX_community_post_likes_UserId"
                ON mirage.community_post_likes ("UserId");

            CREATE INDEX IF NOT EXISTS "IX_community_post_comments_PostId_CreatedAt"
                ON mirage.community_post_comments ("PostId", "CreatedAt");

            CREATE INDEX IF NOT EXISTS "IX_community_post_comments_AuthorUserId"
                ON mirage.community_post_comments ("AuthorUserId");

            CREATE INDEX IF NOT EXISTS "IX_community_post_comments_ParentCommentId"
                ON mirage.community_post_comments ("ParentCommentId");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
