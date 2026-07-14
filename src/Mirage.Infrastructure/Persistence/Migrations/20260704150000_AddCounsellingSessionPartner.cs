using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mirage.Infrastructure.Persistence.Migrations;

[DbContext(typeof(MirageDbContext))]
[Migration("20260704150000_AddCounsellingSessionPartner")]
public sealed class AddCounsellingSessionPartner : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE mirage.counselling_sessions
                ADD COLUMN IF NOT EXISTS "PartnerUserId" uuid NULL,
                ADD COLUMN IF NOT EXISTS "PartnerAccepted" boolean NOT NULL DEFAULT false;

            DO $$
            BEGIN
                IF NOT EXISTS (
                    SELECT 1 FROM pg_constraint
                    WHERE conname = 'FK_counselling_sessions_AspNetUsers_PartnerUserId'
                      AND conrelid = 'mirage.counselling_sessions'::regclass
                ) THEN
                    ALTER TABLE mirage.counselling_sessions
                    ADD CONSTRAINT "FK_counselling_sessions_AspNetUsers_PartnerUserId"
                    FOREIGN KEY ("PartnerUserId")
                    REFERENCES mirage."AspNetUsers" ("Id")
                    ON DELETE SET NULL;
                END IF;
            END $$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE mirage.counselling_sessions
                DROP COLUMN IF EXISTS "PartnerUserId",
                DROP COLUMN IF EXISTS "PartnerAccepted";
            """);
    }
}
