using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mirage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(Mirage.Infrastructure.Persistence.MirageDbContext))]
    [Migration("20260702150000_AddOrgEventImageUrl")]
    public partial class AddOrgEventImageUrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_schema = 'mirage' AND table_name = 'org_events' AND column_name = 'ImageUrl'
                    ) THEN
                        ALTER TABLE mirage.org_events ADD COLUMN "ImageUrl" character varying(1000);
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
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
                END $$;
                """);
        }
    }
}
