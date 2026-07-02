using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mirage.Infrastructure.Persistence.Migrations;

[Migration("20260702213000_RepairMissingRuntimeColumns")]
public sealed class RepairMissingRuntimeColumns : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$
            BEGIN
                IF NOT EXISTS (
                    SELECT 1 FROM information_schema.columns
                    WHERE table_schema = 'mirage' AND table_name = 'profiles' AND column_name = 'Occupation'
                ) THEN
                    ALTER TABLE mirage.profiles ADD COLUMN "Occupation" character varying(160);
                END IF;

                IF NOT EXISTS (
                    SELECT 1 FROM information_schema.columns
                    WHERE table_schema = 'mirage' AND table_name = 'date_requests' AND column_name = 'ImageUrl'
                ) THEN
                    ALTER TABLE mirage.date_requests ADD COLUMN "ImageUrl" character varying(1000);
                END IF;

                IF NOT EXISTS (
                    SELECT 1 FROM information_schema.columns
                    WHERE table_schema = 'mirage' AND table_name = 'date_requests' AND column_name = 'RequestorIsRecommended'
                ) THEN
                    ALTER TABLE mirage.date_requests ADD COLUMN "RequestorIsRecommended" boolean NOT NULL DEFAULT false;
                END IF;

                IF NOT EXISTS (
                    SELECT 1 FROM information_schema.columns
                    WHERE table_schema = 'mirage' AND table_name = 'date_requests' AND column_name = 'RequestorIsVerified'
                ) THEN
                    ALTER TABLE mirage.date_requests ADD COLUMN "RequestorIsVerified" boolean NOT NULL DEFAULT false;
                END IF;

                IF NOT EXISTS (
                    SELECT 1 FROM information_schema.columns
                    WHERE table_schema = 'mirage' AND table_name = 'org_events' AND column_name = 'ImageUrl'
                ) THEN
                    ALTER TABLE mirage.org_events ADD COLUMN "ImageUrl" character varying(1000);
                END IF;
            END $$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$
            BEGIN
                IF EXISTS (
                    SELECT 1 FROM information_schema.columns
                    WHERE table_schema = 'mirage' AND table_name = 'org_events' AND column_name = 'ImageUrl'
                ) THEN
                    ALTER TABLE mirage.org_events DROP COLUMN "ImageUrl";
                END IF;

                IF EXISTS (
                    SELECT 1 FROM information_schema.columns
                    WHERE table_schema = 'mirage' AND table_name = 'date_requests' AND column_name = 'RequestorIsVerified'
                ) THEN
                    ALTER TABLE mirage.date_requests DROP COLUMN "RequestorIsVerified";
                END IF;

                IF EXISTS (
                    SELECT 1 FROM information_schema.columns
                    WHERE table_schema = 'mirage' AND table_name = 'date_requests' AND column_name = 'RequestorIsRecommended'
                ) THEN
                    ALTER TABLE mirage.date_requests DROP COLUMN "RequestorIsRecommended";
                END IF;

                IF EXISTS (
                    SELECT 1 FROM information_schema.columns
                    WHERE table_schema = 'mirage' AND table_name = 'date_requests' AND column_name = 'ImageUrl'
                ) THEN
                    ALTER TABLE mirage.date_requests DROP COLUMN "ImageUrl";
                END IF;

                IF EXISTS (
                    SELECT 1 FROM information_schema.columns
                    WHERE table_schema = 'mirage' AND table_name = 'profiles' AND column_name = 'Occupation'
                ) THEN
                    ALTER TABLE mirage.profiles DROP COLUMN "Occupation";
                END IF;
            END $$;
            """);
    }
}
