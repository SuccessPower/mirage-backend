using System.Globalization;

namespace Mirage.Domain.Services;

public static class CountryMetadata
{
    private static readonly Lazy<IReadOnlyDictionary<string, string>> Names = new(() =>
        CultureInfo.GetCultures(CultureTypes.SpecificCultures)
            .Select(c => new RegionInfo(c.Name))
            .GroupBy(r => r.EnglishName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().TwoLetterISORegionName, StringComparer.OrdinalIgnoreCase));

    private const string Africa = "DZ AO BJ BW BF BI CV CM CF TD KM CG CD CI DJ EG GQ ER SZ ET GA GM GH GN GW KE LS LR LY MG MW ML MR MU MA MZ NA NE NG RW ST SN SC SL SO ZA SS SD TZ TG TN UG EH ZM ZW RE YT";
    private const string Europe = "AL AD AT BY BE BA BG HR CY CZ DK EE FI FR DE GR HU IS IE IT XK LV LI LT LU MT MD MC ME NL MK NO PL PT RO RU SM RS SK SI ES SE CH UA GB VA AX FO GG GI IM JE SJ";
    private const string Asia = "AF AM AZ BH BD BT BN KH CN GE IN ID IR IQ IL JP JO KZ KW KG LA LB MY MV MN MM NP KP OM PK PS PH QA SA SG KR LK SY TW TJ TH TL TR TM AE UZ VN YE HK MO";
    private const string NorthAmerica = "AG BS BB BZ CA CR CU DM DO SV GD GT HT HN JM MX NI PA KN LC VC US AI AW BM BQ VG KY CW GL GP MQ MS PR SX TC VI";
    private const string SouthAmerica = "AR BO BR CL CO EC GY PY PE SR UY VE GF FK GS";
    private const string Oceania = "AU FJ KI MH FM NR NZ PW PG WS SB TO TV VU AS CK GU MP NC NU NF PF PN TK WF";

    public static string? ResolveCountryCode(string? country, string? suppliedCode = null)
    {
        var code = suppliedCode?.Trim().ToUpperInvariant();
        if (code?.Length == 2) return code;
        if (string.IsNullOrWhiteSpace(country)) return null;
        if (country.Trim().Length == 2) return country.Trim().ToUpperInvariant();
        return Names.Value.GetValueOrDefault(country.Trim());
    }

    public static string? ResolveContinentCode(string? countryCode)
    {
        var code = countryCode?.Trim().ToUpperInvariant();
        if (code is null) return null;
        static bool Has(string list, string value) => $" {list} ".Contains($" {value} ", StringComparison.Ordinal);
        if (Has(Africa, code)) return "AF";
        if (Has(Europe, code)) return "EU";
        if (Has(Asia, code)) return "AS";
        if (Has(NorthAmerica, code)) return "NA";
        if (Has(SouthAmerica, code)) return "SA";
        if (Has(Oceania, code)) return "OC";
        return code == "AQ" ? "AN" : null;
    }
}
