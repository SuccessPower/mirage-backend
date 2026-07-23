using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Mirage.Application.Abstractions;

namespace Mirage.Api.Endpoints;

// Public, unauthenticated HTML endpoint at the exact URL people share
// (themiragehub.com/gatherings/{id}, proxied here by the frontend's Vercel rewrite).
// Link-preview crawlers (WhatsApp, iMessage, etc.) don't execute JS, so the SPA can never hand
// them per-gathering Open Graph tags itself — this endpoint renders a tiny static HTML page with
// the tags baked in, then bounces real browsers straight into the app via a meta-refresh/redirect.
internal static class GatheringShareEndpoints
{
    private const string FallbackImageUrl = "https://www.themiragehub.com/og-default.png";
    private const string SiteUrl = "https://www.themiragehub.com";

    public static void MapGatheringShareEndpoints(this WebApplication app)
    {
        app.MapGet("/gatherings/{id:guid}", Get).AllowAnonymous().ExcludeFromDescription();
    }

    private static async Task<IResult> Get(Guid id, IMirageDbContext db, CancellationToken cancellationToken)
    {
        var share = await db.DateRequests.AsNoTracking()
            .Where(x => x.Id == id)
            .Join(db.Profiles.AsNoTracking(), request => request.RequestorUserId, profile => profile.UserId,
                (request, profile) => new
                {
                    request.Activity,
                    request.Note,
                    request.ImageUrl,
                    request.LocationArea,
                    HostDisplayName = profile.DisplayName
                })
            .SingleOrDefaultAsync(cancellationToken);

        var pageUrl = $"{SiteUrl}/gatherings/{id}";

        if (share is null)
            return Results.Content(
                RenderHtml("Mirage", "A gathering shared on Mirage.", FallbackImageUrl, pageUrl, SiteUrl),
                "text/html");

        var title = $"{share.Activity} — hosted by {share.HostDisplayName}";
        var description = !string.IsNullOrWhiteSpace(share.Note)
            ? share.Note
            : $"Join this gathering in {share.LocationArea} on Mirage.";
        var imageUrl = string.IsNullOrWhiteSpace(share.ImageUrl) ? FallbackImageUrl : share.ImageUrl;
        var redirectUrl = $"{SiteUrl}/calendar/{id}";

        return Results.Content(RenderHtml(title, description, imageUrl, pageUrl, redirectUrl), "text/html");
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
            <meta property="og:type" content="website">
            <meta property="og:title" content="{encodedTitle}">
            <meta property="og:description" content="{encodedDescription}">
            <meta property="og:image" content="{encodedImageUrl}">
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
