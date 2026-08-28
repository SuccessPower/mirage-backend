using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mirage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReconcileApprovedCoupleRelationshipStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Couple approval enforces this invariant for new records. Reconcile couples that
            // were approved before that behavior existed or were imported directly.
            // RelationshipStatus.Married = 5; CoupleStatus.Approved = 2.
            migrationBuilder.Sql("""
                UPDATE mirage.profiles AS p
                SET "RelationshipStatus" = 5,
                    "UpdatedAt" = NOW()
                WHERE p."RelationshipStatus" IS DISTINCT FROM 5
                  AND p."UserId" IN (
                      SELECT c."User1Id" FROM mirage.couples AS c WHERE c."Status" = 2
                      UNION
                      SELECT c."User2Id" FROM mirage.couples AS c WHERE c."Status" = 2
                  );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally irreversible: the previous relationship status cannot be inferred
            // safely after an approved couple established the authoritative married state.
        }
    }
}
