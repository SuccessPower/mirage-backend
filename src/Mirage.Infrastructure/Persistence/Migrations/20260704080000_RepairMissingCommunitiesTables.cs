using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mirage.Infrastructure.Persistence.Migrations;

[DbContext(typeof(MirageDbContext))]
[Migration("20260704080000_RepairMissingCommunitiesTables")]
public sealed class RepairMissingCommunitiesTables : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS mirage.communities (
                "Id" uuid NOT NULL,
                "CreatedByUserId" uuid NOT NULL,
                "Name" character varying(120) NOT NULL,
                "Category" character varying(80) NOT NULL,
                "Description" character varying(1000) NOT NULL,
                "Status" integer NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NOT NULL
            );

            CREATE TABLE IF NOT EXISTS mirage.community_members (
                "Id" uuid NOT NULL,
                "CommunityId" uuid NOT NULL,
                "UserId" uuid NOT NULL,
                "Role" integer NOT NULL,
                "LeftAt" timestamp with time zone NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NOT NULL
            );

            CREATE TABLE IF NOT EXISTS mirage.community_posts (
                "Id" uuid NOT NULL,
                "CommunityId" uuid NOT NULL,
                "AuthorUserId" uuid NOT NULL,
                "Body" character varying(2000) NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NOT NULL
            );

            DO $$
            BEGIN
                IF NOT EXISTS (
                    SELECT 1 FROM pg_constraint
                    WHERE conname = 'PK_communities'
                      AND conrelid = 'mirage.communities'::regclass
                ) THEN
                    ALTER TABLE mirage.communities
                    ADD CONSTRAINT "PK_communities" PRIMARY KEY ("Id");
                END IF;

                IF NOT EXISTS (
                    SELECT 1 FROM pg_constraint
                    WHERE conname = 'FK_communities_AspNetUsers_CreatedByUserId'
                      AND conrelid = 'mirage.communities'::regclass
                ) THEN
                    ALTER TABLE mirage.communities
                    ADD CONSTRAINT "FK_communities_AspNetUsers_CreatedByUserId"
                    FOREIGN KEY ("CreatedByUserId")
                    REFERENCES mirage."AspNetUsers" ("Id")
                    ON DELETE RESTRICT;
                END IF;

                IF NOT EXISTS (
                    SELECT 1 FROM pg_constraint
                    WHERE conname = 'PK_community_members'
                      AND conrelid = 'mirage.community_members'::regclass
                ) THEN
                    ALTER TABLE mirage.community_members
                    ADD CONSTRAINT "PK_community_members" PRIMARY KEY ("Id");
                END IF;

                IF NOT EXISTS (
                    SELECT 1 FROM pg_constraint
                    WHERE conname = 'FK_community_members_AspNetUsers_UserId'
                      AND conrelid = 'mirage.community_members'::regclass
                ) THEN
                    ALTER TABLE mirage.community_members
                    ADD CONSTRAINT "FK_community_members_AspNetUsers_UserId"
                    FOREIGN KEY ("UserId")
                    REFERENCES mirage."AspNetUsers" ("Id")
                    ON DELETE CASCADE;
                END IF;

                IF NOT EXISTS (
                    SELECT 1 FROM pg_constraint
                    WHERE conname = 'FK_community_members_communities_CommunityId'
                      AND conrelid = 'mirage.community_members'::regclass
                ) THEN
                    ALTER TABLE mirage.community_members
                    ADD CONSTRAINT "FK_community_members_communities_CommunityId"
                    FOREIGN KEY ("CommunityId")
                    REFERENCES mirage.communities ("Id")
                    ON DELETE CASCADE;
                END IF;

                IF NOT EXISTS (
                    SELECT 1 FROM pg_constraint
                    WHERE conname = 'PK_community_posts'
                      AND conrelid = 'mirage.community_posts'::regclass
                ) THEN
                    ALTER TABLE mirage.community_posts
                    ADD CONSTRAINT "PK_community_posts" PRIMARY KEY ("Id");
                END IF;

                IF NOT EXISTS (
                    SELECT 1 FROM pg_constraint
                    WHERE conname = 'FK_community_posts_AspNetUsers_AuthorUserId'
                      AND conrelid = 'mirage.community_posts'::regclass
                ) THEN
                    ALTER TABLE mirage.community_posts
                    ADD CONSTRAINT "FK_community_posts_AspNetUsers_AuthorUserId"
                    FOREIGN KEY ("AuthorUserId")
                    REFERENCES mirage."AspNetUsers" ("Id")
                    ON DELETE RESTRICT;
                END IF;

                IF NOT EXISTS (
                    SELECT 1 FROM pg_constraint
                    WHERE conname = 'FK_community_posts_communities_CommunityId'
                      AND conrelid = 'mirage.community_posts'::regclass
                ) THEN
                    ALTER TABLE mirage.community_posts
                    ADD CONSTRAINT "FK_community_posts_communities_CommunityId"
                    FOREIGN KEY ("CommunityId")
                    REFERENCES mirage.communities ("Id")
                    ON DELETE CASCADE;
                END IF;
            END $$;

            CREATE INDEX IF NOT EXISTS "IX_communities_Category"
                ON mirage.communities ("Category");

            CREATE INDEX IF NOT EXISTS "IX_communities_CreatedByUserId"
                ON mirage.communities ("CreatedByUserId");

            CREATE INDEX IF NOT EXISTS "IX_communities_Status"
                ON mirage.communities ("Status");

            CREATE UNIQUE INDEX IF NOT EXISTS "IX_community_members_CommunityId_UserId"
                ON mirage.community_members ("CommunityId", "UserId");

            CREATE INDEX IF NOT EXISTS "IX_community_members_UserId_LeftAt"
                ON mirage.community_members ("UserId", "LeftAt");

            CREATE INDEX IF NOT EXISTS "IX_community_posts_AuthorUserId"
                ON mirage.community_posts ("AuthorUserId");

            CREATE INDEX IF NOT EXISTS "IX_community_posts_CommunityId_CreatedAt"
                ON mirage.community_posts ("CommunityId", "CreatedAt");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
