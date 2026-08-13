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

    // Klipy's strictest safety level (the ladder is off, low, medium, high). Fixed rather than
    // configurable because this is a faith-oriented platform and the picker sits inside private
    // conversations.
    //
    // The parameter is content_filter, not Giphy's "rating" — Klipy ignores unknown parameters, so
    // the old rating=g silently applied no filter at all.
    private const string ContentFilter = "high";

    // Klipy also accepts an optional customer_id for personalisation and ad revenue. Deliberately
    // not sent: it would hand a stable per-user identifier to a third party, which is exactly the
    // leak this proxy exists to prevent.

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
            + $"per_page={limit}&page={page}&content_filter={ContentFilter}";

        var response = await http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
        return Parse(body, page);
    }

    /// <summary>Maps one Klipy response page onto <see cref="GifSearchResult"/>.</summary>
    /// <remarks>Separate from the fetch so the provider's shape can be exercised without HTTP.</remarks>
    internal static GifSearchResult Parse(JsonElement body, int page)
    {
        // Shape: { result: true, data: { data: [...], current_page, per_page, has_next } }
        // A refusal comes back as { result: false, errors: { message: [...] } }, sometimes with a
        // 2xx status. Treating that as "no results" is what made the last outage look like an empty
        // GIF library rather than a broken integration, so it is raised instead of swallowed.
        if (!body.TryGetProperty("data", out var envelope) || envelope.ValueKind != JsonValueKind.Object)
            throw new HttpRequestException($"Klipy returned an unexpected response: {Describe(body)}");

        var results = new List<GifResult>();
        if (envelope.TryGetProperty("data", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
            {
                var media = ReadMedia(item);
                if (media.Count == 0) continue;
                var full = PickFull(media);
                var preview = PickPreview(media);
                results.Add(new GifResult(
                    ReadId(item),
                    ReadString(item, "title") ?? ReadString(item, "slug") ?? "GIF",
                    full.Url, preview.Url, full.Width, full.Height));
            }
        }

        var hasNext = envelope.TryGetProperty("has_next", out var next)
            && next.ValueKind == JsonValueKind.True;
        return new GifSearchResult(results, hasNext ? page + 1 : null);
    }

    // Trimmed so a provider error reaches the logs without dumping a full payload into them.
    private static string Describe(JsonElement body)
    {
        var text = body.ValueKind == JsonValueKind.Undefined ? "(empty)" : body.GetRawText();
        return text.Length <= 300 ? text : text[..300] + "…";
    }

    // Minimum long edge for a grid thumbnail. Klipy's smallest tier is 90px, which is visibly soft
    // in a ~150px picker cell on a retina screen, so the next tier up is preferred where it exists.
    private const int MinPreviewEdge = 200;

    private sealed record MediaVariant(string Url, int Width, int Height, long Bytes)
    {
        // Zero dimensions sort first so an unmeasured variant is treated as a thumbnail candidate
        // rather than being mistaken for the full-size asset.
        public long Area => (long)Width * Height;
        public int LongEdge => Math.Max(Width, Height);
    }

    // What gets sent into the conversation. Klipy publishes hd and md at identical dimensions where
    // md is simply a lighter encode — the hd gif in their own example is 4 MB against roughly a
    // quarter of that for md — so among equally large variants the smallest file wins. Preferring
    // .gif keeps the sent message readable by any client that renders an <img>.
    private static MediaVariant PickFull(List<MediaVariant> media) =>
        media.OrderByDescending(variant => variant.Area)
            .ThenByDescending(variant => IsGif(variant.Url))
            .ThenBy(variant => variant.Bytes == 0 ? long.MaxValue : variant.Bytes)
            .First();

    // What fills the picker grid: the smallest variant that still looks sharp, preferring webp
    // because it is dramatically lighter than the same frame as a gif.
    private static MediaVariant PickPreview(List<MediaVariant> media)
    {
        var sharpEnough = media.Where(variant => variant.LongEdge >= MinPreviewEdge).ToList();
        // Nothing measured or everything tiny — fall back to the largest rather than shipping a
        // thumbnail that may not exist.
        var candidates = sharpEnough.Count > 0 ? sharpEnough : media;
        return candidates
            .OrderBy(variant => variant.Area)
            .ThenByDescending(variant => IsWebp(variant.Url))
            .ThenBy(variant => variant.Bytes == 0 ? long.MaxValue : variant.Bytes)
            .First();
    }

    /// <summary>Collects every renderable media variant on a result.</summary>
    /// <remarks>
    /// Deliberately structure-agnostic about the size tiers. Klipy nests variants under
    /// <c>file</c> keyed by size, but those key names are not part of its published contract, and
    /// flatter <c>url</c>/<c>src</c> forms appear in the wild too. Walking for any object that
    /// carries a URL — instead of hard-coding tier names — means a rename upstream degrades to
    /// "fewer sizes found" rather than an empty picker.
    /// </remarks>
    private static List<MediaVariant> ReadMedia(JsonElement item)
    {
        var found = new List<MediaVariant>();

        // "file", singular — Klipy nests it as file → size (hd/md/sm/xs) → format (gif/webp/mp4/jpg).
        // "files" is accepted too because the flatter plural form shows up in some responses; it
        // was previously the only name checked, which is why every result was silently skipped.
        if ((item.TryGetProperty("file", out var file) || item.TryGetProperty("files", out file))
            && file.ValueKind == JsonValueKind.Object)
            CollectVariants(file, found, depth: 0);

        // Flat fallbacks, used when a result carries a single URL rather than a variant set.
        if (found.Count == 0)
        {
            foreach (var name in (string[])["url", "src", "proxy_src", "preview_url"])
            {
                var value = ReadString(item, name);
                if (!string.IsNullOrWhiteSpace(value))
                    found.Add(new MediaVariant(value, ReadInt(item, "width"), ReadInt(item, "height"),
                        ReadLong(item, "size")));
            }
        }

        // Klipy carries five formats at every size: gif, webp, jpg, mp4 and webm. Only the animated
        // raster formats belong in an <img> — mp4/webm render as a broken image, and the jpg is a
        // still that would freeze the grid. Every format shares its tier's dimensions, so without
        // this the largest-by-area pick was whichever format the provider happened to list last.
        var animated = found.Where(variant => IsAnimated(variant.Url)).ToList();
        return animated.Count > 0 ? animated : found;
    }

    private static bool IsAnimated(string url) => IsGif(url) || IsWebp(url);

    private static bool IsGif(string url) => HasExtension(url, ".gif");

    private static bool IsWebp(string url) => HasExtension(url, ".webp");

    // Compares against the path only: Klipy serves CDN URLs that may carry a query string.
    private static bool HasExtension(string url, string extension)
    {
        var end = url.IndexOfAny(['?', '#']);
        var path = end < 0 ? url : url[..end];
        return path.EndsWith(extension, StringComparison.OrdinalIgnoreCase);
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
                if (IsMediaUrl(direct)) found.Add(new MediaVariant(direct!, 0, 0, 0));
                continue;
            }
            if (value.ValueKind != JsonValueKind.Object) continue;

            var url = ReadString(value, "url") ?? ReadString(value, "src");
            if (!string.IsNullOrWhiteSpace(url))
                found.Add(new MediaVariant(url, ReadInt(value, "width"), ReadInt(value, "height"),
                    ReadLong(value, "size")));
            else
                CollectVariants(value, found, depth + 1);
        }
    }

    private static bool IsMediaUrl(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.StartsWith("http", StringComparison.OrdinalIgnoreCase);

    // Klipy sends id as a JSON number, so reading it as a string yields nothing and every result
    // ends up sharing an empty key — which collapses them to one cell in a keyed client list.
    // Falls back to the slug so a result is still addressable if the shape changes again.
    private static string ReadId(JsonElement item)
    {
        if (item.TryGetProperty("id", out var id))
        {
            if (id.ValueKind == JsonValueKind.String) return id.GetString() ?? string.Empty;
            if (id.ValueKind == JsonValueKind.Number) return id.GetRawText();
        }
        return ReadString(item, "slug") ?? string.Empty;
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    private static long ReadLong(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt64(out var parsed) ? parsed : 0;

    private static int ReadInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var parsed) ? parsed : 0;
}
