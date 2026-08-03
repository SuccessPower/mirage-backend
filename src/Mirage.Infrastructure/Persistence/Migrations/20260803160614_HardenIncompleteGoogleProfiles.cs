using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mirage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HardenIncompleteGoogleProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Google verifies email ownership, not the completeness or authenticity of a Mirage
            // dating profile. Repair legacy records created before server-side onboarding gates.
            migrationBuilder.Sql("""
                UPDATE mirage.profiles
                SET "IsVerified" = FALSE,
                    "UpdatedAt" = NOW()
                WHERE "IsProfileComplete" = FALSE
                  AND "IsVerified" = TRUE;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
