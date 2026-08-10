using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace Mirage.Infrastructure.Email;

/// <summary>Byline shown under the headline. <paramref name="AvatarUrl"/> falls back to a monogram tile when the
/// author has no photograph, so the block never collapses into a broken image.</summary>
public sealed record NewsletterAuthor(string Name, string? AvatarUrl);

public sealed record NewsletterSocialLink(string Label, string Url);

/// <summary>
/// The Mirage Journal email. Deliberately table-based with inline styles: Outlook ignores flexbox, grid, and most
/// modern CSS, so the layout is built from nested tables and the modern touches (rounded corners, embedded prose
/// styling, the two-column mosaic collapse) are layered on as progressive enhancement.
///
/// Anything that must change on a phone carries a class AND an !important override in the head styles — inline
/// styles beat plain class rules, so a media-query declaration without !important is silently ignored.
/// </summary>
public static class NewsletterEmailTemplate
{
    private const string Ink = "#1b1424";
    private const string Plum = "#6c4bd1";
    private const string Muted = "#6f6880";
    private const string Paper = "#faf7ff";
    private const string Rule = "#e7e0f4";

    public static string Render(string displayName, string title, string excerpt, string contentHtml,
        IReadOnlyList<string> imageUrls, string newsletterUrl, string unsubscribeUrl,
        NewsletterAuthor? author = null, IReadOnlyList<NewsletterSocialLink>? socials = null)
    {
        var images = imageUrls.Where(IsHttpsUrl).Take(10).ToList();
        var hero = images.FirstOrDefault();
        var gallery = images.Skip(1).ToList();

        var body = new StringBuilder();
        body.Append($"""
          <tr><td style="padding:0 0 8px">
            <div class="eyebrow" style="font:700 11px/1 Helvetica,Arial,sans-serif;letter-spacing:.32em;text-transform:uppercase;color:{Plum}">The Mirage Journal</div>
          </td></tr>
          <tr><td style="padding:14px 0 0">
            <h1 class="display" style="margin:0;font:400 40px/1.1 Georgia,'Times New Roman',serif;color:{Ink};letter-spacing:-.4px">{Encode(title)}</h1>
          </td></tr>
        """);

        if (author is not null) body.Append(Byline(author));

        body.Append($"""
          <tr><td style="padding:20px 0 0">
            <div style="width:56px;height:3px;background:{Plum};border-radius:3px;font-size:0;line-height:0">&nbsp;</div>
          </td></tr>
          <tr><td class="lede" style="padding:22px 0 0;font:400 17px/1.7 Georgia,'Times New Roman',serif;color:#3c3450">
            <span style="color:{Ink};font-weight:700">Hello {Encode(displayName)},</span> {Encode(excerpt)}
          </td></tr>
        """);

        if (hero is not null)
            body.Append($"""
              <tr><td style="padding:30px 0 0">
                <img src="{Encode(hero)}" width="552" alt="" style="display:block;width:100%;max-width:552px;height:auto;border:0;border-radius:18px;outline:none;text-decoration:none" />
              </td></tr>
            """);

        body.Append($"""
          <tr><td class="prose" style="padding:30px 0 0;font:400 16px/1.85 Georgia,'Times New Roman',serif;color:#332c45">{contentHtml}</td></tr>
        """);

        if (gallery.Count > 0) body.Append(Mosaic(gallery));

        body.Append($"""
          <tr><td align="center" style="padding:38px 0 6px">{Button(newsletterUrl, "Read it on Mirage")}</td></tr>
          <tr><td align="center" style="padding:10px 0 0;font:400 13px/1.6 Helvetica,Arial,sans-serif;color:{Muted}">
            Like it, and leave a comment for the community.
          </td></tr>
        """);

        return Shell(excerpt, body.ToString(), socials, unsubscribeUrl);
    }

    public static string PlatformManagerInvite(string inviteUrl, IReadOnlyList<NewsletterSocialLink>? socials = null) => Shell(
        "You have been invited to create and schedule editions of the Mirage Journal.",
        $"""
          <tr><td style="padding:0 0 8px">
            <div class="eyebrow" style="font:700 11px/1 Helvetica,Arial,sans-serif;letter-spacing:.32em;text-transform:uppercase;color:{Plum}">An invitation</div>
          </td></tr>
          <tr><td style="padding:14px 0 0">
            <h1 class="display" style="margin:0;font:400 38px/1.12 Georgia,'Times New Roman',serif;color:{Ink};letter-spacing:-.4px">Shape the stories<br />we send.</h1>
          </td></tr>
          <tr><td style="padding:20px 0 0">
            <div style="width:56px;height:3px;background:{Plum};border-radius:3px;font-size:0;line-height:0">&nbsp;</div>
          </td></tr>
          <tr><td class="lede" style="padding:22px 0 0;font:400 17px/1.75 Georgia,'Times New Roman',serif;color:#3c3450">
            A Mirage platform administrator has invited you to join the editorial team as a <b>Platform Manager</b>.
            You will be able to compose editions with photography, schedule them to the minute, and watch how the
            community responds.
          </td></tr>
          <tr><td style="padding:26px 0 0">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="background:{Paper};border:1px solid {Rule};border-radius:16px">
              <tr><td class="card-pad" style="padding:22px 24px;font:400 15px/1.7 Helvetica,Arial,sans-serif;color:#463d5c">
                <b style="color:{Ink}">What you get</b><br />
                · A rich composer with photographs and blog-style formatting<br />
                · Scheduling that sends at the exact date and time you choose<br />
                · Delivery reports and engagement dashboards
              </td></tr>
            </table>
          </td></tr>
          <tr><td align="center" style="padding:34px 0 6px">{Button(inviteUrl, "Accept the invitation")}</td></tr>
          <tr><td align="center" style="padding:12px 0 0;font:400 13px/1.6 Helvetica,Arial,sans-serif;color:{Muted}">
            This invitation expires in seven days. Sign in with this email address to accept it.
          </td></tr>
        """, socials);

    private const string DefaultSupportEmail = "support@themiragehub.com";

    /// <summary>Reads the shared SocialMedia configuration block and appends the support mailbox. Blank entries are
    /// dropped, so an unconfigured network simply does not appear rather than linking somewhere broken.</summary>
    public static IReadOnlyList<NewsletterSocialLink> SocialLinks(IConfiguration configuration)
    {
        var links = new[] { "Instagram", "Facebook", "X", "LinkedIn" }
            .Select(name => new { Label = name, Url = configuration[$"SocialMedia:{name}"]?.Trim() })
            .Where(x => !string.IsNullOrWhiteSpace(x.Url) && IsHttpsUrl(x.Url!))
            .Select(x => new NewsletterSocialLink(x.Label, x.Url!))
            .ToList();

        var support = configuration["SocialMedia:Email"]?.Trim() is { Length: > 0 } configured
            ? configured
            : DefaultSupportEmail;
        links.Add(new NewsletterSocialLink("Email", $"mailto:{support}"));
        return links;
    }

    // Avatar beside the name, as a fixed-width table cell — email clients have no reliable inline-block.
    private static string Byline(NewsletterAuthor author)
    {
        var initial = Encode(author.Name.Trim().Length > 0 ? author.Name.Trim()[..1].ToUpperInvariant() : "M");
        var avatar = IsHttpsUrl(author.AvatarUrl ?? string.Empty)
            ? $"""<img src="{Encode(author.AvatarUrl!)}" width="46" height="46" alt="" style="display:block;width:46px;height:46px;border:0;border-radius:23px;object-fit:cover;outline:none;text-decoration:none" />"""
            : $"""<div style="width:46px;height:46px;border-radius:23px;background:{Plum};color:#ffffff;font:700 19px/46px Georgia,serif;text-align:center">{initial}</div>""";

        return $"""
          <tr><td style="padding:22px 0 0">
            <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr>
              <td width="46" valign="middle" style="width:46px">{avatar}</td>
              <td valign="middle" style="padding-left:12px;font-family:Helvetica,Arial,sans-serif">
                <div style="font:700 14px/1.3 Helvetica,Arial,sans-serif;color:{Ink}">{Encode(author.Name)}</div>
                <div style="font:400 12px/1.4 Helvetica,Arial,sans-serif;color:{Muted}">Writing for the Mirage Journal</div>
              </td>
            </tr></table>
          </td></tr>
        """;
    }

    // Two-up photo mosaic that stacks to one column on narrow screens via the .col class in the head styles.
    private static string Mosaic(IReadOnlyList<string> images)
    {
        var mosaic = new StringBuilder("""<tr><td style="padding:26px 0 0"><table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0"><tr>""");
        for (var index = 0; index < images.Count; index++)
        {
            if (index > 0 && index % 2 == 0) mosaic.Append("""</tr><tr><td colspan="2" height="12" style="font-size:0;line-height:0">&nbsp;</td></tr><tr>""");
            var pad = index % 2 == 0 && index + 1 < images.Count ? "padding-right:6px" : "padding-left:6px";
            if (images.Count == 1) pad = "padding:0";
            mosaic.Append($"""<td class="col" width="50%" valign="top" style="{pad}"><img src="{Encode(images[index])}" width="270" alt="" style="display:block;width:100%;height:auto;border:0;border-radius:14px;outline:none;text-decoration:none" /></td>""");
        }
        if (images.Count % 2 == 1) mosaic.Append("""<td class="col" width="50%">&nbsp;</td>""");
        return mosaic.Append("</tr></table></td></tr>").ToString();
    }

    // Bulletproof button: VML fills the shape for Outlook, everyone else gets the padded anchor.
    private static string Button(string url, string label) => $"""
      <!--[if mso]><v:roundrect xmlns:v="urn:schemas-microsoft-com:vml" xmlns:w="urn:schemas-microsoft-com:office:word" href="{Encode(url)}" style="height:48px;v-text-anchor:middle;width:250px" arcsize="50%" stroke="f" fillcolor="{Plum}"><w:anchorlock/><center style="color:#ffffff;font-family:Helvetica,Arial,sans-serif;font-size:15px;font-weight:bold">{Encode(label)}</center></v:roundrect><![endif]-->
      <!--[if !mso]><!--><a class="cta" href="{Encode(url)}" style="display:inline-block;background:{Plum};color:#ffffff;text-decoration:none;font:700 15px/1 Helvetica,Arial,sans-serif;padding:17px 34px;border-radius:999px">{Encode(label)}</a><!--<![endif]-->
    """;

    private static string SocialRow(IReadOnlyList<NewsletterSocialLink> socials) => socials.Count == 0
        ? string.Empty
        : $"""
          <div style="padding:4px 0 14px">
            {string.Join($"""<span style="color:{Rule}">&nbsp;·&nbsp;</span>""", socials.Select(link =>
                $"""<a href="{Encode(link.Url)}" style="color:{Plum};text-decoration:none;font:700 12px/1.9 Helvetica,Arial,sans-serif;white-space:nowrap">{Encode(link.Label)}</a>"""))}
          </div>
        """;

    // Kept token-substituted rather than interpolated: CSS braces and raw-string interpolation do not mix cleanly.
    // Every mobile declaration is !important because the elements it targets also carry inline styles.
    private static readonly string HeadStyles = """
          body,table,td,a{-webkit-text-size-adjust:100%;-ms-text-size-adjust:100%}
          img{-ms-interpolation-mode:bicubic}
          .prose p{margin:0 0 18px}
          .prose h2{font:400 27px/1.25 Georgia,'Times New Roman',serif;color:@ink@;margin:34px 0 14px;letter-spacing:-.2px}
          .prose h3{font:700 19px/1.35 Helvetica,Arial,sans-serif;color:@ink@;margin:28px 0 10px}
          .prose a{color:@plum@}
          .prose img{max-width:100%;height:auto;border-radius:14px;margin:10px 0}
          .prose ul,.prose ol{margin:0 0 18px;padding-left:22px}
          .prose li{margin:0 0 8px}
          .prose blockquote{margin:26px 0;padding:4px 0 4px 22px;border-left:3px solid @plum@;font:italic 400 19px/1.6 Georgia,serif;color:#463c60}
          .prose hr{border:0;border-top:1px solid @rule@;margin:30px 0}
          @media only screen and (max-width:620px){
            .shell{padding:0 !important}
            .frame{width:100% !important;border-radius:0 !important}
            .bar{padding:16px 22px !important}
            .pad{padding:30px 22px 34px !important}
            .foot{padding:24px 22px !important}
            .card-pad{padding:18px 18px !important}
            .display{font-size:29px !important;line-height:1.14 !important;letter-spacing:-.2px !important}
            .lede{font-size:16px !important;line-height:1.65 !important}
            .eyebrow{letter-spacing:.22em !important;font-size:10px !important}
            .prose{font-size:16px !important}
            .prose h2{font-size:23px !important;margin-top:28px !important}
            .prose h3{font-size:18px !important}
            .prose blockquote{font-size:17px !important;padding-left:16px !important}
            .cta{display:block !important;padding:16px 18px !important;text-align:center !important}
            .col{display:block !important;width:100% !important;padding:0 0 12px !important}
            .tagline{font-size:9px !important;letter-spacing:.18em !important}
          }
        """.Replace("@ink@", Ink).Replace("@plum@", Plum).Replace("@rule@", Rule);

    private static string Shell(string preheader, string bodyRows,
        IReadOnlyList<NewsletterSocialLink>? socials = null, string? unsubscribeUrl = null)
    {
        var footerNote = unsubscribeUrl is null ? string.Empty : $"""
          <div style="padding-top:12px">You are receiving this because you subscribed to the Mirage Journal.<br />
            <a href="{Encode(unsubscribeUrl)}" style="color:{Plum};text-decoration:underline">Unsubscribe</a> ·
            <a href="{Encode(unsubscribeUrl)}" style="color:{Muted};text-decoration:underline">Manage preferences</a>
          </div>
        """;
        return $"""
      <!doctype html>
      <html lang="en" xmlns:v="urn:schemas-microsoft-com:vml" xmlns:o="urn:schemas-microsoft-com:office:office">
      <head>
        <meta charset="utf-8" />
        <meta name="viewport" content="width=device-width,initial-scale=1" />
        <meta name="x-apple-disable-message-reformatting" />
        <meta name="color-scheme" content="light" />
        <meta name="supported-color-schemes" content="light" />
        <!--[if mso]><xml><o:OfficeDocumentSettings><o:PixelsPerInch>96</o:PixelsPerInch></o:OfficeDocumentSettings></xml><![endif]-->
        <style>
        {HeadStyles}
        </style>
      </head>
      <body style="margin:0;padding:0;background:#efeaf8">
        <div style="display:none;max-height:0;overflow:hidden;opacity:0;color:transparent;height:0;width:0">{Encode(preheader)}</div>
        <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="background:#efeaf8">
          <tr><td class="shell" align="center" style="padding:34px 12px">
            <table role="presentation" class="frame" width="640" cellpadding="0" cellspacing="0" border="0" style="width:640px;max-width:640px;background:#ffffff;border-radius:24px;overflow:hidden;box-shadow:0 20px 60px rgba(41,26,74,.14)">
              <tr><td class="bar" style="background:{Ink};padding:22px 44px">
                <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0"><tr>
                  <td style="font:700 17px/1 Georgia,serif;color:#ffffff;letter-spacing:.22em">MIRAGE</td>
                  <td align="right" class="tagline" style="font:700 10px/1 Helvetica,Arial,sans-serif;letter-spacing:.26em;text-transform:uppercase;color:#b9a4f5">Faith · Love · Becoming</td>
                </tr></table>
              </td></tr>
              <tr><td class="pad" style="padding:44px 44px 48px">
                <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">{bodyRows}</table>
              </td></tr>
              <tr><td class="foot" style="background:{Paper};border-top:1px solid {Rule};padding:28px 44px;font:400 12px/1.7 Helvetica,Arial,sans-serif;color:{Muted}" align="center">
                <div style="font:700 12px/1 Georgia,serif;color:{Ink};letter-spacing:.2em;padding-bottom:12px">MIRAGE</div>
                {SocialRow(socials ?? [])}
                <div>A faith-integrated home for relationships worth building.</div>
                {footerNote}
              </td></tr>
            </table>
          </td></tr>
        </table>
      </body>
      </html>
    """;
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
    private static bool IsHttpsUrl(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
}
