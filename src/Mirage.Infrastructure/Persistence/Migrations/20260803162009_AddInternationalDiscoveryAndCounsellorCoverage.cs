using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mirage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInternationalDiscoveryAndCounsellorCoverage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContinentCode",
                schema: "mirage",
                table: "profiles",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CountryCode",
                schema: "mirage",
                table: "profiles",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DiscoveryScope",
                schema: "mirage",
                table: "profiles",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string[]>(
                name: "PreferredCountryCodes",
                schema: "mirage",
                table: "profiles",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            var backfillSql = """
                UPDATE mirage.profiles
                SET "CountryCode" = CASE LOWER(TRIM("Country"))
                    WHEN 'nigeria' THEN 'NG' WHEN 'ghana' THEN 'GH' WHEN 'south africa' THEN 'ZA'
                    WHEN 'kenya' THEN 'KE' WHEN 'uganda' THEN 'UG' WHEN 'tanzania' THEN 'TZ'
                    WHEN 'united kingdom' THEN 'GB' WHEN 'ireland' THEN 'IE' WHEN 'france' THEN 'FR'
                    WHEN 'germany' THEN 'DE' WHEN 'italy' THEN 'IT' WHEN 'spain' THEN 'ES'
                    WHEN 'united states' THEN 'US' WHEN 'canada' THEN 'CA' WHEN 'mexico' THEN 'MX'
                    WHEN 'brazil' THEN 'BR' WHEN 'argentina' THEN 'AR' WHEN 'australia' THEN 'AU'
                    WHEN 'new zealand' THEN 'NZ' WHEN 'india' THEN 'IN' WHEN 'china' THEN 'CN'
                    WHEN 'japan' THEN 'JP' WHEN 'united arab emirates' THEN 'AE'
                    ELSE CASE WHEN LENGTH(TRIM("Country")) = 2 THEN UPPER(TRIM("Country")) ELSE NULL END
                END,
                "DiscoveryScope" = 2;

                UPDATE mirage.profiles SET "ContinentCode" = CASE
                    WHEN "CountryCode" IN ('NG','GH','ZA','KE','UG','TZ') THEN 'AF'
                    WHEN "CountryCode" IN ('GB','IE','FR','DE','IT','ES') THEN 'EU'
                    WHEN "CountryCode" IN ('US','CA','MX') THEN 'NA'
                    WHEN "CountryCode" IN ('BR','AR') THEN 'SA'
                    WHEN "CountryCode" IN ('AU','NZ') THEN 'OC'
                    WHEN "CountryCode" IN ('IN','CN','JP','AE') THEN 'AS'
                    ELSE NULL END;

                UPDATE mirage.counsellors SET "AcceptsInternationalClients" = TRUE;
                """;

            migrationBuilder.AddColumn<string>(
                name: "TimeZoneId",
                schema: "mirage",
                table: "profiles",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AcceptsInternationalClients",
                schema: "mirage",
                table: "counsellors",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string[]>(
                name: "ServiceCountryCodes",
                schema: "mirage",
                table: "counsellors",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.Sql(backfillSql);

            migrationBuilder.CreateIndex(
                name: "IX_profiles_ContinentCode_CountryCode_RelationshipStatus",
                schema: "mirage",
                table: "profiles",
                columns: new[] { "ContinentCode", "CountryCode", "RelationshipStatus" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_profiles_ContinentCode_CountryCode_RelationshipStatus",
                schema: "mirage",
                table: "profiles");

            migrationBuilder.DropColumn(
                name: "ContinentCode",
                schema: "mirage",
                table: "profiles");

            migrationBuilder.DropColumn(
                name: "CountryCode",
                schema: "mirage",
                table: "profiles");

            migrationBuilder.DropColumn(
                name: "DiscoveryScope",
                schema: "mirage",
                table: "profiles");

            migrationBuilder.DropColumn(
                name: "PreferredCountryCodes",
                schema: "mirage",
                table: "profiles");

            migrationBuilder.DropColumn(
                name: "TimeZoneId",
                schema: "mirage",
                table: "profiles");

            migrationBuilder.DropColumn(
                name: "AcceptsInternationalClients",
                schema: "mirage",
                table: "counsellors");

            migrationBuilder.DropColumn(
                name: "ServiceCountryCodes",
                schema: "mirage",
                table: "counsellors");
        }
    }
}
