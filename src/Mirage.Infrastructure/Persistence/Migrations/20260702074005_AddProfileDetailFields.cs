using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mirage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProfileDetailFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "HeightInches",
                schema: "mirage",
                table: "profiles",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreferredLanguage",
                schema: "mirage",
                table: "profiles",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RelationshipStatus",
                schema: "mirage",
                table: "profiles",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Sex",
                schema: "mirage",
                table: "profiles",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SkinTone",
                schema: "mirage",
                table: "profiles",
                type: "integer",
                nullable: true);

            // ChatRequestedByUserId (column/index/FK) was already applied to production directly
            // by an earlier deploy that never got recorded in __EFMigrationsHistory, so the plain
            // AddColumn/CreateIndex/AddForeignKey calls collide there. Guard each with an existence
            // check so this migration is safe to run whether or not that drift is present.
            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_schema = 'mirage' AND table_name = 'matches' AND column_name = 'ChatRequestedByUserId'
                    ) THEN
                        ALTER TABLE mirage.matches ADD ""ChatRequestedByUserId"" uuid;
                    END IF;
                END $$;
            ");

            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM pg_indexes
                        WHERE schemaname = 'mirage' AND tablename = 'matches' AND indexname = 'IX_matches_ChatRequestedByUserId'
                    ) THEN
                        CREATE INDEX ""IX_matches_ChatRequestedByUserId"" ON mirage.matches (""ChatRequestedByUserId"");
                    END IF;
                END $$;
            ");

            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM pg_constraint WHERE conname = 'FK_matches_AspNetUsers_ChatRequestedByUserId'
                    ) THEN
                        ALTER TABLE mirage.matches ADD CONSTRAINT ""FK_matches_AspNetUsers_ChatRequestedByUserId""
                            FOREIGN KEY (""ChatRequestedByUserId"") REFERENCES mirage.""AspNetUsers"" (""Id"") ON DELETE RESTRICT;
                    END IF;
                END $$;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"ALTER TABLE mirage.matches DROP CONSTRAINT IF EXISTS ""FK_matches_AspNetUsers_ChatRequestedByUserId"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS mirage.""IX_matches_ChatRequestedByUserId"";");

            migrationBuilder.DropColumn(
                name: "HeightInches",
                schema: "mirage",
                table: "profiles");

            migrationBuilder.DropColumn(
                name: "PreferredLanguage",
                schema: "mirage",
                table: "profiles");

            migrationBuilder.DropColumn(
                name: "RelationshipStatus",
                schema: "mirage",
                table: "profiles");

            migrationBuilder.DropColumn(
                name: "Sex",
                schema: "mirage",
                table: "profiles");

            migrationBuilder.DropColumn(
                name: "SkinTone",
                schema: "mirage",
                table: "profiles");

            migrationBuilder.DropColumn(
                name: "ChatRequestedByUserId",
                schema: "mirage",
                table: "matches");
        }
    }
}
