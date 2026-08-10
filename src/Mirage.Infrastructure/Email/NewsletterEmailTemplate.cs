using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace Mirage.Infrastructure.Email;

/// <summary>Byline shown under the headline. <paramref name="AvatarUrl"/> falls back to a monogram tile when the
/// author has no photograph, so the block never collapses into a broken image.</summary>
public sealed record NewsletterAuthor(string Name, string? AvatarUrl);

/// <summary>A footer link. <paramref name="IconUrl"/> is optional: when a hosted icon is configured it is used,
/// otherwise the link renders as a lettered badge, which no image blocker can break.</summary>
public sealed record NewsletterSocialLink(string Label, string Url, string Glyph, string? IconUrl = null);

/// <summary>
/// The Mirage Journal email — written to read like a letter rather than a marketing blast: warm paper ground,
/// old-style serif throughout, a drop cap opening the first paragraph, and rule ornaments between movements.
///
/// Table-based with inline styles, because Outlook ignores flexbox, grid, and most modern CSS. Anything that must
/// change on a phone carries a class AND an !important override in the head styles — inline styles beat plain
/// class rules, so a media-query declaration without !important is silently ignored.
/// </summary>
public static class NewsletterEmailTemplate
{
    private const string Ink = "#2a2018";
    private const string Plum = "#6c4bd1";
    private const string Muted = "#7d7261";
    private const string Paper = "#fbf7ef";
    private const string PaperDeep = "#f3ebdd";
    private const string Rule = "#ddd0ba";
    private const string DefaultLogoUrl =
        "https://res.cloudinary.com/dl2z33x6z/image/upload/v1785248851/Asset_3Mirage_obqm6m.png";
    private const string DefaultSupportEmail = "support@themiragehub.com";

    /// <summary>Marks a template that already carries its own complete footer, so the shared branding pass does
    /// not append a second one underneath it.</summary>
    public const string SelfBrandedMarker = "<!--mirage:self-branded-->";

    // Old-style faces first, each with a broad fallback. Email clients cannot be relied on to load a webfont, so
    // the letter's character has to survive on faces that ship with the OS.
    private const string Letter = "'Baskerville','Libre Baskerville','Hoefler Text','Palatino Linotype',Palatino,'Book Antiqua',Georgia,serif";
    private const string Display = "'Baskerville Old Face','Hoefler Text','Big Caslon','Palatino Linotype',Palatino,Georgia,serif";
    private const string Utility = "'Optima','Palatino Linotype',Palatino,Georgia,serif";

    public static string Render(string displayName, string title, string excerpt, string contentHtml,
        IReadOnlyList<string> imageUrls, string newsletterUrl, string unsubscribeUrl,
        NewsletterAuthor? author = null, IReadOnlyList<NewsletterSocialLink>? socials = null,
        string? thumbnailUrl = null, string? logoUrl = null)
    {
        var images = imageUrls.Where(IsHttpsUrl).Take(10).ToList();
        // The chosen thumbnail leads the letter; without one the first gallery photograph steps in.
        var hero = IsHttpsUrl(thumbnailUrl ?? string.Empty) ? thumbnailUrl : images.FirstOrDefault();
        var gallery = images.Where(x => !string.Equals(x, hero, StringComparison.OrdinalIgnoreCase)).ToList();

        var body = new StringBuilder();
        body.Append($"""
          <tr><td align="center" style="padding:0 0 6px">
            <div class="eyebrow" style="font:400 11px/1 {Utility};letter-spacing:.42em;text-transform:uppercase;color:{Plum}">The Mirage Journal</div>
          </td></tr>
          <tr><td align="center" style="padding:14px 0 0">
            <h1 class="display" style="margin:0;font:400 42px/1.12 {Display};color:{Ink};letter-spacing:.4px">{Encode(title)}</h1>
          </td></tr>
          {Ornament()}
        """);

        if (author is not null) body.Append(Byline(author));

        if (hero is not null)
            body.Append($"""
              <tr><td style="padding:28px 0 0">
                <img src="{Encode(hero)}" width="552" alt="" style="display:block;width:100%;max-width:552px;height:auto;border:0;border-radius:6px;outline:none;text-decoration:none" />
              </td></tr>
            """);

        // Drop cap on the salutation, the way a letter opens.
        body.Append($"""
          <tr><td class="lede" style="padding:30px 0 0;font:400 18px/1.75 {Letter};color:#4a4034">
            <span style="float:left;font:400 58px/44px {Display};color:{Plum};padding:6px 10px 0 0">D</span>
            <span style="letter-spacing:.03em;color:{Ink}">earest {Encode(displayName.Trim())},</span>
            {Encode(excerpt)}
          </td></tr>
          <tr><td class="prose" style="padding:26px 0 0;font:400 17px/1.9 {Letter};color:#413729">{contentHtml}</td></tr>
        """);

        if (gallery.Count > 0) body.Append(Mosaic(gallery));

        body.Append($"""
          {Ornament()}
          <tr><td align="center" style="padding:22px 0 6px">{Button(newsletterUrl, "Read it on Mirage")}</td></tr>
          <tr><td align="center" style="padding:12px 0 0;font:italic 400 14px/1.6 {Letter};color:{Muted}">
            Ever yours, the Mirage Journal
          </td></tr>
        """);

        return Shell(excerpt, body.ToString(), socials, unsubscribeUrl, logoUrl);
    }

    public static string PlatformManagerInvite(string inviteUrl, IReadOnlyList<NewsletterSocialLink>? socials = null,
        string? logoUrl = null) => Shell(
        "You have been invited to create and schedule editions of the Mirage Journal.",
        $"""
          <tr><td align="center" style="padding:0 0 6px">
            <div class="eyebrow" style="font:400 11px/1 {Utility};letter-spacing:.42em;text-transform:uppercase;color:{Plum}">An invitation</div>
          </td></tr>
          <tr><td align="center" style="padding:14px 0 0">
            <h1 class="display" style="margin:0;font:400 40px/1.14 {Display};color:{Ink};letter-spacing:.4px">Shape the stories<br />we send.</h1>
          </td></tr>
          {Ornament()}
          <tr><td class="lede" style="padding:22px 0 0;font:400 18px/1.8 {Letter};color:#4a4034">
            A Mirage platform administrator has invited you to join the editorial team as a <b>Platform Manager</b>.
            You will be able to compose editions with photography, schedule them to the minute, and watch how the
            community responds.
          </td></tr>
          <tr><td style="padding:26px 0 0">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="background:{PaperDeep};border:1px solid {Rule};border-radius:6px">
              <tr><td class="card-pad" style="padding:22px 24px;font:400 16px/1.8 {Letter};color:#4a4034">
                <b style="color:{Ink};font-variant:small-caps;letter-spacing:.06em">What you get</b><br />
                &#8212; A rich composer with photographs and blog-style formatting<br />
                &#8212; Scheduling that sends at the exact date and time you choose<br />
                &#8212; Delivery reports and engagement dashboards
              </td></tr>
            </table>
          </td></tr>
          <tr><td align="center" style="padding:32px 0 6px">{Button(inviteUrl, "Accept the invitation")}</td></tr>
          <tr><td align="center" style="padding:12px 0 0;font:italic 400 14px/1.6 {Letter};color:{Muted}">
            This invitation expires in seven days. Sign in with this email address to accept it.
          </td></tr>
        """, socials, null, logoUrl);

    /// <summary>"Ada from The Mirage Hub" — the author's first name in front of the brand, so a newsletter looks
    /// like it came from a person. Falls back to the brand alone when there is no author to name.</summary>
    public static string? SenderName(string? authorName, IConfiguration configuration)
    {
        var brand = configuration["Brand:SenderSuffix"]?.Trim() is { Length: > 0 } configured
            ? configured
            : "The Mirage Hub";
        var firstName = (authorName ?? string.Empty).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        return string.IsNullOrWhiteSpace(firstName) ? brand : $"{firstName} from {brand}";
    }

    public static string LogoUrl(IConfiguration configuration) =>
        configuration["Brand:LogoUrl"]?.Trim() is { Length: > 0 } configured ? configured : DefaultLogoUrl;

    /// <summary>Reads the shared SocialMedia block and appends the support mailbox. Each network may also carry an
    /// icon URL under <c>SocialMedia:Icons:{Name}</c>; without one the link renders as a lettered badge, which
    /// survives the image blocking most mail clients apply by default.</summary>
    public static IReadOnlyList<NewsletterSocialLink> SocialLinks(IConfiguration configuration)
    {
        var networks = new (string Name, string Glyph)[]
            { ("Instagram", "IG"), ("Facebook", "f"), ("X", "X"), ("LinkedIn", "in") };
        var links = networks
            .Select(x => new { x.Name, x.Glyph, Url = configuration[$"SocialMedia:{x.Name}"]?.Trim() })
            .Where(x => IsHttpsUrl(x.Url ?? string.Empty))
            .Select(x => new NewsletterSocialLink(x.Name, x.Url!, x.Glyph, IconUrlFor(configuration, x.Name)))
            .ToList();

        var support = configuration["SocialMedia:Email"]?.Trim() is { Length: > 0 } configured
            ? configured
            : DefaultSupportEmail;
        links.Add(new NewsletterSocialLink("Email", $"mailto:{support}", "@", IconUrlFor(configuration, "Email")));
        return links;
    }

    private static string? IconUrlFor(IConfiguration configuration, string name) =>
        IsHttpsUrl(configuration[$"SocialMedia:Icons:{name}"]?.Trim() ?? string.Empty)
            ? configuration[$"SocialMedia:Icons:{name}"]!.Trim()
            : null;

    private static string Ornament() => $"""
      <tr><td align="center" style="padding:18px 0 0">
        <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr>
          <td style="width:62px;border-bottom:1px solid {Rule};font-size:0;line-height:0">&nbsp;</td>
          <td style="padding:0 10px;font:400 13px/1 {Display};color:{Plum}">&#10087;</td>
          <td style="width:62px;border-bottom:1px solid {Rule};font-size:0;line-height:0">&nbsp;</td>
        </tr></table>
      </td></tr>
    """;

    // Avatar beside the name, as a fixed-width table cell — email clients have no reliable inline-block.
    private static string Byline(NewsletterAuthor author)
    {
        var avatar = IsHttpsUrl(author.AvatarUrl ?? string.Empty)
            ? $"""<img src="{Encode(author.AvatarUrl!)}" width="46" height="46" alt="" style="display:block;width:46px;height:46px;border:0;border-radius:23px;object-fit:cover;outline:none;text-decoration:none" />"""
            : $"""<div style="width:46px;height:46px;border-radius:23px;background:{Plum};color:#ffffff;font:400 20px/46px {Display};text-align:center">{Encode(FirstLetter(author.Name, "M"))}</div>""";

        return $"""
          <tr><td align="center" style="padding:20px 0 0">
            <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr>
              <td width="46" valign="middle" style="width:46px">{avatar}</td>
              <td valign="middle" align="left" style="padding-left:12px">
                <div style="font:italic 400 13px/1.3 {Letter};color:{Muted}">written by</div>
                <div style="font:400 17px/1.35 {Display};color:{Ink};letter-spacing:.3px">{Encode(author.Name)}</div>
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
            mosaic.Append($"""<td class="col" width="50%" valign="top" style="{pad}"><img src="{Encode(images[index])}" width="270" alt="" style="display:block;width:100%;height:auto;border:0;border-radius:5px;outline:none;text-decoration:none" /></td>""");
        }
        if (images.Count % 2 == 1) mosaic.Append("""<td class="col" width="50%">&nbsp;</td>""");
        return mosaic.Append("</tr></table></td></tr>").ToString();
    }

    // Bulletproof button: VML fills the shape for Outlook, everyone else gets the padded anchor.
    private static string Button(string url, string label) => $"""
      <!--[if mso]><v:roundrect xmlns:v="urn:schemas-microsoft-com:vml" xmlns:w="urn:schemas-microsoft-com:office:word" href="{Encode(url)}" style="height:50px;v-text-anchor:middle;width:250px" arcsize="8%" stroke="f" fillcolor="{Plum}"><w:anchorlock/><center style="color:#ffffff;font-family:Georgia,serif;font-size:15px">{Encode(label)}</center></v:roundrect><![endif]-->
      <!--[if !mso]><!--><a class="cta" href="{Encode(url)}" style="display:inline-block;background:{Plum};color:#ffffff;text-decoration:none;font:400 16px/1 {Display};letter-spacing:.06em;padding:18px 34px;border-radius:4px">{Encode(label)}</a><!--<![endif]-->
    """;

    // Round badges rather than text links. A configured PNG wins; the lettered fallback needs no image at all.
    private static string SocialRow(IReadOnlyList<NewsletterSocialLink> socials)
    {
        if (socials.Count == 0) return string.Empty;
        var cells = new StringBuilder();
        foreach (var link in socials)
        {
            var inner = IsHttpsUrl(link.IconUrl ?? string.Empty)
                ? $"""<img src="{Encode(link.IconUrl!)}" width="20" height="20" alt="{Encode(link.Label)}" style="display:block;width:20px;height:20px;border:0" />"""
                : $"""<span style="font:400 13px/34px {Utility};color:#ffffff;letter-spacing:.02em">{Encode(link.Glyph)}</span>""";
            cells.Append($"""
              <td style="padding:0 5px">
                <a href="{Encode(link.Url)}" title="{Encode(link.Label)}" style="text-decoration:none">
                  <table role="presentation" cellpadding="0" cellspacing="0" border="0" style="width:34px;height:34px;background:{Ink};border-radius:17px">
                    <tr><td align="center" valign="middle" height="34" style="height:34px;text-align:center">{inner}</td></tr>
                  </table>
                </a>
              </td>
            """);
        }
        return $"""
          <table role="presentation" cellpadding="0" cellspacing="0" border="0" align="center" style="margin:0 auto 16px"><tr>{cells}</tr></table>
        """;
    }

    // Kept token-substituted rather than interpolated: CSS braces and raw-string interpolation do not mix cleanly.
    // Every mobile declaration is !important because the elements it targets also carry inline styles.
    private static readonly string HeadStyles = """
          body,table,td,a{-webkit-text-size-adjust:100%;-ms-text-size-adjust:100%}
          img{-ms-interpolation-mode:bicubic}
          .prose p{margin:0 0 20px}
          .prose h2{font:400 28px/1.3 @display@;color:@ink@;margin:36px 0 14px;letter-spacing:.3px;text-align:center}
          .prose h3{font:400 21px/1.4 @display@;color:@ink@;margin:28px 0 10px;font-variant:small-caps;letter-spacing:.08em}
          .prose a{color:@plum@}
          .prose img{max-width:100%;height:auto;border-radius:5px;margin:12px 0}
          .prose ul,.prose ol{margin:0 0 20px;padding-left:24px}
          .prose li{margin:0 0 9px}
          .prose blockquote{margin:28px 24px;padding:0;border:0;font:italic 400 20px/1.7 @letter@;color:#5b4f3e;text-align:center}
          .prose hr{border:0;border-top:1px solid @rule@;margin:32px 0}
          @media only screen and (max-width:620px){
            .shell{padding:0 !important}
            .frame{width:100% !important;border-radius:0 !important}
            .bar{padding:18px 22px !important}
            .pad{padding:32px 24px 36px !important}
            .foot{padding:26px 22px !important}
            .card-pad{padding:18px 18px !important}
            .display{font-size:31px !important;line-height:1.16 !important}
            .lede{font-size:17px !important;line-height:1.7 !important}
            .eyebrow{letter-spacing:.3em !important;font-size:10px !important}
            .prose{font-size:16px !important}
            .prose h2{font-size:24px !important;margin-top:30px !important}
            .prose h3{font-size:19px !important}
            .prose blockquote{font-size:18px !important;margin:24px 8px !important}
            .cta{display:block !important;padding:17px 18px !important;text-align:center !important}
            .col{display:block !important;width:100% !important;padding:0 0 12px !important}
            .logo{height:26px !important}
            .tagline{font-size:9px !important;letter-spacing:.22em !important}
          }
        """.Replace("@ink@", Ink).Replace("@plum@", Plum).Replace("@rule@", Rule)
           .Replace("@letter@", Letter).Replace("@display@", Display);

    private static string Shell(string preheader, string bodyRows,
        IReadOnlyList<NewsletterSocialLink>? socials = null, string? unsubscribeUrl = null, string? logoUrl = null)
    {
        var logo = IsHttpsUrl(logoUrl ?? string.Empty) ? logoUrl! : DefaultLogoUrl;
        var footerNote = unsubscribeUrl is null ? string.Empty : $"""
          <div style="padding-top:12px">You are receiving this because you subscribed to the Mirage Journal.<br />
            <a href="{Encode(unsubscribeUrl)}" style="color:{Plum};text-decoration:underline">Unsubscribe</a> &#183;
            <a href="{Encode(unsubscribeUrl)}" style="color:{Muted};text-decoration:underline">Manage preferences</a>
          </div>
        """;
        return $"""
      {SelfBrandedMarker}
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
      <body style="margin:0;padding:0;background:{PaperDeep}">
        <div style="display:none;max-height:0;overflow:hidden;opacity:0;color:transparent;height:0;width:0">{Encode(preheader)}</div>
        <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="background:{PaperDeep}">
          <tr><td class="shell" align="center" style="padding:34px 12px">
            <table role="presentation" class="frame" width="640" cellpadding="0" cellspacing="0" border="0" style="width:640px;max-width:640px;background:{Paper};border:1px solid {Rule};border-radius:8px;overflow:hidden">
              <tr><td class="bar" style="background:{Ink};padding:22px 44px">
                <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0"><tr>
                  <td align="left">
                    <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr>
                      <td valign="middle" style="line-height:0;padding-right:10px">
                        <img class="logo" src="{Encode(logo)}" alt="Mirage" height="28" style="display:block;height:28px;width:auto;border:0;outline:none;text-decoration:none" />
                      </td>
                      <td valign="middle" style="font:700 17px/1 Georgia,serif;color:#ffffff;letter-spacing:.22em">MIRAGE</td>
                    </tr></table>
                  </td>
                  <td align="right" class="tagline" style="font:400 10px/1 {Utility};letter-spacing:.3em;text-transform:uppercase;color:#c3b3f0">Faith &#183; Love &#183; Becoming</td>
                </tr></table>
              </td></tr>
              <tr><td class="pad" style="padding:46px 44px 48px">
                <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">{bodyRows}</table>
              </td></tr>
              <tr><td class="foot" style="background:{PaperDeep};border-top:1px solid {Rule};padding:30px 44px;font:400 12px/1.75 {Utility};color:{Muted}" align="center">
                <table role="presentation" cellpadding="0" cellspacing="0" border="0" align="center" style="margin:0 auto 16px"><tr>
                  <td valign="middle" style="line-height:0;padding-right:9px">
                    <img class="logo" src="{Encode(logo)}" alt="Mirage" height="24" style="display:block;height:24px;width:auto;border:0;outline:none;text-decoration:none" />
                  </td>
                  <td valign="middle" style="font:700 13px/1 Georgia,serif;color:{Ink};letter-spacing:.2em">MIRAGE</td>
                </tr></table>
                {SocialRow(socials ?? [])}
                <div style="font:italic 400 13px/1.7 {Letter};color:{Muted}">A faith-integrated home for relationships worth building.</div>
                {footerNote}
              </td></tr>
            </table>
          </td></tr>
        </table>
      </body>
      </html>
    """;
    }

    private static string FirstLetter(string value, string fallback)
    {
        var trimmed = value.Trim();
        return trimmed.Length > 0 ? trimmed[..1].ToUpperInvariant() : fallback;
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
    private static bool IsHttpsUrl(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
}
