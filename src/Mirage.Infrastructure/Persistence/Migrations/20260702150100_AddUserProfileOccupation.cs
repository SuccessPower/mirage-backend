using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mirage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(Mirage.Infrastructure.Persistence.MirageDbContext))]
    [Migration("20260702150100_AddUserProfileOccupation")]
    public partial class AddUserProfileOccupation : Migration
    {
        /// <inheritdoc />
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
                        WHERE table_schema = 'mirage' AND table_name = 'profiles' AND column_name = 'Occupation'
                    ) THEN
                        ALTER TABLE mirage.profiles DROP COLUMN "Occupation";
                    END IF;
                END $$;
                """);
        }
    }
}
