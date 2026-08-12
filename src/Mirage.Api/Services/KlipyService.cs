using System.Net.Http.Json;
using System.Text.Json;

namespace Mirage.Api.Services;

/// <summary>A single GIF result, flattened from the provider's nested media shape.</summary>
public sealed record GifResult(string Id, string Description, string Url, string PreviewUrl,
    int Width, int Height);

/// <summary>
/// A page of GIF results. Klipy paginates by page number rather than an opaque cursor, so
/// <paramref name="NextPage"/> is null once the provider reports no further pages.
/// </summary>
public sealed record GifSearchResult(IReadOnlyList<GifResult> Results, int? NextPage);

// Klipy, not Tenor: Google shut the Tenor API down on 30 June 2026 and stopped issuing new keys
// that January, so it was never an option for a new integration. Klipy is the migration path most
// former Tenor consumers took — it is built by ex-Tenor staff and is deliberately close in shape.
//
// Proxied through our own API so the key never reaches the browser bundle, which also means the
// GIF *search* stays between the user and Mirage. The tradeoff we cannot remove: the chosen GIF is
// still fetched from Klipy's CDN when the bubble renders. That is why GIFs are a distinct
// MessageType and are never treated as end-to-end encrypted content.
public sealed class KlipyService(HttpClient http, IConfiguration configuration)
{
    private const string BaseUrl = "https://api.klipy.com/api/v1";

    // "g" is Klipy's strictest rating (the ladder is g, pg, pg-13, r). Fixed rather than
    // configurable because this is a faith-oriented platform and the picker sits inside private
    // conversations.
    private const string Rating = "g";

    private string ApiKey =>
        configuration["Klipy:ApiKey"] ?? throw new InvalidOperationException("Klipy:ApiKey is not configured.");

    public bool IsConfigured => !string.IsNullOrWhiteSpace(configuration["Klipy:ApiKey"]);

    public Task<GifSearchResult> SearchAsync(string query, int limit, int page,
        CancellationToken cancellationToken) =>
        FetchAsync($"gifs/search?q={Uri.EscapeDataString(query)}", limit, page, cancellationToken);

    public Task<GifSearchResult> TrendingAsync(int limit, int page, CancellationToken cancellationToken) =>
        FetchAsync("gifs/trending", limit, page, cancellationToken);

    private async Task<GifSearchResult> FetchAsync(string endpoint, int limit, int page,
        CancellationToken cancellationToken)
    {
        // The API key is a path segment for Klipy, not a query parameter.
        var url = $"{BaseUrl}/{ApiKey}/{endpoint}{(endpoint.Contains('?') ? '&' : '?')}"
            + $"per_page={limit}&page={page}&rating={Rating}";

        var response = await http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);

        // Shape: { result: true, data: { data: [...], current_page, per_page, has_next } }
        if (!body.TryGetProperty("data", out var envelope) || envelope.ValueKind != JsonValueKind.Object)
            return new GifSearchResult([], null);

        var results = new List<GifResult>();
        if (envelope.TryGetProperty("data", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
            {
                var media = ReadMedia(item);
                if (media.Count == 0) continue;
                // Largest variant is the one sent into the conversation; smallest is the grid
                // thumbnail. Choosing by pixel area rather than by variant name keeps this working
                // regardless of what the provider calls its sizes.
                var full = media[^1];
                var preview = media[0];
                results.Add(new GifResult(
                    ReadString(item, "id") ?? string.Empty,
                    ReadString(item, "title") ?? ReadString(item, "slug") ?? "GIF",
                    full.Url, preview.Url, full.Width, full.Height));
            }
        }

        var hasNext = envelope.TryGetProperty("has_next", out var next)
            && next.ValueKind == JsonValueKind.True;
        return new GifSearchResult(results, hasNext ? page + 1 : null);
    }

    private sealed record MediaVariant(string Url, int Width, int Height)
    {
        // Zero dimensions sort first so an unmeasured variant is treated as a thumbnail candidate
        // rather than being mistaken for the full-size asset.
        public long Area => (long)Width * Height;
    }

    /// <summary>
    /// Collects every playable media variant on a result, ascending by pixel area.
    /// </summary>
    /// <remarks>
    /// Deliberately structure-agnostic. Klipy nests variants under <c>files</c> keyed by size, but
    /// the key names are not part of its published contract, and flatter <c>url</c>/<c>src</c>
    /// forms appear in the wild too. Walking for any object that carries a URL — instead of
    /// hard-coding variant names — means a rename upstream degrades to "fewer sizes found" rather
    /// than an empty picker.
    /// </remarks>
    private static List<MediaVariant> ReadMedia(JsonElement item)
    {
        var found = new List<MediaVariant>();

        if (item.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Object)
            CollectVariants(files, found, depth: 0);

        // Flat fallbacks, used when a result carries a single URL rather than a variant set.
        if (found.Count == 0)
        {
            foreach (var name in (string[])["url", "src", "proxy_src", "preview_url"])
            {
                var value = ReadString(item, name);
                if (!string.IsNullOrWhiteSpace(value))
                    found.Add(new MediaVariant(value, ReadInt(item, "width"), ReadInt(item, "height")));
            }
        }

        return [.. found.OrderBy(variant => variant.Area)];
    }

    // Two levels is enough for files → size → format nesting; the bound stops a surprising payload
    // from turning this into an unbounded walk.
    private static void CollectVariants(JsonElement node, List<MediaVariant> found, int depth)
    {
        if (depth > 2) return;
        foreach (var property in node.EnumerateObject())
        {
            var value = property.Value;
            if (value.ValueKind == JsonValueKind.String)
            {
                // A bare "key": "https://…" variant.
                var direct = value.GetString();
                if (IsMediaUrl(direct)) found.Add(new MediaVariant(direct!, 0, 0));
                continue;
            }
            if (value.ValueKind != JsonValueKind.Object) continue;

            var url = ReadString(value, "url") ?? ReadString(value, "src");
            if (!string.IsNullOrWhiteSpace(url))
                found.Add(new MediaVariant(url, ReadInt(value, "width"), ReadInt(value, "height")));
            else
                CollectVariants(value, found, depth + 1);
        }
    }

    private static bool IsMediaUrl(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.StartsWith("http", StringComparison.OrdinalIgnoreCase);

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    private static int ReadInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var parsed) ? parsed : 0;
}
