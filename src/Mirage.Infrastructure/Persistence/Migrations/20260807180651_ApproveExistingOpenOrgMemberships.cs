using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mirage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    /// Data backfill: membership requests created before open organisations started admitting
    /// members instantly (see OrganisationMembershipService) were left Pending even though the org
    /// never asked for approval. Approve that backlog and grant the verified tick those members would
    /// have received, exactly as OrganisationEndpoints.ApproveMembershipAsync does. Their church
    /// community membership was already granted at signup, so no community rows need touching.
    public partial class ApproveExistingOpenOrgMemberships : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // OrganisationMemberStatus: 1 = Pending, 2 = Approved; OrganisationStatus: 2 = Approved.
            migrationBuilder.Sql("""
                UPDATE mirage.organisation_members m
                SET "Status" = 2, "UpdatedAt" = now()
                FROM mirage.organisations o
                WHERE o."Id" = m."OrganisationId"
                  AND m."Status" = 1
                  AND o."Status" = 2
                  AND o."RequireApproval" = false;

                UPDATE mirage.profiles p
                SET "IsVerified" = true, "UpdatedAt" = now()
                FROM mirage.organisation_members m
                JOIN mirage.organisations o ON o."Id" = m."OrganisationId"
                WHERE p."UserId" = m."UserId"
                  AND p."IsVerified" = false
                  AND m."Status" = 2
                  AND o."Status" = 2
                  AND o."RequireApproval" = false;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Data backfill — nothing sensible to undo.
        }
    }
}
