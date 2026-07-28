using System.Globalization;
using System.Text;

namespace Mirage.Domain.Services;

/// <summary>
/// Produces a conservative identity key for church names. The key removes only common
/// organisational suffixes, so brand variants such as "Daystar" and
/// "Daystar Christian Centre" resolve to the same organisation.
/// </summary>
public static class OrganisationIdentity
{
    private static readonly HashSet<string> Suffixes = new(StringComparer.Ordinal)
    {
        "church", "christian", "centre", "center", "chapel", "ministry", "ministries",
        "international", "global", "worldwide", "inc", "incorporated", "organisation",
        "organization"
    };

    private static readonly HashSet<string> AmbiguousKeys = new(StringComparer.Ordinal)
    {
        "christ", "church", "faith", "god", "grace", "hope", "jesus", "love"
    };

    public static string NameKey(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;

        var decomposed = name.Trim().Normalize(NormalizationForm.FormD);
        var cleaned = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                continue;
            cleaned.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : ' ');
        }

        var tokens = cleaned.ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => !Suffixes.Contains(token))
            .ToArray();
        return string.Join(' ', tokens);
    }

    public static bool IsLikelyDuplicate(
        string candidateName,
        string candidateCountry,
        string? candidateWebsite,
        string existingName,
        string existingCountry,
        string? existingWebsite)
    {
        var candidateHost = WebsiteHost(candidateWebsite);
        var existingHost = WebsiteHost(existingWebsite);
        if (candidateHost is not null && existingHost is not null)
            return candidateHost.Equals(existingHost, StringComparison.Ordinal);

        if (!CountryKey(candidateCountry).Equals(CountryKey(existingCountry), StringComparison.Ordinal))
            return false;

        var candidateKey = NameKey(candidateName);
        return candidateKey.Length >= 5
               && !AmbiguousKeys.Contains(candidateKey)
               && candidateKey.Equals(NameKey(existingName), StringComparison.Ordinal);
    }

    private static string CountryKey(string country) =>
        string.Concat(country.Where(char.IsLetterOrDigit)).ToLowerInvariant();

    private static string? WebsiteHost(string? website)
    {
        if (string.IsNullOrWhiteSpace(website)) return null;
        var value = website.Trim();
        if (!value.Contains("://", StringComparison.Ordinal)) value = $"https://{value}";
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return null;
        var host = uri.Host.ToLowerInvariant();
        return host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : host;
    }
}
