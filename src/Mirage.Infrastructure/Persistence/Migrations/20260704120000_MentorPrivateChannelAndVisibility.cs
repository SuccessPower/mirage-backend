using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mirage.Infrastructure.Persistence.Migrations;

[DbContext(typeof(MirageDbContext))]
[Migration("20260704120000_MentorPrivateChannelAndVisibility")]
public sealed class MentorPrivateChannelAndVisibility : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$
            BEGIN
                IF EXISTS (
                    SELECT 1 FROM information_schema.columns
                    WHERE table_schema = 'mirage' AND table_name = 'mentors' AND column_name = 'IsAnonymous'
                ) THEN
                    ALTER TABLE mirage.mentors RENAME COLUMN "IsAnonymous" TO "AllowMenteesToSeeEachOther";
                    ALTER TABLE mirage.mentors ALTER COLUMN "AllowMenteesToSeeEachOther" SET DEFAULT false;
                    UPDATE mirage.mentors SET "AllowMenteesToSeeEachOther" = false;
                ELSIF NOT EXISTS (
                    SELECT 1 FROM information_schema.columns
                    WHERE table_schema = 'mirage' AND table_name = 'mentors' AND column_name = 'AllowMenteesToSeeEachOther'
                ) THEN
                    ALTER TABLE mirage.mentors ADD COLUMN "AllowMenteesToSeeEachOther" boolean NOT NULL DEFAULT false;
                END IF;
            END $$;

            CREATE TABLE IF NOT EXISTS mirage.mentor_messages (
                "Id" uuid NOT NULL,
                "MentorRequestId" uuid NOT NULL,
                "SenderId" uuid NOT NULL,
                "Content" character varying(2000) NOT NULL,
                "Type" integer NOT NULL,
                "AttachmentUrl" text NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_mentor_messages" PRIMARY KEY ("Id"),
                CONSTRAINT "FK_mentor_messages_mentor_requests_MentorRequestId" FOREIGN KEY ("MentorRequestId")
                    REFERENCES mirage.mentor_requests ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_mentor_messages_AspNetUsers_SenderId" FOREIGN KEY ("SenderId")
                    REFERENCES mirage."AspNetUsers" ("Id") ON DELETE RESTRICT
            );

            CREATE INDEX IF NOT EXISTS "IX_mentor_messages_MentorRequestId" ON mirage.mentor_messages ("MentorRequestId");
            CREATE INDEX IF NOT EXISTS "IX_mentor_messages_SenderId" ON mirage.mentor_messages ("SenderId");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TABLE IF EXISTS mirage.mentor_messages;

            DO $$
            BEGIN
                IF EXISTS (
                    SELECT 1 FROM information_schema.columns
                    WHERE table_schema = 'mirage' AND table_name = 'mentors' AND column_name = 'AllowMenteesToSeeEachOther'
                ) THEN
                    ALTER TABLE mirage.mentors RENAME COLUMN "AllowMenteesToSeeEachOther" TO "IsAnonymous";
                    ALTER TABLE mirage.mentors ALTER COLUMN "IsAnonymous" SET DEFAULT true;
                END IF;
            END $$;
            """);
    }
}
