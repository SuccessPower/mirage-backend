using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mirage.Infrastructure.Persistence.Migrations;

[Migration("20260704130000_AddCounsellorFreeSessionGate")]
public sealed class AddCounsellorFreeSessionGate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE mirage.counsellors
                ADD COLUMN IF NOT EXISTS "CompletedFreeSessionsCount" integer NOT NULL DEFAULT 0;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE mirage.counsellors DROP COLUMN IF EXISTS "CompletedFreeSessionsCount";
            """);
    }
}
