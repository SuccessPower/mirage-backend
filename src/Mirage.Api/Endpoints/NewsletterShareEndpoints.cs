using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Mirage.Application.Abstractions;
using Mirage.Domain.Enums;

namespace Mirage.Api.Endpoints;

// Public, unauthenticated HTML endpoint at the exact URL people share
// (themiragehub.com/newsletters/{id}, proxied here by the frontend's Vercel rewrite).
// Link-preview crawlers (WhatsApp, Telegram, iMessage, etc.) don't execute JS, so the SPA can never
// hand them per-story Open Graph tags itself — this endpoint renders a tiny static HTML page with
// the story's title, excerpt and cover baked in, then bounces real browsers straight into the app.
internal static class NewsletterShareEndpoints
{
    private const string FallbackImageUrl = "https://www.themiragehub.com/og-default.png";
    private const string SiteUrl = "https://www.themiragehub.com";

    public static void MapNewsletterShareEndpoints(this WebApplication app)
    {
        app.MapGet("/newsletters/{id:guid}", Get).AllowAnonymous().ExcludeFromDescription();
    }

    private static async Task<IResult> Get(Guid id, IMirageDbContext db, CancellationToken cancellationToken)
    {
        // Same visibility rule as the public reader API: only a sent edition is part of the archive,
        // so nothing in review or on the calendar can be previewed into existence by guessing its id.
        var share = await db.Newsletters.AsNoTracking()
            .Where(x => x.Id == id && x.Status == NewsletterStatus.Sent)
            .Select(x => new { x.Title, x.Excerpt, x.ThumbnailUrl, x.ImageUrls })
            .SingleOrDefaultAsync(cancellationToken);

        var pageUrl = $"{SiteUrl}/newsletters/{id}";

        if (share is null)
            return Results.Content(
                RenderHtml("The Mirage Journal", "Stories and considered guidance from the Mirage community.",
                    FallbackImageUrl, pageUrl, SiteUrl),
                "text/html");

        var title = $"{share.Title} — The Mirage Journal";
        var description = !string.IsNullOrWhiteSpace(share.Excerpt)
            ? share.Excerpt
            : "A story from The Mirage Journal.";
        // The cover mirrors the reader page: the chosen thumbnail, else the first gallery photograph.
        var imageUrl = ToPreviewImage(share.ThumbnailUrl ?? share.ImageUrls.FirstOrDefault() ?? FallbackImageUrl);
        // Same URL as the page itself — the SPA route (NewsletterDetailPage.vue) lives at
        // /newsletters/{id} too, and only bot traffic ever reaches this endpoint (see vercel.json's
        // user-agent-gated rewrite), so real browsers should never actually hit this redirect.
        var redirectUrl = pageUrl;

        return Results.Content(RenderHtml(title, description, imageUrl, pageUrl, redirectUrl), "text/html");
    }

    // WhatsApp (and several other messengers) silently drop og:image files heavier than ~600 KB,
    // and uploaded covers can run to many megabytes. Cloudinary resizes on the fly: injecting a
    // transformation segment after /upload/ serves a 1200x630 JPEG crop of preview weight without
    // touching the stored original. Non-Cloudinary URLs pass through untouched.
    private static string ToPreviewImage(string url)
    {
        const string marker = "/upload/";
        var index = url.IndexOf(marker, StringComparison.Ordinal);
        return index < 0 ? url : url.Insert(index + marker.Length, "w_1200,h_630,c_fill,g_auto,q_auto:good,f_jpg/");
    }

    // All string parameters are raw (un-encoded) — encoding happens once, here, for every field.
    private static string RenderHtml(string title, string description, string imageUrl, string pageUrl,
        string redirectUrl)
    {
        var encodedTitle = HtmlEncoder.Default.Encode(title);
        var encodedDescription = HtmlEncoder.Default.Encode(description);
        var encodedImageUrl = HtmlEncoder.Default.Encode(imageUrl);
        var encodedPageUrl = HtmlEncoder.Default.Encode(pageUrl);
        var encodedRedirectUrl = HtmlEncoder.Default.Encode(redirectUrl);
        var redirectUrlJson = JsonSerializer.Serialize(redirectUrl);

        return $"""
            <!doctype html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <title>{encodedTitle}</title>
            <meta property="og:type" content="article">
            <meta property="og:site_name" content="Mirage">
            <meta property="og:title" content="{encodedTitle}">
            <meta property="og:description" content="{encodedDescription}">
            <meta property="og:image" content="{encodedImageUrl}">
            <meta property="og:image:width" content="1200">
            <meta property="og:image:height" content="630">
            <meta property="og:url" content="{encodedPageUrl}">
            <meta name="twitter:card" content="summary_large_image">
            <meta name="twitter:title" content="{encodedTitle}">
            <meta name="twitter:description" content="{encodedDescription}">
            <meta name="twitter:image" content="{encodedImageUrl}">
            <meta http-equiv="refresh" content="0;url={encodedRedirectUrl}">
            <script>location.replace({redirectUrlJson});</script>
            </head>
            <body>
            <p>Redirecting to <a href="{encodedRedirectUrl}">Mirage</a>&hellip;</p>
            </body>
            </html>
            """;
    }
}
